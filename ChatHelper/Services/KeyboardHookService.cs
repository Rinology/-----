using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using ChatHelper.Constants;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class KeyboardHookService : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly TemplateManager _templateManager;
    private readonly GlossaryService? _glossaryService;
    private readonly SuggestionEngine _suggestionEngine;

    private IntPtr _hookId = IntPtr.Zero;
    private Win32Api.HookProc? _hookProc;

    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    public event Action<List<TemplateItem>, int, IntPtr>? CandidatesUpdated;
    public event Action<TemplateItem, int, IntPtr>? AiCandidateReady;
    public event Action<string>? AiGeneratingStarted;
    public event Action? RequestCloseCandidates;
    public event Action<IntPtr>? GlobalHotkeyTriggered;
    public event Action<IntPtr>? AiHotkeyTriggered;
    public event Action<TemplateItem>? ItemAutoCompleted;

    public IntPtr LastTargetHwnd => _lastTargetHwnd;
    public bool IsNaturalLanguageMode => _isNaturalLanguageMode;
    public bool HasActiveCandidates => _hasCandidates;
    private List<TemplateItem> _currentCandidates = new();
    private int _currentMatchedLen = 0;
    private bool _isNaturalLanguageMode = false;
    private IntPtr _lastTargetHwnd = IntPtr.Zero;
    private bool _isReplacing = false;
    private bool _triggerArmed = false;

    // 일정 시간(3.5초) 입력이 없으면 추천 알약을 자동으로 숨기는 타이머
    private readonly System.Threading.Timer _autoHideTimer;
    // 자연어 입력 시 Ollama + 시트 의미 검색을 디바운스(300ms)로 실행하는 타이머
    private readonly System.Threading.Timer _aiDebounceTimer;
    private CancellationTokenSource? _aiCts;
    private string _pendingAiRaw = string.Empty;

    // WH_KEYBOARD_LL 훅 콜백 지연을 0.1ms 미만으로 유지하기 위한 백그라운드 키 이벤트 채널
    private readonly Channel<(int VkCode, bool IsShift, IntPtr Hwnd)> _keyChannel =
        Channel.CreateUnbounded<(int, bool, IntPtr)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _workerCts = new();
    private volatile bool _hasCandidates = false;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public KeyboardHookService(ConfigManager configManager, TemplateManager templateManager, GlossaryService? glossaryService = null)
    {
        _configManager = configManager;
        _templateManager = templateManager;
        _glossaryService = glossaryService;
        _suggestionEngine = new SuggestionEngine(templateManager, null, glossaryService);
        _autoHideTimer = new System.Threading.Timer(OnAutoHideTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _aiDebounceTimer = new System.Threading.Timer(OnAiDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _ = Task.Run(ProcessKeyChannelAsync);
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
            int scanCode = Marshal.ReadInt32(lParam + 4);

            // Chrome, Google Chat, Edge 등 브라우저의 한글 IME 조합 중(VK_PROCESSKEY)일 때 물리 키코드로 복원
            if (vkCode == Win32Api.VK_PROCESSKEY)
            {
                int mapped = Win32Api.MapVirtualKey((uint)scanCode, Win32Api.MAPVK_VSC_TO_VK_EX);
                if (mapped > 0)
                {
                    vkCode = mapped;
                }
            }

            // 한국어 한/영 전환키(Right Alt: VK_RMENU, VK_HANGUL)는 단축키 조합키로 취급하지 않음
            bool isHangulKey = (vkCode == Win32Api.VK_HANGUL || vkCode == Win32Api.VK_RMENU);
            int flags = Marshal.ReadInt32(lParam + 8);
            bool isLeftAltPressed = !isHangulKey && (((flags & 0x20) != 0) || ((GetAsyncKeyState(Win32Api.VK_LMENU) & 0x8000) != 0));
            bool isCtrlPressed = (GetAsyncKeyState(Win32Api.VK_CONTROL) & 0x8000) != 0;
            bool isWinPressed = (GetAsyncKeyState(Win32Api.VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(Win32Api.VK_RWIN) & 0x8000) != 0;
            bool isShiftPressed = (GetAsyncKeyState(Win32Api.VK_SHIFT) & 0x8000) != 0;

            // Alt + Q 수동 검색창 호출 (왼쪽 Alt + Q)
            if (isLeftAltPressed && !isCtrlPressed && (vkCode == Win32Api.VK_Q || scanCode == Win32Api.SCAN_Q))
            {
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                GlobalHotkeyTriggered?.Invoke(fgHwnd);
                return (IntPtr)1;
            }

            // Alt + X 로컬 AI 상담 비서 호출 (왼쪽 Alt + X)
            if (isLeftAltPressed && !isCtrlPressed && (vkCode == Win32Api.VK_X || scanCode == Win32Api.SCAN_X))
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

            // Ctrl, 왼쪽 Alt, Win 조합 단축키 입력 시 백그라운드 버퍼 초기화 요청
            if (isCtrlPressed || isLeftAltPressed || isWinPressed)
            {
                if (_hasCandidates)
                {
                    _keyChannel.Writer.TryWrite((Win32Api.VK_ESCAPE, false, IntPtr.Zero));
                }
                return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // 후보가 떠 있을 때의 완성 트리거(\, Tab, Shift+Space, 1~9, A)만 훅 스레드에서 즉시 가로채기
            // (트리거 키가 아닌 일반 키는 _lock 경합 없이 바로 통과시켜 타이핑 렉 원천 차단)
            if (_hasCandidates && IsPotentialTriggerKey(vkCode) && TryHandleCompletionTrigger(vkCode, isShiftPressed))
            {
                return (IntPtr)1;
            }

            // 일반 키 입력은 채널로 즉시 위임해 OS 훅 콜백 지연(<0.1ms) 최소화
            if ((vkCode >= Win32Api.VK_A && vkCode <= Win32Api.VK_Z) ||
                (vkCode >= Win32Api.VK_0 && vkCode <= Win32Api.VK_9) ||
                (vkCode >= Win32Api.VK_NUMPAD0 && vkCode <= Win32Api.VK_NUMPAD9) ||
                vkCode is Win32Api.VK_OEM_5 or Win32Api.VK_BACK or Win32Api.VK_SPACE
                or Win32Api.VK_RETURN or Win32Api.VK_ESCAPE
                or Win32Api.VK_LEFT or Win32Api.VK_UP or Win32Api.VK_RIGHT or Win32Api.VK_DOWN)
            {
                _keyChannel.Writer.TryWrite((vkCode, isShiftPressed, Win32Api.GetForegroundWindow()));
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsPotentialTriggerKey(int vkCode) =>
        vkCode == Win32Api.VK_OEM_5 ||
        vkCode == Win32Api.VK_TAB ||
        vkCode == Win32Api.VK_SPACE ||
        vkCode == Win32Api.VK_A ||
        (vkCode >= Win32Api.VK_1 && vkCode <= Win32Api.VK_9);

    private int GetAutoHideTimeoutMs() =>
        Math.Clamp(_configManager.Config.AutoHideSeconds,
            AppConstants.MinAutoHideSeconds,
            AppConstants.MaxAutoHideSeconds) * 1000;

    private bool TryHandleCompletionTrigger(int vkCode, bool isShiftPressed)
    {
        lock (_lock)
        {
            if (_currentCandidates.Count == 0)
                return false;

            string mode = _configManager.Config.CompleteKeyMode ?? "SlashTab";
            bool isSlashHeld = (GetAsyncKeyState(Win32Api.VK_OEM_5) & 0x8000) != 0;

            if (mode == "ShiftSpace" && isShiftPressed && vkCode == Win32Api.VK_SPACE)
            {
                CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                return true;
            }

            if (vkCode == Win32Api.VK_OEM_5)
            {
                if (mode == "SlashOnly")
                {
                    CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                    return true;
                }

                _triggerArmed = true;
                _autoHideTimer.Change(GetAutoHideTimeoutMs(), Timeout.Infinite);
                return true;
            }

            if (vkCode == Win32Api.VK_TAB)
            {
                if (isSlashHeld || _triggerArmed)
                {
                    CompleteSelection(_currentCandidates[0], _currentMatchedLen);
                    return true;
                }

                _buffer.Clear();
                ClearCandidates();
                return false;
            }

            if (_configManager.Config.EnableNumberSelection && (isSlashHeld || _triggerArmed) &&
                vkCode >= Win32Api.VK_1 && vkCode <= Win32Api.VK_9)
            {
                int index = vkCode - Win32Api.VK_1;
                if (index < _currentCandidates.Count)
                {
                    CompleteSelection(_currentCandidates[index], _currentMatchedLen);
                    return true;
                }
            }

            if ((isSlashHeld || _triggerArmed) && vkCode == Win32Api.VK_A)
            {
                var aiCandidate = _currentCandidates.FirstOrDefault(c => c.Category == "AI생성");
                if (aiCandidate != null)
                {
                    CompleteSelection(aiCandidate, _currentMatchedLen);
                    return true;
                }
            }

            _triggerArmed = false;
            return false;
        }
    }

    private async Task ProcessKeyChannelAsync()
    {
        try
        {
            await foreach (var (vkCode, isShiftPressed, currentHwnd) in _keyChannel.Reader.ReadAllAsync(_workerCts.Token))
            {
                ProcessBufferedKeyDown(vkCode, isShiftPressed, currentHwnd);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error("KeyboardHookService", "키 이벤트 워커 예외", ex);
        }
    }

    private void ProcessBufferedKeyDown(int vkCode, bool isShiftPressed, IntPtr currentHwnd)
    {
        lock (_lock)
        {
            if (currentHwnd != IntPtr.Zero)
            {
                if (_lastTargetHwnd != IntPtr.Zero && _lastTargetHwnd != currentHwnd)
                {
                    _buffer.Clear();
                    ClearCandidates();
                }
                _lastTargetHwnd = currentHwnd;
            }

            _triggerArmed = false;

            if (vkCode == Win32Api.VK_BACK)
            {
                if (_buffer.Length > 0)
                    _buffer.Length--;
                EvaluateCandidates();
                return;
            }

            if (vkCode is Win32Api.VK_RETURN or Win32Api.VK_ESCAPE
                or Win32Api.VK_LEFT or Win32Api.VK_UP or Win32Api.VK_RIGHT or Win32Api.VK_DOWN)
            {
                _buffer.Clear();
                ClearCandidates();
                return;
            }

            if (vkCode == Win32Api.VK_SPACE)
            {
                // 최대 1개의 띄어쓰기까지만 자연어 구문("배송 안내", "화난 고객")으로 유지하고,
                // 그 이상 문장이 길어지면 일반 채팅으로 간주해 버퍼/알약을 즉시 닫음 (알약 계속 떠있음 방지)
                if (_buffer.Length >= 2 && _buffer.Length <= 15 && !_buffer.ToString().Contains(' '))
                {
                    _buffer.Append(' ');
                    EvaluateCandidates();
                }
                else
                {
                    _buffer.Clear();
                    ClearCandidates();
                }
                return;
            }

            char? inputChar = null;
            if (vkCode >= Win32Api.VK_A && vkCode <= Win32Api.VK_Z)
            {
                inputChar = isShiftPressed ? char.ToUpper((char)vkCode) : char.ToLower((char)vkCode);
            }
            else if (vkCode >= Win32Api.VK_0 && vkCode <= Win32Api.VK_9 && !isShiftPressed)
            {
                inputChar = (char)('0' + (vkCode - Win32Api.VK_0));
            }
            else if (vkCode >= Win32Api.VK_NUMPAD0 && vkCode <= Win32Api.VK_NUMPAD9)
            {
                inputChar = (char)('0' + (vkCode - Win32Api.VK_NUMPAD0));
            }
            else if (vkCode == Win32Api.VK_OEM_5)
            {
                inputChar = '\\';
            }

            if (inputChar.HasValue)
            {
                if (!Win32Api.TryGetValidInputPosition(out _, out _, out _))
                {
                    _buffer.Clear();
                    ClearCandidates();
                    return;
                }

                _buffer.Append(inputChar.Value);
                if (_buffer.Length > 30)
                    _buffer.Remove(0, _buffer.Length - 30);

                EvaluateCandidates();
            }
        }
    }

    private void EvaluateCandidates()
    {
        var raw = _buffer.ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Length < 2)
        {
            ClearCandidates();
            return;
        }

        // 1. 영문 단축키 즉시 매칭 (예: qa, wtt)
        var list = _templateManager.FindMatchingCandidates(raw, out int matchedLen);
        if (list.Count > 0)
        {
            ShowCandidateList(list, matchedLen, isNaturalLanguage: false);
            ScheduleBackgroundAi(raw, "");
            return;
        }

        // 2. 한글 조합 변환 후 한글 단축키 즉시 매칭 (예: 반품, 환불)
        string hangul = HangulAssembler.ConvertQwertyToHangul(raw).Trim();
        if (!string.IsNullOrWhiteSpace(hangul) && hangul.Length >= 2)
        {
            var hangulList = _templateManager.FindMatchingCandidates(hangul, out int hMatchedLen);
            if (hangulList.Count > 0)
            {
                ShowCandidateList(hangulList, hMatchedLen, isNaturalLanguage: true);
                ScheduleBackgroundAi(raw, hangul);
                return;
            }

            // 3. 자연어 의도 -> 로컬 퍼지 즉시 매칭
            var fuzzyPills = _suggestionEngine.SearchRelatedPills(hangul, maxResults: 5);
            if (fuzzyPills.Count > 0)
            {
                ShowCandidateList(fuzzyPills, hangul.Length, isNaturalLanguage: true);
            }
            else
            {
                ClearCandidates(cancelAi: false);
            }

            ScheduleBackgroundAi(raw, hangul);
            return;
        }

        ClearCandidates();
    }

    private void ScheduleBackgroundAi(string raw, string hangul)
    {
        if (!_configManager.Config.EnableLocalAi)
            return;

        _pendingAiRaw = raw;
        _aiDebounceTimer.Change(300, Timeout.Infinite);

        // 사용자가 타이핑하자마자 헬프 화면에 즉시 "⚡ AI 답변 생성 중..." 알림
        string query = !string.IsNullOrWhiteSpace(hangul) ? hangul : raw;
        AiGeneratingStarted?.Invoke(query);
    }

    private void ShowCandidateList(List<TemplateItem> list, int matchedLen, bool isNaturalLanguage)
    {
        _currentCandidates = list;
        _currentMatchedLen = matchedLen;
        _isNaturalLanguageMode = isNaturalLanguage;
        _hasCandidates = list.Count > 0;
        _autoHideTimer.Change(GetAutoHideTimeoutMs(), Timeout.Infinite);
        CandidatesUpdated?.Invoke(list, matchedLen, _lastTargetHwnd);
    }

    private void OnAiDebounceElapsed(object? state)
    {
        string targetRaw;
        IntPtr targetHwnd;
        lock (_lock)
        {
            targetRaw = _pendingAiRaw;
            if (string.IsNullOrWhiteSpace(targetRaw) || _buffer.ToString() != targetRaw)
                return;
            targetHwnd = _lastTargetHwnd;
        }

        string hangul = HangulAssembler.ConvertQwertyToHangul(targetRaw).Trim();
        if (hangul.Length < 2) return;

        var glossaryMatches = _glossaryService?.FindRelevantTerms(hangul, maxResults: 5);

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var token = _aiCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await OllamaService.Instance.IsServerRespondingAsync())
                    return;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                string aiAnswer = await OllamaService.Instance.GenerateCsAnswerAsync(hangul, glossaryMatches, token);
                sw.Stop();

                if (token.IsCancellationRequested || string.IsNullOrWhiteSpace(aiAnswer)) return;

                var aiItem = new TemplateItem
                {
                    Keyword = hangul,
                    ContentKeyword = hangul,
                    Title = "🤖 AI 추천 답변",
                    Content = aiAnswer,
                    Category = "AI생성"
                };

                lock (_lock)
                {
                    if (_buffer.ToString() != targetRaw) return;

                    // 현재 후보 목록에 AI 추천 추가
                    if (!_currentCandidates.Any(c => c.Category == "AI생성"))
                    {
                        _currentCandidates.Add(aiItem);
                    }
                    else
                    {
                        int idx = _currentCandidates.FindIndex(c => c.Category == "AI생성");
                        if (idx >= 0) _currentCandidates[idx] = aiItem;
                    }
                }

                // AI 후보 준비 완료 알림
                AiCandidateReady?.Invoke(aiItem, hangul.Length, targetHwnd);
            }
            catch { }
        }, token);
    }

    private void CompleteSelection(TemplateItem item, int deleteCount)
    {
        bool useShiftHome = _isNaturalLanguageMode;
        _isReplacing = true;
        _buffer.Clear();
        _triggerArmed = false;
        ClearCandidates();

        var hwnd = _lastTargetHwnd != IntPtr.Zero ? _lastTargetHwnd : Win32Api.GetForegroundWindow();

        Task.Run(() =>
        {
            try
            {
                TextInjectionService.ReplaceText(deleteCount, item.Content, hwnd, useShiftHome);
                ItemAutoCompleted?.Invoke(item);
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

    private void ClearCandidates(bool cancelAi = true)
    {
        _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (cancelAi)
        {
            _aiDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _aiCts?.Cancel();
        }
        _currentCandidates.Clear();
        _hasCandidates = false;
        _currentMatchedLen = 0;
        _triggerArmed = false;
        RequestCloseCandidates?.Invoke();
    }

    public void Dispose()
    {
        _workerCts.Cancel();
        _keyChannel.Writer.TryComplete();
        _autoHideTimer.Dispose();
        _aiDebounceTimer.Dispose();
        _aiCts?.Cancel();
        Stop();
    }
}
