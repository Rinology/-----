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

    // 현재 활성화된 후보 목록 캐시
    private List<TemplateItem> _currentCandidates = new();
    private int _currentMatchedLen = 0;
    private IntPtr _lastTargetHwnd = IntPtr.Zero;
    private bool _isReplacing = false;

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
            var result = ProcessKeyDown(vkCode);
            if (result == true)
            {
                // 키 이벤트를 가로채서 원래 창으로 전달되지 않게 삼킴
                return (IntPtr)1;
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ProcessKeyDown(int vkCode)
    {
        var config = _configManager.Config;
        string trigger = config.TriggerKey ?? "\\";

        lock (_lock)
        {
            _lastTargetHwnd = Win32Api.GetForegroundWindow();

            // 0. Alt + Q 전역 단축키 감지
            if (vkCode == 0x51 && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0)
            {
                GlobalHotkeyTriggered?.Invoke();
                return true;
            }

            // 1. Tab 키 처리
            if (vkCode == Win32Api.VK_TAB)
            {
                if (_currentCandidates.Count > 0)
                {
                    // 트리거 키(\)가 활성화되어 있거나 설정에 따라 Tab으로 완성
                    bool hasTrigger = !string.IsNullOrEmpty(trigger) && _buffer.ToString().Contains(trigger);
                    if (!config.RequireTriggerForTab || hasTrigger)
                    {
                        CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                        return true; // 원래 Tab 키 동작 방지!
                    }
                }
                // 후보가 없거나 트리거 상태가 아니면 원래 Tab 키 그대로 통과
                return false;
            }

            // 2. 1~9 숫자 키 선택 처리
            if (config.EnableNumberSelection && _currentCandidates.Count > 0 && vkCode >= 0x31 && vkCode <= 0x39)
            {
                bool hasTrigger = !string.IsNullOrEmpty(trigger) && _buffer.ToString().Contains(trigger);
                if (!config.RequireTriggerForTab || hasTrigger)
                {
                    int index = vkCode - 0x31;
                    if (index < _currentCandidates.Count)
                    {
                        CompleteSelection(_currentCandidates[index], _currentMatchedLen);
                        return true; // 원래 숫자 키 입력 방지
                    }
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
                ClearCandidates();
                return false;
            }

            // 5. 일반 문자 변환
            char ch = ConvertVkCodeToChar(vkCode);
            if (ch != '\0')
            {
                _buffer.Append(ch);
                if (_buffer.Length > 30)
                {
                    _buffer.Remove(0, _buffer.Length - 30);
                }

                // 즉시 단축어 검사 (예: \ㅇㄴ 치면 바로 완성)
                if (CheckImmediateExpansion())
                {
                    return false;
                }

                // 실시간 추천 후보 검사
                EvaluateCandidates();
            }
        }

        return false;
    }

    private void EvaluateCandidates()
    {
        var raw = _buffer.ToString();
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var curToken = tokens.Length > 0 ? tokens[^1] : "";

        if (string.IsNullOrEmpty(curToken))
        {
            ClearCandidates();
            return;
        }

        var list = _templateManager.FindMatchingCandidates(curToken, out int matchedLen);
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

    private bool CheckImmediateExpansion()
    {
        var raw = _buffer.ToString();
        var trigger = _configManager.Config.TriggerKey ?? "\\";
        if (string.IsNullOrEmpty(trigger))
            return false;

        foreach (var item in _templateManager.Templates)
        {
            if (string.IsNullOrWhiteSpace(item.Keyword)) continue;

            string targetEng = $"{trigger}{item.Keyword}";
            string korKeyword = TemplateManager.ConvertEngToKor(item.Keyword);
            string targetKor = $"{trigger}{korKeyword}";

            if (raw.EndsWith(targetEng, StringComparison.OrdinalIgnoreCase) || raw.EndsWith(targetKor))
            {
                int len = targetEng.Length;
                CompleteSelection(item, len);
                return true;
            }
        }

        return false;
    }

    private void CompleteSelection(TemplateItem item, int deleteCount)
    {
        _isReplacing = true;
        _buffer.Clear();
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

    private void ClearCandidates()
    {
        _currentCandidates.Clear();
        _currentMatchedLen = 0;
        RequestCloseCandidates?.Invoke();
    }

    private static char ConvertVkCodeToChar(int vkCode)
    {
        if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
            return char.ToLower((char)vkCode);
        if (vkCode >= 0x30 && vkCode <= 0x39) // 0-9
            return (char)vkCode;
        if (vkCode == 0xDC) // '\' (Backslash)
            return '\\';
        if (vkCode == 0xBF) // '/' (Slash)
            return '/';
        if (vkCode == 0xBA) // ';' (Semicolon)
            return ';';
        if (vkCode == Win32Api.VK_SPACE)
            return ' ';

        return '\0';
    }

    public void Dispose()
    {
        Stop();
    }
}
