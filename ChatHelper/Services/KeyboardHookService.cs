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

    // 추천 칩이 떠 있을 때 '\' 키를 눌렀는지 여부
    private bool _triggerArmed = false;

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

            // Alt 키 상태 (VK_MENU: 0x12)
            bool isAltPressed = (GetAsyncKeyState(Win32Api.VK_MENU) & 0x8000) != 0;

            // Alt + Q 수동 검색창
            if (isAltPressed && vkCode == 0x51)
            {
                GlobalHotkeyTriggered?.Invoke();
                return (IntPtr)1;
            }

            if (ProcessKeyDown(vkCode))
            {
                return (IntPtr)1; // 키 가로채기 (Eat)
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ProcessKeyDown(int vkCode)
    {
        lock (_lock)
        {
            _lastTargetHwnd = Win32Api.GetForegroundWindow();

            // 백슬래시 키 ('\', VK_OEM_5 = 0xDC)가 눌려있는지 확인
            bool isSlashHeld = (GetAsyncKeyState(0xDC) & 0x8000) != 0;

            // 1. 추천 칩이 떠 있을 때의 완성 트리거 처리 (\+Tab 또는 \ 누른 직후 Tab)
            if (_currentCandidates.Count > 0)
            {
                // 사용자가 '\' (0xDC) 키를 눌렀을 때:
                // 화면에 '\'가 쳐지지 않게 가로채고, 완성 준비 상태(_triggerArmed)로 전환!
                if (vkCode == 0xDC)
                {
                    _triggerArmed = true;
                    return true; // 채팅창에 '\' 입력 방지! 깔끔하게 가로챔!
                }

                // 완성 트리거: [ \ 키가 눌린 상태에서 Tab ] 또는 [ \ 키를 누른 직후 Tab ]
                if (vkCode == Win32Api.VK_TAB)
                {
                    if (isSlashHeld || _triggerArmed)
                    {
                        CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                        return true; // 원래 Tab 방지하고 즉시 완성!
                    }
                    else
                    {
                        // '\' 없이 그냥 누른 일반 Tab 키는 100% 정상 통과! 추천 칩만 닫음
                        ClearCandidates();
                        return false;
                    }
                }

                // 1~9 숫자 키 선택 (\+1, \+2 등)
                if (_triggerArmed && vkCode >= 0x31 && vkCode <= 0x39)
                {
                    int index = vkCode - 0x31;
                    if (index < _currentCandidates.Count)
                    {
                        CompleteSelection(_currentCandidates[index], _currentMatchedLen);
                        return true;
                    }
                }
            }

            // 일반 타이핑이 시작되면 _triggerArmed 리셋
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

            // 3. 엔터 또는 ESC 키 -> 버퍼 초기화 및 칩 닫기
            if (vkCode == Win32Api.VK_RETURN || vkCode == Win32Api.VK_ESCAPE)
            {
                _buffer.Clear();
                ClearCandidates();
                return false;
            }

            // 4. 스페이스바 (공백) -> 단어 끝남, 버퍼 초기화
            if (vkCode == Win32Api.VK_SPACE)
            {
                _buffer.Clear();
                ClearCandidates();
                return false;
            }

            // 5. 물리 영문 자판 (A-Z, 0x41 ~ 0x5A) -> 한글 모드/영문 모드 상관없이 물리 키 감지
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                char ch = char.ToLower((char)vkCode);
                _buffer.Append(ch);
                if (_buffer.Length > 15)
                {
                    _buffer.Remove(0, _buffer.Length - 15);
                }

                // 글자 치자마자 즉시 실시간 후보 평가 -> 알약 칩 활성화!
                EvaluateCandidates();
                return false;
            }
        }

        return false;
    }

    private void EvaluateCandidates()
    {
        var raw = _buffer.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            ClearCandidates();
            return;
        }

        // 최근 입력된 단어로 추천 검색 (접두사 \ 없이도 바로 검색!)
        var list = _templateManager.FindMatchingCandidates(raw, out int matchedLen);
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
                // 화면에 쳐진 글자 수(예: 'ㅇㄴ' -> 2타)만큼 백스페이스로 싹 지우고 문장 주입!
                TextInjectionService.ReplaceText(deleteCount, item.Content, hwnd);
            }
            finally
            {
                Thread.Sleep(80);
                _isReplacing = false;
            }
        });
    }

    private void ClearCandidates()
    {
        _currentCandidates.Clear();
        _currentMatchedLen = 0;
        _triggerArmed = false;
        RequestCloseCandidates?.Invoke();
    }

    public void Dispose()
    {
        Stop();
    }
}
