using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class KeyboardHookService : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly TemplateManager _templateManager;

    private IntPtr _hookId = IntPtr.Zero;
    private Win32Api.HookProc? _hookProc;

    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    public event Action<List<TemplateItem>, int, IntPtr>? CandidatesUpdated;
    public event Action? RequestCloseCandidates;
    public event Action? GlobalHotkeyTriggered;
    public event Action<IntPtr>? AiHotkeyTriggered;

    private List<TemplateItem> _currentCandidates = new();
    private int _currentMatchedLen = 0;
    private IntPtr _lastTargetHwnd = IntPtr.Zero;
    private bool _isReplacing = false;
    private bool _triggerArmed = false;

    // 일정 시간(3.5초) 입력이 없으면 추천 알약을 자동으로 숨기는 타이머
    private readonly System.Threading.Timer _autoHideTimer;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public KeyboardHookService(ConfigManager configManager, TemplateManager templateManager)
    {
        _configManager = configManager;
        _templateManager = templateManager;
        _autoHideTimer = new System.Threading.Timer(OnAutoHideTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    private void OnAutoHideTimerElapsed(object? state)
    {
        lock (_lock)
        {
            if (_currentCandidates.Count > 0)
            {
                _buffer.Clear();
                ClearCandidates();
            }
        }
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hMod = Win32Api.GetModuleHandle(curModule?.ModuleName);
        _hookId = Win32Api.SetWindowsHookEx(Win32Api.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        Debug.WriteLine($"[KeyboardHook] 훅 설치 완료. ID: {_hookId}");
    }

    public void Stop()
    {
        _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_hookId != IntPtr.Zero)
        {
            Win32Api.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Debug.WriteLine("[KeyboardHook] 훅 해제 완료.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)Win32Api.WM_KEYDOWN || wParam == (IntPtr)Win32Api.WM_SYSKEYDOWN) && !_isReplacing)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            bool isAltPressed = (GetAsyncKeyState(Win32Api.VK_MENU) & 0x8000) != 0;
            bool isCtrlPressed = (GetAsyncKeyState(Win32Api.VK_CONTROL) & 0x8000) != 0;
            bool isWinPressed = (GetAsyncKeyState(Win32Api.VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(Win32Api.VK_RWIN) & 0x8000) != 0;
            bool isShiftPressed = (GetAsyncKeyState(Win32Api.VK_SHIFT) & 0x8000) != 0;

            // Alt + Q 수동 검색창 호출
            if (isAltPressed && !isCtrlPressed && vkCode == 0x51)
            {
                GlobalHotkeyTriggered?.Invoke();
                return (IntPtr)1;
            }

            // Alt + X 로컬 AI 상담 비서 호출
            if (isAltPressed && !isCtrlPressed && vkCode == 0x58)
            {
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                AiHotkeyTriggered?.Invoke(fgHwnd);
                return (IntPtr)1;
            }

            // 자동완성이 일시정지(OFF) 상태이면 패스
            if (!_configManager.Config.IsEnabled)
            {
                return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Ctrl, Alt, Win 단축키(예: Ctrl+C, Ctrl+V, Ctrl+A, Ctrl+S 등) 입력 시 버퍼 초기화 및 무시!
            if (isCtrlPressed || isAltPressed || isWinPressed)
            {
                lock (_lock)
                {
                    if (_buffer.Length > 0 || _currentCandidates.Count > 0)
                    {
                        _buffer.Clear();
                        ClearCandidates();
                    }
                }
                return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (ProcessKeyDown(vkCode, isShiftPressed))
            {
                return (IntPtr)1; // 키 가로채기 (Eat)
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ProcessKeyDown(int vkCode, bool isShiftPressed)
    {
        lock (_lock)
        {
            IntPtr currentHwnd = Win32Api.GetForegroundWindow();

            // 활성 창이 바뀌었으면 이전 입력 버퍼 초기화
            if (_lastTargetHwnd != IntPtr.Zero && _lastTargetHwnd != currentHwnd)
            {
                _buffer.Clear();
                ClearCandidates();
            }
            _lastTargetHwnd = currentHwnd;

            string mode = _configManager.Config.CompleteKeyMode ?? "SlashTab";
            bool isSlashHeld = (GetAsyncKeyState(0xDC) & 0x8000) != 0;

            // 1. 추천 칩이 떠 있을 때의 완성 트리거 처리
            if (_currentCandidates.Count > 0)
            {
                // 모드 B: Shift + Space 완성
                if (mode == "ShiftSpace" && isShiftPressed && vkCode == Win32Api.VK_SPACE)
                {
                    CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                    return true;
                }

                // '\' (0xDC) 키가 눌렸을 때
                if (vkCode == 0xDC)
                {
                    if (mode == "SlashOnly")
                    {
                        // '\' 단독 누름 시 즉시 1번 후보 완성
                        CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                        return true;
                    }

                    // SlashTab 모드: 화면에 '\'가 쳐지지 않게 가로채고 완성 준비 상태(_triggerArmed)로 전환
                    _triggerArmed = true;
                    _autoHideTimer.Change(3500, Timeout.Infinite);
                    return true;
                }

                // Tab 키가 눌렸을 때
                if (vkCode == Win32Api.VK_TAB)
                {
                    if (isSlashHeld || _triggerArmed)
                    {
                        CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                        return true;
                    }
                    else
                    {
                        // 일반 Tab 키는 100% 통과시키고 추천 칩만 닫음
                        _buffer.Clear();
                        ClearCandidates();
                        return false;
                    }
                }

                // 1~9 숫자 키 선택 (\ 누른 직후 1~9 또는 \ 누른 채 1~9)
                if (_configManager.Config.EnableNumberSelection && (isSlashHeld || _triggerArmed) && vkCode >= 0x31 && vkCode <= 0x39)
                {
                    int index = vkCode - 0x31;
                    if (index < _currentCandidates.Count)
                    {
                        CompleteSelection(_currentCandidates[index], _currentMatchedLen);
                        return true;
                    }
                }
            }

            _triggerArmed = false;

            // 2. 백스페이스
            if (vkCode == Win32Api.VK_BACK)
            {
                if (_buffer.Length > 0)
                {
                    _buffer.Length--;
                }
                EvaluateCandidates();
                return false;
            }

            // 3. 엔터, ESC, 스페이스, 방향키 -> 단어 종료/이동으로 간주하여 버퍼 및 알약 초기화
            if (vkCode is Win32Api.VK_RETURN or Win32Api.VK_ESCAPE or Win32Api.VK_SPACE
                or Win32Api.VK_LEFT or Win32Api.VK_UP or Win32Api.VK_RIGHT or Win32Api.VK_DOWN)
            {
                _buffer.Clear();
                ClearCandidates();
                return false;
            }

            // 4. 물리 영문 자판 (A-Z, 0x41 ~ 0x5A)
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                // 현재 창이 실제 입력 가능한 창인지 먼저 확인 (자기 자신 창이나 바탕화면이면 무시)
                if (!Win32Api.TryGetValidInputPosition(out _, out _, out _))
                {
                    _buffer.Clear();
                    ClearCandidates();
                    return false;
                }

                char ch = char.ToLower((char)vkCode);
                _buffer.Append(ch);
                if (_buffer.Length > 15)
                {
                    _buffer.Remove(0, _buffer.Length - 15);
                }

                EvaluateCandidates();
                return false;
            }
        }

        return false;
    }

    private void EvaluateCandidates()
    {
        var raw = _buffer.ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 2)
        {
            ClearCandidates();
            return;
        }

        var list = _templateManager.FindMatchingCandidates(raw, out int matchedLen);
        if (list.Count > 0)
        {
            _currentCandidates = list;
            _currentMatchedLen = matchedLen;
            _autoHideTimer.Change(3500, Timeout.Infinite); // 3.5초 뒤 자동 숨김 예약
            CandidatesUpdated?.Invoke(list, matchedLen, _lastTargetHwnd);
        }
        else
        {
            ClearCandidates();
        }
    }

    private void CompleteSelection(TemplateItem item, int deleteCount)
    {
        _isReplacing = true;
        _buffer.Clear();
        _triggerArmed = false;
        ClearCandidates();

        var hwnd = _lastTargetHwnd != IntPtr.Zero ? _lastTargetHwnd : Win32Api.GetForegroundWindow();

        Task.Run(() =>
        {
            try
            {
                TextInjectionService.ReplaceText(deleteCount, item.Content, hwnd);
            }
            finally
            {
                Thread.Sleep(80);
                _isReplacing = false;
            }
        });
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _buffer.Clear();
            ClearCandidates();
        }
    }

    private void ClearCandidates()
    {
        _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _currentCandidates.Clear();
        _currentMatchedLen = 0;
        _triggerArmed = false;
        RequestCloseCandidates?.Invoke();
    }

    public void Dispose()
    {
        _autoHideTimer.Dispose();
        Stop();
    }
}
