import time
import threading
from typing import Optional, Callable
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
    백그라운드에서 키보드 입력을 모니터링하여,
    단축어(예: \\ㅇㄴ, \\계좌 또는 초성 + Space/Tab) 입력 시 즉시 전체 문장으로 치환하는 모듈
    """
    def __init__(self, manager: TemplateManager, on_expanded_callback: Optional[Callable[[str], None]] = None):
        self.manager = manager
        self.on_expanded_callback = on_expanded_callback
        self.listener: Optional[keyboard.Listener] = None

        # 입력 버퍼 (영문 원형 및 한글 변환형)
        self.buffer_eng = ""
        self.buffer_kor = ""
        self.lock = threading.Lock()
        self.is_replacing = False  # 치환 중 키 입력 루프 방지

    def start(self):
        if not self.manager.config.get("enable_text_expansion", True):
            return

        self.listener = keyboard.Listener(
            on_press=self.on_key_press
        )
        self.listener.daemon = True
        self.listener.start()
        print("[TextExpander] 단축어 자동 치환 엔진 시작됨")

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
            # 1. 특수 키 처리
            if key == keyboard.Key.backspace:
                if self.buffer_eng:
                    self.buffer_eng = self.buffer_eng[:-1]
                if self.buffer_kor:
                    self.buffer_kor = self.buffer_kor[:-1]
                return

            if key in (keyboard.Key.enter, keyboard.Key.esc):
                # 엔터나 ESC를 누르면 버퍼 초기화
                self.buffer_eng = ""
                self.buffer_kor = ""
                return

            # 문자 추출
            char_eng = None
            char_kor = None

            if hasattr(key, 'char') and key.char:
                char_eng = key.char
                char_kor = ENG_TO_KOR_MAP.get(char_eng, char_eng)
            elif key == keyboard.Key.space:
                char_eng = " "
                char_kor = " "
            elif key == keyboard.Key.tab:
                char_eng = "\t"
                char_kor = "\t"
            else:
                return

            # 버퍼에 추가 (최대 30자 유지)
            self.buffer_eng = (self.buffer_eng + char_eng)[-30:]
            self.buffer_kor = (self.buffer_kor + char_kor)[-30:]

            # 매칭 검사 실행
            self.check_and_expand(char_eng)

    def check_and_expand(self, last_char: str):
        config = self.manager.config
        prefix = config.get("expansion_prefix", "\\")  # 기본 접두사: \
        trigger_mode = config.get("expansion_trigger", "prefix_or_space")  # prefix_only, space_only, prefix_or_space

        # 템플릿 목록 순회
        for item in self.manager.templates:
            kw = item.keyword.strip()
            if not kw:
                continue

            # Case A: 접두사 방식 (예: \ㅇㄴ 또는 \acc)
            if trigger_mode in ("prefix_only", "prefix_or_space") and prefix:
                target_pat_kor = f"{prefix}{kw}"
                # 버퍼 끝이 접두사+키워드와 일치하는지 확인
                matched = False
                matched_len = 0

                if self.buffer_kor.endswith(target_pat_kor):
                    matched = True
                    matched_len = len(target_pat_kor)
                elif self.buffer_eng.lower().endswith(target_pat_kor.lower()):
                    matched = True
                    matched_len = len(target_pat_kor)

                if matched:
                    self.perform_replacement(matched_len, item)
                    return

            # Case B: 공백/탭 구분자 방식 (예: ㅇㄴ + Space 또는 ㅇㄴ + Tab)
            if trigger_mode in ("space_only", "prefix_or_space") and last_char in (" ", "\t"):
                target_pat_kor = f"{kw}{last_char}"
                matched = False
                matched_len = 0

                if self.buffer_kor.endswith(target_pat_kor):
                    matched = True
                    matched_len = len(target_pat_kor)
                elif self.buffer_eng.lower().endswith(target_pat_kor.lower()):
                    matched = True
                    matched_len = len(target_pat_kor)

                if matched:
                    self.perform_replacement(matched_len, item)
                    return

    def perform_replacement(self, delete_count: int, item: TemplateItem):
        """백스페이스로 축약어를 지우고 전체 문장으로 치환"""
        self.is_replacing = True
        self.buffer_eng = ""
        self.buffer_kor = ""

        def _worker():
            try:
                target_hwnd = windows_helper.get_foreground_window()
                restore_cb = self.manager.config.get("restore_clipboard", True)

                # 한글 IME 조합 해제를 위해 약간의 지연 후 백스페이스 전송
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
