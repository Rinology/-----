using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ChatHelper.Services;

public class TrayIconService : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 101;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private static IntPtr GetAppIconHandle()
    {
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                if (hIcon != IntPtr.Zero) return hIcon;
            }
        }
        catch { }

        try
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            var hMod = Win32Api.GetModuleHandle(curProcess.MainModule?.ModuleName);
            IntPtr modIcon = LoadIcon(hMod, (IntPtr)1);
            if (modIcon != IntPtr.Zero) return modIcon;
        }
        catch { }

        return LoadIcon(IntPtr.Zero, (IntPtr)32512);
    }

    private readonly Window _parentWindow;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _isCreated = false;

    public event Action? OpenStatusWindowRequested;
    public event Action? OpenControlPanelRequested;
    public event Action? OpenQuickSearchRequested;
    public event Action? ReloadTemplatesRequested;
    public event Action? SyncRemoteRequested;
    public event Action? ReloginRequested;
    public event Action? ExitRequested;

    public TrayIconService(Window parentWindow)
    {
        _parentWindow = parentWindow;
    }

    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(_parentWindow).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = GetAppIconHandle(),
            szTip = "ChatHelper (실행 중 - 클릭하여 열기)"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public void ShowBalloon(string title, string message)
    {
        if (!_isCreated) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_INFO,
            szInfo = message,
            szInfoTitle = title,
            dwInfoFlags = 0x00000001
        };

        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);

            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                handled = true;
                _parentWindow.Dispatcher.BeginInvoke(() => OpenControlPanelRequested?.Invoke());
            }
            else if (mouseMsg == WM_LBUTTONUP)
            {
                handled = true;
                _parentWindow.Dispatcher.BeginInvoke(() => OpenStatusWindowRequested?.Invoke());
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                handled = true;
                ShowNativeContextMenu();
            }
        }

        return IntPtr.Zero;
    }

    private void ShowNativeContextMenu()
    {
        IntPtr hMenu = Win32Api.CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 1, "💻 ChatHelper 제어판 열기 (설정/커맨드)");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 2, "🖥️ 상태표시창 보이기");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 3, "🔍 빠른 검색 열기 (Alt+S)");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_SEPARATOR, 0, "");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 4, "🔄 CSV 템플릿 다시 불러오기");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 5, "☁️ 구글 시트에서 최신 동기화");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 6, "🔑 다른 사용자로 로그온...");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_SEPARATOR, 0, "");
            Win32Api.AppendMenu(hMenu, Win32Api.MF_STRING, 99, "❌ 프로그램 종료");

            Win32Api.GetCursorPos(out var pt);
            Win32Api.SetForegroundWindow(_hwnd);

            int cmd = Win32Api.TrackPopupMenu(
                hMenu,
                Win32Api.TPM_RETURNCMD | Win32Api.TPM_NONOTIFY | Win32Api.TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);

            if (cmd > 0)
            {
                _parentWindow.Dispatcher.BeginInvoke(() =>
                {
                    switch (cmd)
                    {
                        case 1: OpenControlPanelRequested?.Invoke(); break;
                        case 2: OpenStatusWindowRequested?.Invoke(); break;
                        case 3: OpenQuickSearchRequested?.Invoke(); break;
                        case 4: ReloadTemplatesRequested?.Invoke(); break;
                        case 5: SyncRemoteRequested?.Invoke(); break;
                        case 6: ReloginRequested?.Invoke(); break;
                        case 99: ExitRequested?.Invoke(); break;
                    }
                });
            }
        }
        finally
        {
            Win32Api.DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hwnd,
                uID = 1001
            };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _isCreated = false;
        }

        _hwndSource?.RemoveHook(WndProc);
    }
}
