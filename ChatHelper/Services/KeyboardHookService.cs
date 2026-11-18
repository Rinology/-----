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

    private List<TemplateItem> _currentCandidates = new();
    private int _currentMatchedLen = 0;
    private IntPtr _lastTargetHwnd = IntPtr.Zero;
    private bool _isReplacing = false;

    // 트리거 키(\)가 활성화되어 추천 바가 대기 중인지 여부
    public bool IsTriggerActive { get; private set; } = false;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public KeyboardHookService(ConfigManager configManager, TemplateManager templateManager)
    {
        _configManager = configManager;
        _templateManager = templateManager;
    }

    public void Start()
    {
        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hMod = Win32Api.GetModuleHandle(curModule?.ModuleName);
        _hookId = Win32Api.SetWindowsHookEx(Win32Api.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        Debug.WriteLine($"[KeyboardHook] 훅 설치 완료. ID: {_hookId}");
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32Api.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Debug.WriteLine("[KeyboardHook] 훅 해제 완료.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)Win32Api.WM_KEYDOWN && !_isReplacing)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            // Alt 키 상태 확인 (GetAsyncKeyState VK_MENU: 0x12)
            bool isAltPressed = (GetAsyncKeyState(Win32Api.VK_MENU) & 0x8000) != 0;

            // 0. Alt + Q 전역 수동 검색 단축키
            if (isAltPressed && vkCode == 0x51) // 'Q'
            {
                GlobalHotkeyTriggered?.Invoke();
                return (IntPtr)1; // 원래 키 동작 가로챔
            }

            // 키 처리 및 가로채기(Eat) 여부 결정
            if (ProcessKeyDown(vkCode))
            {
                return (IntPtr)1; // OS 및 채팅창으로 전달되지 않게 차단!
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ProcessKeyDown(int vkCode)
    {
        lock (_lock)
        {
            _lastTargetHwnd = Win32Api.GetForegroundWindow();

            // 1. Tab 키: 추천 후보가 떠 있는 상태에서 완성 키로 가로채기
            if (vkCode == Win32Api.VK_TAB)
            {
                if (_currentCandidates.Count > 0)
                {
                    CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                    return true; // 원래 Tab 키 동작 방지!
                }
                // 후보가 없으면 원래 Tab 키 100% 정상 통과!
                return false;
            }

            // 2. 1~9 숫자 키 선택
            if (_configManager.Config.EnableNumberSelection && _currentCandidates.Count > 0 && vkCode >= 0x31 && vkCode <= 0x39)
            {
                int index = vkCode - 0x31;
                if (index < _currentCandidates.Count)
                {
                    CompleteSelection(_currentCandidates[index], _currentMatchedLen);
                    return true; // 원래 숫자 키 차단
                }
            }

            // 3. 백스페이스
            if (vkCode == Win32Api.VK_BACK)
            {
                if (_buffer.Length > 0)
                {
                    _buffer.Length--;
                }
                EvaluateCandidates();
                return false;
            }

            // 4. 엔터 또는 ESC 키
            if (vkCode == Win32Api.VK_RETURN || vkCode == Win32Api.VK_ESCAPE)
            {
                _buffer.Clear();
                IsTriggerActive = false;
                ClearCandidates();
                return false;
            }

            // 5. 트리거 키 '\' (VK_OEM_5 = 0xDC) 감지
            if (vkCode == 0xDC)
            {
                _buffer.Append('\\');
                IsTriggerActive = true;
                EvaluateCandidates();
                return false;
            }

            // 6. 물리 영문 자판 (A-Z, 0x41 ~ 0x5A) 감지
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                char ch = char.ToLower((char)vkCode);
                _buffer.Append(ch);
                if (_buffer.Length > 20)
                {
                    _buffer.Remove(0, _buffer.Length - 20);
                }

                // 실시간 매칭 검사
                EvaluateCandidates();
                return false;
            }

            // 7. 스페이스바 (공백)
            if (vkCode == Win32Api.VK_SPACE)
            {
                _buffer.Clear();
                IsTriggerActive = false;
                ClearCandidates();
                return false;
            }
        }

        return false;
    }

    private void EvaluateCandidates()
    {
        var raw = _buffer.ToString();
        var trigger = _configManager.Config.TriggerKey ?? "\\";

        // 트리거 모드 체크: 설정된 트리거(\)로 시작하는지 확인
        if (!string.IsNullOrEmpty(trigger))
        {
            int lastTriggerIdx = raw.LastIndexOf(trigger, StringComparison.Ordinal);
            if (lastTriggerIdx < 0)
            {
                ClearCandidates();
                return;
            }

            string tokenAfterTrigger = raw[lastTriggerIdx..]; // 예: "\qt", "\ds"
            var list = _templateManager.FindMatchingCandidates(tokenAfterTrigger, out int matchedLen);
            if (list.Count > 0)
            {
                _currentCandidates = list;
                _currentMatchedLen = matchedLen;
                CandidatesUpdated?.Invoke(list, matchedLen, _lastTargetHwnd);
            }
            else
            {
                ClearCandidates();
            }
        }
    }

    private void CompleteSelection(TemplateItem item, int deleteCount)
    {
        _isReplacing = true;
        _buffer.Clear();
        IsTriggerActive = false;
        ClearCandidates();

        var hwnd = _lastTargetHwnd != IntPtr.Zero ? _lastTargetHwnd : Win32Api.GetForegroundWindow();

        Task.Run(() =>
        {
            try
            {
                // 지울 글자 수: 트리거(\) + 쳐진 글자 수
                TextInjectionService.ReplaceText(deleteCount, item.Content, hwnd);
            }
            finally
            {
                Thread.Sleep(100);
                _isReplacing = false;
            }
        });
    }

    private void ClearCandidates()
    {
        _currentCandidates.Clear();
        _currentMatchedLen = 0;
        RequestCloseCandidates?.Invoke();
    }

    public void Dispose()
    {
        Stop();
    }
}
