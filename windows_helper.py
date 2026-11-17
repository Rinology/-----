import ctypes
import time
import pyperclip
from ctypes import wintypes

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

VK_CONTROL = 0x11
VK_V = 0x56
VK_MENU = 0x12  # ALT
KEYEVENTF_KEYUP = 0x0002


def get_foreground_window() -> int:
    return user32.GetForegroundWindow()


def force_set_foreground_window(hwnd: int) -> bool:
    """Windows에서 다른 프로세스의 창으로 포커스를 강제로 확실히 넘기는 기법"""
    if not hwnd or not user32.IsWindow(hwnd):
        return False

    current_foreground = user32.GetForegroundWindow()
    if current_foreground == hwnd:
        return True

    current_thread = kernel32.GetCurrentThreadId()
    target_thread = user32.GetWindowThreadProcessId(hwnd, None)

    # 스레드 입력 연결
    user32.AttachThreadInput(current_thread, target_thread, True)

    # Alt 키 탭 트릭으로 윈도우 포커스 잠금 해제
    user32.keybd_event(VK_MENU, 0, 0, 0)
    user32.keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0)

    user32.SetForegroundWindow(hwnd)
    user32.BringWindowToTop(hwnd)

    user32.AttachThreadInput(current_thread, target_thread, False)
    return True


def paste_text_to_window(text: str, target_hwnd: int, restore_clipboard: bool = True, delay_ms: int = 70):
    """지정된 창에 텍스트를 클립보드로 붙여넣기"""
    original_clipboard = None
    if restore_clipboard:
        try:
            original_clipboard = pyperclip.paste()
        except Exception:
            original_clipboard = None

    # 새 텍스트 클립보드에 복사
    pyperclip.copy(text)

    # 대상 창 포커스 복원
    force_set_foreground_window(target_hwnd)
    time.sleep(delay_ms / 1000.0)

    # Ctrl + V 전송
    user32.keybd_event(VK_CONTROL, 0, 0, 0)
    user32.keybd_event(VK_V, 0, 0, 0)
    time.sleep(0.02)
    user32.keybd_event(VK_V, 0, KEYEVENTF_KEYUP, 0)
    user32.keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0)

    # 대상 프로그램이 클립보드를 읽을 시간 대기 후 클립보드 원복
    if restore_clipboard and original_clipboard is not None:
        time.sleep(0.2)
        try:
            pyperclip.copy(original_clipboard)
        except Exception:
            pass
