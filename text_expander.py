import time
import threading
from typing import Optional, Callable, List
from pynput import keyboard
from template_manager import TemplateManager, TemplateItem
import windows_helper

# 영문 자판 -> 한글 두벌식 자모 변환 딕셔너리
ENG_TO_KOR_MAP = {
    'q': 'ㅂ', 'w': 'ㅈ', 'e': 'ㄷ', 'r': 'ㄱ', 't': 'ㅅ', 'y': 'ㅛ', 'u': 'ㅕ', 'i': 'ㅑ', 'o': 'ㅐ', 'p': 'ㅔ',
    'a': 'ㅁ', 's': 'ㄴ', 'd': 'ㅇ', 'f': 'ㄹ', 'g': 'ㅎ', 'h': 'ㅗ', 'j': 'ㅓ', 'k': 'ㅏ', 'l': 'ㅣ',
    'z': 'ㅋ', 'x': 'ㅌ', 'c': 'ㅊ', 'v': 'ㅍ', 'b': 'ㅠ', 'n': 'ㅜ', 'm': 'ㅡ',
    'Q': 'ㅃ', 'W': 'ㅉ', 'E': 'ㄸ', 'R': 'ㄲ', 'T': 'ㅆ', 'O': 'ㅒ', 'P': 'ㅖ'
}


class TextExpander:
    """
    백그라운드에서 키보드 입력을 모니터링하여:
    1. 실시간 타이핑에 따라 일치하는 추천 후보들을 감지하고 플로팅 바에 전달
    2. Tab 키 또는 즉시 단축어(\ㅇㄴ) 입력 시 전체 문장으로 자동 치환
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
        print("[TextExpander] 단축어 및 실시간 추천 엔진 가동됨")

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
            # 1. Tab 키: 현재 떠 있는 추천 후보 완성 시도
            if key == keyboard.Key.tab:
                if self.on_tab_complete_callback and self.on_tab_complete_callback():
                    # 후보가 있어서 완성 처리됨
                    self.buffer_eng = ""
                    self.buffer_kor = ""
                    return
                # 후보가 없으면 기본 Tab 키 동작 통과

            # 2. 백스페이스 처리
            if key == keyboard.Key.backspace:
                if self.buffer_eng:
                    self.buffer_eng = self.buffer_eng[:-1]
                if self.buffer_kor:
                    self.buffer_kor = self.buffer_kor[:-1]
                self.evaluate_realtime_candidates()
                return

            # 3. 엔터 또는 ESC 키: 버퍼 초기화 및 후보 닫기
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

            # 버퍼에 누적 (최대 30자)
            self.buffer_eng = (self.buffer_eng + char_eng)[-30:]
            self.buffer_kor = (self.buffer_kor + char_kor)[-30:]

            # 즉시 단축어(\ㅇㄴ) 치환 검사
            if self.check_immediate_expansion(char_eng):
                return

            # 실시간 추천 후보 검사 (미니 바 팝업용)
            self.evaluate_realtime_candidates()

    def check_immediate_expansion(self, last_char: str) -> bool:
        """\\ㅇㄴ 같은 즉시 치환 접두사 패턴 확인"""
        config = self.manager.config
        prefix = config.get("expansion_prefix", "\\")

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
                self.perform_replacement(matched_len, item)
                return True

        return False

    def evaluate_realtime_candidates(self):
        """현재 버퍼의 마지막 단어를 기반으로 추천 후보를 찾아 콜백 호출"""
        if not self.on_candidates_callback:
            return

        target_hwnd = windows_helper.get_foreground_window()

        # 마지막 공백 이후의 현재 입력 중인 토큰 추출
        cur_token_kor = self.buffer_kor.split()[-1] if self.buffer_kor.split() else ""
        cur_token_eng = self.buffer_eng.split()[-1] if self.buffer_eng.split() else ""

        prefix = self.manager.config.get("expansion_prefix", "\\")
        # 접두사가 붙어있으면 떼고 검색
        if prefix and cur_token_kor.startswith(prefix):
            cur_token_kor = cur_token_kor[len(prefix):]
        if prefix and cur_token_eng.startswith(prefix):
            cur_token_eng = cur_token_eng[len(prefix):]

        if not cur_token_kor and not cur_token_eng:
            self.on_candidates_callback([], 0, target_hwnd)
            return

        # 매칭 검사
        matched_items: List[TemplateItem] = []
        for item in self.manager.templates:
            kw = item.keyword.strip()
            title = item.title.strip()
            if not kw and not title:
                continue

            # 키워드 시작 일치 또는 초성 일치
            if (kw and kw.startswith(cur_token_kor)) or (kw and kw.lower().startswith(cur_token_eng.lower())):
                matched_items.append(item)
            elif title.startswith(cur_token_kor) or title.lower().startswith(cur_token_eng.lower()):
                matched_items.append(item)

        if matched_items:
            # 매칭된 단어 길이
            matched_len = len(cur_token_kor)
            if prefix and self.buffer_kor.endswith(f"{prefix}{cur_token_kor}"):
                matched_len += len(prefix)
            self.on_candidates_callback(matched_items, matched_len, target_hwnd)
        else:
            self.on_candidates_callback([], 0, target_hwnd)

    def perform_replacement(self, delete_count: int, item: TemplateItem):
        """백스페이스로 축약어를 지우고 전체 문장으로 치환"""
        self.is_replacing = True
        self.buffer_eng = ""
        self.buffer_kor = ""

        # 후보 팝업 숨김
        if self.on_candidates_callback:
            self.on_candidates_callback([], 0, 0)

        def _worker():
            try:
                target_hwnd = windows_helper.get_foreground_window()
                restore_cb = self.manager.config.get("restore_clipboard", True)

                time.sleep(0.04)
                windows_helper.replace_text_at_cursor(
                    delete_count=delete_count,
                    new_text=item.content,
                    target_hwnd=target_hwnd,
                    restore_clipboard=restore_cb
                )

                if self.on_expanded_callback:
                    self.on_expanded_callback(item.title)
            finally:
                time.sleep(0.15)
                self.is_replacing = False

        threading.Thread(target=_worker, daemon=True).start()
