import time
import threading
from typing import Optional, Callable, List
from pynput import keyboard
from template_manager import TemplateManager, TemplateItem
import windows_helper

ENG_TO_KOR_MAP = {
    'q': 'ㅂ', 'w': 'ㅈ', 'e': 'ㄷ', 'r': 'ㄱ', 't': 'ㅅ', 'y': 'ㅛ', 'u': 'ㅕ', 'i': 'ㅑ', 'o': 'ㅐ', 'p': 'ㅔ',
    'a': 'ㅁ', 's': 'ㄴ', 'd': 'ㅇ', 'f': 'ㄹ', 'g': 'ㅎ', 'h': 'ㅗ', 'j': 'ㅓ', 'k': 'ㅏ', 'l': 'ㅣ',
    'z': 'ㅋ', 'x': 'ㅌ', 'c': 'ㅊ', 'v': 'ㅍ', 'b': 'ㅠ', 'n': 'ㅜ', 'm': 'ㅡ',
    'Q': 'ㅃ', 'W': 'ㅉ', 'E': 'ㄸ', 'R': 'ㄲ', 'T': 'ㅆ', 'O': 'ㅒ', 'P': 'ㅖ'
}


class TextExpander:
    """
    백그라운드에서 키보드 입력을 모니터링하여:
    1. 실시간 타이핑에 따라 일치하는 추천 후보를 초고속 감지
    2. Tab 키 또는 단축어(\\ㅇㄴ) 입력 시 즉시 10ms 내 초고속 치환
    """
    def __init__(
        self,
        manager: TemplateManager,
        on_candidates_callback: Optional[Callable[[List[TemplateItem], int, int], None]] = None,
        on_tab_complete_callback: Optional[Callable[[], bool]] = None,
        on_expanded_callback: Optional[Callable[[str], None]] = None
    ):
        self.manager = manager
        self.on_candidates_callback = on_candidates_callback
        self.on_tab_complete_callback = on_tab_complete_callback
        self.on_expanded_callback = on_expanded_callback
        self.listener: Optional[keyboard.Listener] = None

        self.buffer_eng = ""
        self.buffer_kor = ""
        self.lock = threading.Lock()
        self.is_replacing = False

    def start(self):
        if self.listener:
            self.stop()

        self.listener = keyboard.Listener(
            on_press=self.on_key_press
        )
        self.listener.daemon = True
        self.listener.start()
        print("[TextExpander] 초고속 실시간 추천 및 단축어 엔진 시작됨")

    def stop(self):
        if self.listener:
            try:
                self.listener.stop()
            except Exception:
                pass
            self.listener = None

    def on_key_press(self, key):
        if self.is_replacing:
            return

        with self.lock:
            # 1. Tab 키: 후보 자동완성 시도
            if key == keyboard.Key.tab:
                if self.on_tab_complete_callback and self.on_tab_complete_callback():
                    self.buffer_eng = ""
                    self.buffer_kor = ""
                    return

            # 2. 백스페이스
            if key == keyboard.Key.backspace:
                if self.buffer_eng:
                    self.buffer_eng = self.buffer_eng[:-1]
                if self.buffer_kor:
                    self.buffer_kor = self.buffer_kor[:-1]
                self.evaluate_realtime_candidates()
                return

            # 3. 엔터 또는 ESC: 초기화
            if key in (keyboard.Key.enter, keyboard.Key.esc):
                self.buffer_eng = ""
                self.buffer_kor = ""
                if self.on_candidates_callback:
                    target_hwnd = windows_helper.get_foreground_window()
                    self.on_candidates_callback([], 0, target_hwnd)
                return

            # 4. 문자 추출
            char_eng = None
            char_kor = None

            if hasattr(key, 'char') and key.char:
                char_eng = key.char
                char_kor = ENG_TO_KOR_MAP.get(char_eng, char_eng)
            elif key == keyboard.Key.space:
                char_eng = " "
                char_kor = " "
            else:
                return

            self.buffer_eng = (self.buffer_eng + char_eng)[-25:]
            self.buffer_kor = (self.buffer_kor + char_kor)[-25:]

            # 즉시 단축어 (\ㅇㄴ) 검사
            if self.check_immediate_expansion(char_eng):
                return

            # 실시간 후보 검사
            self.evaluate_realtime_candidates()

    def check_immediate_expansion(self, last_char: str) -> bool:
        prefix = self.manager.config.get("expansion_prefix", "\\")
        if not prefix:
            return False

        for item in self.manager.templates:
            kw = item.keyword.strip()
            if not kw:
                continue

            target_kor = f"{prefix}{kw}"
            matched = False
            matched_len = 0

            if self.buffer_kor.endswith(target_kor):
                matched = True
                matched_len = len(target_kor)
            elif self.buffer_eng.lower().endswith(target_kor.lower()):
                matched = True
                matched_len = len(target_kor)

            if matched:
                self.perform_fast_replacement(matched_len, item)
                return True

        return False

    def evaluate_realtime_candidates(self):
        if not self.on_candidates_callback:
            return

        target_hwnd = windows_helper.get_foreground_window()

        cur_token_kor = self.buffer_kor.split()[-1] if self.buffer_kor.split() else ""
        cur_token_eng = self.buffer_eng.split()[-1] if self.buffer_eng.split() else ""

        prefix = self.manager.config.get("expansion_prefix", "\\")
        if prefix and cur_token_kor.startswith(prefix):
            cur_token_kor = cur_token_kor[len(prefix):]
        if prefix and cur_token_eng.startswith(prefix):
            cur_token_eng = cur_token_eng[len(prefix):]

        if not cur_token_kor and not cur_token_eng:
            self.on_candidates_callback([], 0, target_hwnd)
            return

        matched_items: List[TemplateItem] = []
        for item in self.manager.templates:
            kw = item.keyword.strip()
            title = item.title.strip()
            if not kw and not title:
                continue

            if (kw and kw.startswith(cur_token_kor)) or (kw and kw.lower().startswith(cur_token_eng.lower())):
                matched_items.append(item)
            elif title.startswith(cur_token_kor) or title.lower().startswith(cur_token_eng.lower()):
                matched_items.append(item)

        if matched_items:
            matched_len = len(cur_token_kor)
            if prefix and self.buffer_kor.endswith(f"{prefix}{cur_token_kor}"):
                matched_len += len(prefix)
            self.on_candidates_callback(matched_items, matched_len, target_hwnd)
        else:
            self.on_candidates_callback([], 0, target_hwnd)

    def perform_fast_replacement(self, delete_count: int, item: TemplateItem):
        """딜레이 없는 초고속 치환"""
        self.is_replacing = True
        self.buffer_eng = ""
        self.buffer_kor = ""

        if self.on_candidates_callback:
            self.on_candidates_callback([], 0, 0)

        target_hwnd = windows_helper.get_foreground_window()

        def _worker():
            try:
                windows_helper.replace_text_at_cursor(
                    delete_count=delete_count,
                    new_text=item.content,
                    target_hwnd=target_hwnd,
                    restore_clipboard=False
                )
                if self.on_expanded_callback:
                    self.on_expanded_callback(item.title)
            finally:
                time.sleep(0.08)
                self.is_replacing = False

        threading.Thread(target=_worker, daemon=True).start()
