import ctypes
import time
import pyperclip
from ctypes import wintypes
from typing import Tuple

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

VK_BACK = 0x08
VK_CONTROL = 0x11
VK_V = 0x56
VK_MENU = 0x12  # ALT
KEYEVENTF_KEYUP = 0x0002

GWL_EXSTYLE = -20
WS_EX_NOACTIVATE = 0x08000000
WS_EX_TOPMOST = 0x00000008
WS_EX_TOOLWINDOW = 0x00000080


class GUITHREADINFO(ctypes.Structure):
    _fields_ = [
        ('cbSize', wintypes.DWORD),
        ('flags', wintypes.DWORD),
        ('hwndActive', wintypes.HWND),
        ('hwndFocus', wintypes.HWND),
        ('hwndCapture', wintypes.HWND),
        ('hwndMenuOwner', wintypes.HWND),
        ('hwndMoveSize', wintypes.HWND),
        ('hwndCaret', wintypes.HWND),
        ('rcCaret', wintypes.RECT)
    ]


class POINT(ctypes.Structure):
    _fields_ = [('x', ctypes.c_long), ('y', ctypes.c_long)]


def make_window_non_activating(hwnd: int):
    """지정된 윈도우가 마우스 클릭이나 팝업 시 포커스를 뺏지 않도록(WS_EX_NOACTIVATE) 설정"""
    try:
        ex_style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex_style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST)
    except Exception as e:
        print(f"[make_window_non_activating 오류] {e}")


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


def get_caret_or_cursor_pos() -> Tuple[int, int]:
    """
    현재 입력 중인 텍스트 커서(Caret) 위치를 획득.
    표준 Win32 Caret이 없을 경우(웹 브라우저, 카톡 등) 현재 마우스 커서 위치를 반환.
    """
    hwnd = user32.GetForegroundWindow()
    if hwnd:
        tid = user32.GetWindowThreadProcessId(hwnd, None)
        gui = GUITHREADINFO()
        gui.cbSize = ctypes.sizeof(GUITHREADINFO)

        if user32.GetGUIThreadInfo(tid, ctypes.byref(gui)):
            target_hwnd = gui.hwndCaret if gui.hwndCaret else gui.hwndFocus
            if target_hwnd:
                pt = POINT(gui.rcCaret.left, gui.rcCaret.bottom)
                if (gui.rcCaret.left != 0 or gui.rcCaret.top != 0 or
                        gui.rcCaret.right != 0 or gui.rcCaret.bottom != 0):
                    user32.ClientToScreen(target_hwnd, ctypes.byref(pt))
                    if pt.x > 0 and pt.y > 0:
                        return pt.x, pt.y

    # Fallback: 마우스 커서 위치
    m_pt = POINT()
    user32.GetCursorPos(ctypes.byref(m_pt))
    return m_pt.x, m_pt.y


def send_backspaces(count: int, delay: float = 0.01):
    """지정된 개수만큼 백스페이스 키 입력 전송"""
    for _ in range(count):
        user32.keybd_event(VK_BACK, 0, 0, 0)
        time.sleep(0.005)
        user32.keybd_event(VK_BACK, 0, KEYEVENTF_KEYUP, 0)
        if delay > 0:
            time.sleep(delay)


def paste_text_to_window(text: str, target_hwnd: int, restore_clipboard: bool = True, delay_ms: int = 60):
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


def replace_text_at_cursor(delete_count: int, new_text: str, target_hwnd: int, restore_clipboard: bool = True):
    """커서 위치에서 직전 입력된 글자들을 지우고 새 문장으로 치환"""
    force_set_foreground_window(target_hwnd)
    time.sleep(0.03)

    if delete_count > 0:
        send_backspaces(delete_count)
        time.sleep(0.03)

    paste_text_to_window(new_text, target_hwnd, restore_clipboard=restore_clipboard, delay_ms=40)
