using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ChatHelper.Services;

public static class Win32Api
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // ALT
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_V = 0x56;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint MAPVK_VK_TO_VSC = 0x00;
    public const uint MAPVK_VSC_TO_VK = 0x01;
    public const uint MAPVK_VK_TO_CHAR = 0x02;
    public const uint MAPVK_VSC_TO_VK_EX = 0x03;

    [DllImport("user32.dll")]
    public static extern int MapVirtualKey(uint uCode, uint uMapType);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RIGHTBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;

    /// <summary>
    /// 현재 포커스된 창이 유효한 외부 채팅/문서 입력 창인지 확인하고 위치를 구함.
    /// 메모장뿐만 아니라 크롬, 구글 채팅(Google Chat), 엣지, 일렉트론, 슬랙 등 모던 웹 앱을 100% 지원합니다.
    /// (바탕화면, 작업표시줄, 자기 자신 창만 안전하게 제외)
    /// </summary>
    public static bool TryGetValidInputPosition(out double x, out double y, out IntPtr activeHwnd)
    {
        x = 0;
        y = 0;
        activeHwnd = GetForegroundWindow();
        if (activeHwnd == IntPtr.Zero)
            return false;

        uint tid = GetWindowThreadProcessId(activeHwnd, out uint pid);

        // 1. 자기 자신(ChatHelper 제어판 등) 창이면 추천 알약 띄우지 않음
        if (pid == CurrentProcessId)
            return false;

        // 2. 바탕화면, 작업표시줄, 탐색기 배경 등 비입력 시스템 창 제외
        var sbClass = new StringBuilder(128);
        GetClassName(activeHwnd, sbClass, sbClass.Capacity);
        string className = sbClass.ToString();
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "NotifyIconOverflowWindow")
            return false;

        // 3. 표준 Win32 캐럿(Caret) 위치 시도 (메모장, 카카오톡 기본 입력창 등)
        var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
        if (GetGUIThreadInfo(tid, ref gui))
        {
            IntPtr caretHwnd = gui.hwndCaret != IntPtr.Zero ? gui.hwndCaret : gui.hwndFocus;
            if (caretHwnd != IntPtr.Zero && (gui.rcCaret.Left != 0 || gui.rcCaret.Top != 0 || gui.rcCaret.Bottom != 0))
            {
                var pt = new POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Bottom };
                if (ClientToScreen(caretHwnd, ref pt) && pt.X > 0 && pt.Y > 0)
                {
                    x = pt.X;
                    y = pt.Y;
                    return true;
                }
            }
        }

        // 4. Chromium, Chrome, Google Chat, Electron, Slack 등 모던 브라우저/웹 앱 지원 (Fallback)
        GetCursorPos(out var mPt);
        if (GetWindowRect(activeHwnd, out var rcWin))
        {
            // 마우스 커서가 해당 브라우저/채팅창 내에 있으면 마우스 위치 기준
            if (mPt.X >= rcWin.Left && mPt.X <= rcWin.Right && mPt.Y >= rcWin.Top && mPt.Y <= rcWin.Bottom)
            {
                x = mPt.X;
                y = mPt.Y + 16;
                return true;
            }

            // 마우스가 창 밖에 있으면 창 하단 입력창 위치(바닥에서 80px 위)로 자연스럽게 배치
            x = Math.Max(rcWin.Left + 50, 100);
            y = Math.Max(rcWin.Bottom - 90, rcWin.Top + 100);
            return true;
        }

        // 기본 마우스 위치
        x = mPt.X;
        y = mPt.Y + 16;
        return true;
    }
}
