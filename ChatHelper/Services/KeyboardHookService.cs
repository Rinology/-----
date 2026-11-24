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
    private IntPtr _fgWinEventHook = IntPtr.Zero;
    private IntPtr _focusWinEventHook = IntPtr.Zero;
    private Win32Api.WinEventDelegate? _winEventProc;

    private readonly HangulAssembler.HangulImeBuffer _buffer = new();
    private readonly object _lock = new();

    public event Action<List<TemplateItem>, int, IntPtr>? CandidatesUpdated;
    public event Action<TemplateItem, int, IntPtr>? AiCandidateReady;
    public event Action<string>? AiGeneratingStarted;
    public event Action? RequestCloseCandidates;
    public event Action<IntPtr>? GlobalHotkeyTriggered;
    public event Action<IntPtr>? AiHotkeyTriggered;
    public event Action<IntPtr>? TranslateHotkeyTriggered;
    public event Action<IntPtr>? QuickRegisterHotkeyTriggered;
    public event Action<TemplateItem>? ItemAutoCompleted;
    public event Action<string, string>? RecognitionStateChanged;

    public IntPtr LastTargetHwnd => _lastTargetHwnd;

    public void RememberTargetWindow(IntPtr hwnd)
    {
        if (Win32Api.IsValidTargetWindow(hwnd))
        {
            _lastTargetHwnd = hwnd;
        }
    }
    public bool IsNaturalLanguageMode => _isNaturalLanguageMode;
    public bool HasActiveCandidates => _hasCandidates;
    public string LastRecognizedToken { get; private set; } = string.Empty;
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
            if (_currentCandidates.Count > 0 || _buffer.Length > 0)
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
        _winEventProc = WinEventCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hMod = Win32Api.GetModuleHandle(curModule?.ModuleName);
        _hookId = Win32Api.SetWindowsHookEx(Win32Api.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        // WH_MOUSE_LL 전역 마우스 훅은 모든 WM_MOUSEMOVE를 UI 스레드로 동기 전달해 마우스 커서 지연을 유발하므로
        // 비동기 out-of-context WinEventHook(창 전환/포커스 이동)으로 대체
        uint flags = Win32Api.WINEVENT_OUTOFCONTEXT | Win32Api.WINEVENT_SKIPOWNPROCESS;
        _fgWinEventHook = Win32Api.SetWinEventHook(
            Win32Api.EVENT_SYSTEM_FOREGROUND, Win32Api.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, flags);
        _focusWinEventHook = Win32Api.SetWinEventHook(
            Win32Api.EVENT_OBJECT_FOCUS, Win32Api.EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _winEventProc, 0, 0, flags);
        if (Win32Api.IsValidTargetWindow(out var initFg))
        {
            _lastTargetHwnd = initFg;
        }
        Debug.WriteLine($"[KeyboardHook] 훅 설치 완료. KB={_hookId}, WinEventFG={_fgWinEventHook}, WinEventFocus={_focusWinEventHook}");
    }

    public void Stop()
    {
        _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_hookId != IntPtr.Zero)
        {
            Win32Api.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        if (_fgWinEventHook != IntPtr.Zero)
        {
            Win32Api.UnhookWinEvent(_fgWinEventHook);
            _fgWinEventHook = IntPtr.Zero;
        }
        if (_focusWinEventHook != IntPtr.Zero)
        {
            Win32Api.UnhookWinEvent(_focusWinEventHook);
            _focusWinEventHook = IntPtr.Zero;
        }
    }

    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_isReplacing)
            return;

        // 외부 채팅/작업 창으로 포그라운드가 전환될 때마다 항상 마지막 유효 대상 창 핸들을 최신 상태로 유지
        // (제어판이나 검색창을 열었다 닫아도 직전 외부 채팅창 핸들이 유실되지 않도록 보장)
        if (eventType == Win32Api.EVENT_SYSTEM_FOREGROUND && Win32Api.IsValidTargetWindow(hwnd))
        {
            _lastTargetHwnd = hwnd;
        }

        // 외부 창 전환 또는 다른 입력 컨트롤로 포커스 이동 시 진행 중인 버퍼/알약 초기화
        if (_hasCandidates || _buffer.Length > 0)
        {
            if (eventType == Win32Api.EVENT_SYSTEM_FOREGROUND ||
                (eventType == Win32Api.EVENT_OBJECT_FOCUS && hwnd != IntPtr.Zero && hwnd != _lastTargetHwnd))
            {
                _keyChannel.Writer.TryWrite((Win32Api.VK_ESCAPE, false, IntPtr.Zero));
            }
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
                uint cleanScan = (uint)(scanCode & 0xFF);
                int mapped = Win32Api.MapVirtualKey(cleanScan, Win32Api.MAPVK_VSC_TO_VK);
                if (mapped <= 0)
                {
                    mapped = Win32Api.MapVirtualKey((uint)scanCode, Win32Api.MAPVK_VSC_TO_VK_EX);
                }
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
                // Alt 단독 입력으로 오인되어 대상 창의 Alt 메뉴바가 열리지 않도록 더미 키(VK_NONAME) 전송
                Win32Api.keybd_event(0xFC, 0, 0, UIntPtr.Zero);
                Win32Api.keybd_event(0xFC, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                AppLogger.Info("KeyboardHook", $"Alt+Q 훅 감지 (vk=0x{vkCode:X}, scan=0x{scanCode:X}, fg=0x{fgHwnd.ToInt64():X})");
                GlobalHotkeyTriggered?.Invoke(fgHwnd);
                return (IntPtr)1;
            }

            // Alt + X 특수문자 전용 창 호출 (왼쪽 Alt + X)
            if (isLeftAltPressed && !isCtrlPressed && (vkCode == Win32Api.VK_X || scanCode == Win32Api.SCAN_X))
            {
                Win32Api.keybd_event(0xFC, 0, 0, UIntPtr.Zero);
                Win32Api.keybd_event(0xFC, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                AiHotkeyTriggered?.Invoke(fgHwnd);
                return (IntPtr)1;
            }

            // Alt + T 드래그 선택 문장 Google 영문 번역 호출 (왼쪽 Alt + T)
            if (isLeftAltPressed && !isCtrlPressed && (vkCode == Win32Api.VK_T || scanCode == Win32Api.SCAN_T))
            {
                // Alt 단독 입력으로 오인되어 대상 창의 Alt 메뉴바(기본 명령어)가 열리지 않도록 더미 키(VK_NONAME) 전송
                Win32Api.keybd_event(0xFC, 0, 0, UIntPtr.Zero);
                Win32Api.keybd_event(0xFC, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                TranslateHotkeyTriggered?.Invoke(fgHwnd);
                return (IntPtr)1;
            }

            /* 바로등록 기능 임시 비활성화
            // Alt + R 드래그 선택 문장 시트 바로등록 호출 (왼쪽 Alt + R)
            if (isLeftAltPressed && !isCtrlPressed && (vkCode == Win32Api.VK_R || scanCode == Win32Api.SCAN_R))
            {
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                QuickRegisterHotkeyTriggered?.Invoke(fgHwnd);
                return (IntPtr)1;
            }
            */

            // 자동완성이 일시정지(OFF) 상태이면 패스
            if (!_configManager.Config.IsEnabled)
            {
                return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // 현재 포그라운드 창이 ChatHelper 자신(검색창 Alt+Q, 제어판, 기호창 등)이거나 바탕화면이면
            // 내부 입력에 전혀 간섭하지 않고 즉시 통과시켜 _lastTargetHwnd 오염 및 Tab/숫자키 가로채기 원천 차단
            IntPtr currentFgHwnd = Win32Api.GetForegroundWindow();
            if (!Win32Api.IsValidTargetWindow(currentFgHwnd))
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

            // 빠른 타이핑 직후 트리거 키(\, Tab, Space)가 들어왔을 때 채널에 대기 중인 앞 글자들을 즉시 플러시
            // (WH_KEYBOARD_LL 훅 콜백 내부에서는 크로스-프로세스 COM 위치 조회를 건너뛰어 <0.1ms 유지)
            if (IsPotentialTriggerKey(vkCode) && _keyChannel.Reader.TryPeek(out _))
            {
                while (_keyChannel.Reader.TryRead(out var pending))
                {
                    ProcessBufferedKeyDown(pending.VkCode, pending.IsShift, pending.Hwnd, skipPositionCheck: true);
                }
            }

            // 후보가 떠 있을 때의 완성 트리거(\, Tab, Shift+Space, 1~9, A)만 훅 스레드에서 즉시 가로채기
            // (트리거 키가 아닌 일반 키는 _lock 경합 없이 바로 통과시켜 타이핑 렉 원천 차단)
            if (_hasCandidates && IsPotentialTriggerKey(vkCode) && TryHandleCompletionTrigger(vkCode, isShiftPressed))
            {
                return (IntPtr)1;
            }

            // 일반 키 입력(문자, 숫자, 사칙연산 기호 포함)은 채널로 즉시 위임해 OS 훅 콜백 지연(<0.1ms) 최소화
            if ((vkCode >= Win32Api.VK_A && vkCode <= Win32Api.VK_Z) ||
                (vkCode >= Win32Api.VK_0 && vkCode <= Win32Api.VK_9) ||
                (vkCode >= Win32Api.VK_NUMPAD0 && vkCode <= Win32Api.VK_NUMPAD9) ||
                vkCode is Win32Api.VK_MULTIPLY or Win32Api.VK_ADD or Win32Api.VK_SUBTRACT
                or Win32Api.VK_DECIMAL or Win32Api.VK_DIVIDE
                or Win32Api.VK_OEM_PLUS or Win32Api.VK_OEM_MINUS or Win32Api.VK_OEM_PERIOD or Win32Api.VK_OEM_2
                or Win32Api.VK_OEM_5 or Win32Api.VK_BACK or Win32Api.VK_DELETE or Win32Api.VK_SPACE
                or Win32Api.VK_RETURN or Win32Api.VK_ESCAPE
                or Win32Api.VK_HOME or Win32Api.VK_END or Win32Api.VK_PRIOR or Win32Api.VK_NEXT
                or Win32Api.VK_LEFT or Win32Api.VK_UP or Win32Api.VK_RIGHT or Win32Api.VK_DOWN)
            {
                _keyChannel.Writer.TryWrite((vkCode, isShiftPressed, currentFgHwnd));
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsPotentialTriggerKey(int vkCode) =>
        vkCode == Win32Api.VK_OEM_5 ||
        vkCode == Win32Api.VK_TAB ||
        vkCode == Win32Api.VK_SPACE ||
        vkCode == Win32Api.VK_A ||
        vkCode == Win32Api.VK_E ||
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

            if (mode != "ShiftSpace" &&
                _configManager.Config.EnableNumberSelection &&
                (isSlashHeld || _triggerArmed) &&
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

            if ((isSlashHeld || _triggerArmed) && vkCode == Win32Api.VK_E)
            {
                var transCandidate = _currentCandidates.FirstOrDefault(c => c.Category == "AI번역");
                if (transCandidate != null)
                {
                    CompleteSelection(transCandidate, _currentMatchedLen);
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

    private void ProcessBufferedKeyDown(int vkCode, bool isShiftPressed, IntPtr currentHwnd, bool skipPositionCheck = false)
    {
        lock (_lock)
        {
            if (currentHwnd != IntPtr.Zero && Win32Api.IsValidTargetWindow(currentHwnd))
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
                _buffer.Backspace();
                EvaluateCandidates();
                return;
            }

            if (vkCode is Win32Api.VK_RETURN or Win32Api.VK_ESCAPE or Win32Api.VK_DELETE
                or Win32Api.VK_HOME or Win32Api.VK_END or Win32Api.VK_PRIOR or Win32Api.VK_NEXT
                or Win32Api.VK_LEFT or Win32Api.VK_UP or Win32Api.VK_RIGHT or Win32Api.VK_DOWN)
            {
                _buffer.Clear();
                ClearCandidates();
                return;
            }

            if (vkCode == Win32Api.VK_SPACE)
            {
                string curBuf = _buffer.GetRawText();
                bool isMathBuf = _configManager.Config.EnableMathCalc && MathCalcHelper.LooksLikePartialMath(curBuf);

                // 수식("5 * 10 =") 입력 중이거나 1회 띄어쓰기 자연어 구문("배송 안내")은 유지,
                // 그 외 일반 긴 문장은 버퍼/알약을 즉시 닫음
                if ((isMathBuf && _buffer.Length <= 25) ||
                    (_buffer.Length >= 2 && _buffer.Length <= 15 && !curBuf.Contains(' ')))
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
            else if (vkCode == Win32Api.VK_8 && isShiftPressed)
            {
                inputChar = '*';
            }
            else if (vkCode >= Win32Api.VK_0 && vkCode <= Win32Api.VK_9 && !isShiftPressed)
            {
                inputChar = (char)('0' + (vkCode - Win32Api.VK_0));
            }
            else if (vkCode >= Win32Api.VK_NUMPAD0 && vkCode <= Win32Api.VK_NUMPAD9)
            {
                inputChar = (char)('0' + (vkCode - Win32Api.VK_NUMPAD0));
            }
            else if (vkCode == Win32Api.VK_MULTIPLY) inputChar = '*';
            else if (vkCode == Win32Api.VK_ADD) inputChar = '+';
            else if (vkCode == Win32Api.VK_SUBTRACT || (vkCode == Win32Api.VK_OEM_MINUS && !isShiftPressed)) inputChar = '-';
            else if (vkCode == Win32Api.VK_DIVIDE || (vkCode == Win32Api.VK_OEM_2 && !isShiftPressed)) inputChar = '/';
            else if (vkCode == Win32Api.VK_DECIMAL || (vkCode == Win32Api.VK_OEM_PERIOD && !isShiftPressed)) inputChar = '.';
            else if (vkCode == Win32Api.VK_OEM_PLUS) inputChar = isShiftPressed ? '+' : '=';
            else if (vkCode == Win32Api.VK_OEM_5)
            {
                inputChar = '\\';
            }

            if (inputChar.HasValue)
            {
                if (!skipPositionCheck && !Win32Api.IsValidTargetWindow(out _))
                {
                    _buffer.Clear();
                    ClearCandidates();
                    return;
                }

                // '\' 또는 '/' 단축어 시작 기호를 입력한 경우(수식 입력 중이 아닐 때) 이전 잔여 버퍼를 비워 \dz, /dz 즉시 인식 보장
                if (inputChar.Value == '\\' ||
                    (inputChar.Value == '/' && !MathCalcHelper.LooksLikePartialMath(_buffer.GetRawText())))
                {
                    _buffer.Clear();
                }

                _buffer.Append(inputChar.Value);
                EvaluateCandidates();
            }
        }
    }

    private void EvaluateCandidates()
    {
        var raw = _buffer.GetRawText();
        var hangulFull = _buffer.GetHangulText();
        if (string.IsNullOrWhiteSpace(raw))
        {
            LastRecognizedToken = string.Empty;
            RecognitionStateChanged?.Invoke(string.Empty, "WAITING");
            ClearCandidates();
            return;
        }

        if (raw.Trim().Length < 2)
        {
            string partial = hangulFull.Trim();
            LastRecognizedToken = partial;
            RecognitionStateChanged?.Invoke(partial, "WAITING");
            ClearCandidates();
            return;
        }

        // 0. 사칙연산 자동 계산 기능 (ON일 때, 예: "5 * 10 =" -> "50")
        if (_configManager.Config.EnableMathCalc &&
            MathCalcHelper.TryEvaluate(raw, out string calcVal, out string calcEq))
        {
            LastRecognizedToken = raw.Trim();
            RecognitionStateChanged?.Invoke(LastRecognizedToken, "MATCH");
            var mathList = new List<TemplateItem>
            {
                new TemplateItem
                {
                    Keyword = raw.Trim(),
                    ContentKeyword = "사칙연산",
                    Title = $"🧮 계산 결과: {calcVal}",
                    Content = calcVal,
                    Category = "계산"
                },
                new TemplateItem
                {
                    Keyword = raw.Trim(),
                    ContentKeyword = "사칙연산",
                    Title = $"🧮 수식 포함: {calcEq}",
                    Content = calcEq,
                    Category = "계산"
                }
            };
            ShowCandidateList(mathList, raw.Length, isNaturalLanguage: true);
            return;
        }

        // 1. 영문 단축키 즉시 매칭 (예: qa, \dz, /dz, ed) — 단어 전용, 번역 미호출
        var list = _templateManager.FindMatchingCandidates(raw, out int matchedLen);
        if (list.Count > 0)
        {
            LastRecognizedToken = raw.Trim();
            RecognitionStateChanged?.Invoke(LastRecognizedToken, "MATCH");
            ShowCandidateList(list, matchedLen, isNaturalLanguage: false);
            return;
        }

        // 2. 한글 입력: 스프레드시트 키워드 검색 전용 (영문 번역은 Alt+T 드래그 선택으로 명확하게 분리)
        string hangul = hangulFull.Trim().TrimStart('\\', '/');
        if (!string.IsNullOrWhiteSpace(hangul) && hangul.Length >= 2)
        {
            var hangulList = _templateManager.FindMatchingCandidates(hangul, out int hMatchedLen);
            if (hangulList.Count > 0)
            {
                LastRecognizedToken = hangul;
                RecognitionStateChanged?.Invoke(LastRecognizedToken, "MATCH");
                ShowCandidateList(hangulList, hMatchedLen, isNaturalLanguage: true);
                return;
            }

            var fuzzyPills = _suggestionEngine.SearchRelatedPills(hangul, maxResults: 5);
            if (fuzzyPills.Count > 0)
            {
                LastRecognizedToken = hangul;
                RecognitionStateChanged?.Invoke(LastRecognizedToken, "MATCH");
                ShowCandidateList(fuzzyPills, hangul.Length, isNaturalLanguage: true);
                return;
            }

            // 매칭된 키워드가 없으면 [미일치] 알림 표시
            LastRecognizedToken = hangul;
            ClearCandidates(cancelAi: true);
            RecognitionStateChanged?.Invoke(hangul, "UNKNOWN");
            _autoHideTimer.Change(2500, Timeout.Infinite);
            return;
        }

        LastRecognizedToken = raw.Trim();
        ClearCandidates(cancelAi: true);
        RecognitionStateChanged?.Invoke(LastRecognizedToken, "UNKNOWN");
        _autoHideTimer.Change(2500, Timeout.Infinite);
    }

    /// <summary>
    /// 띄어쓰기 2어절 이상이거나 문장부호(?, ., !)가 포함된 한글 입력을 '문장'으로 판별.
    /// 단어(키워드 매칭)와 문장(영문 번역)을 완벽히 분리하기 위한 기준.
    /// </summary>
    private static bool IsKoreanSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.StartsWith("영문 ") || text.StartsWith("번역 ")) return true;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) return true;
        if (text.Length >= 4 && (text.EndsWith('?') || text.EndsWith('.') || text.EndsWith('!'))) return true;
        return false;
    }

    private static bool IsUnrecognizedGibberish(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        // 완성형 한글 음절(가~힣)이나 영문/숫자가 하나도 없고 단자음/단모음(ㄱ~ㅎ, ㅏ~ㅣ)만으로 이루어진 경우
        bool hasSyllable = false;
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                hasSyllable = true;
                break;
            }
        }
        return !hasSyllable;
    }

    private void ScheduleBackgroundAi(string raw, string hangul)
    {
        if (!_configManager.Config.EnableKoToEnTranslation)
            return;
        if (string.IsNullOrWhiteSpace(hangul) || hangul.Length < 2 || IsUnrecognizedGibberish(hangul))
            return;

        _pendingAiRaw = raw;
        _aiDebounceTimer.Change(450, Timeout.Infinite);
        AiGeneratingStarted?.Invoke(hangul);
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
        string hangul;
        IntPtr targetHwnd;
        lock (_lock)
        {
            targetRaw = _pendingAiRaw;
            if (string.IsNullOrWhiteSpace(targetRaw) || _buffer.GetRawText() != targetRaw)
                return;
            hangul = _buffer.GetHangulText().Trim().TrimStart('\\', '/');
            targetHwnd = _lastTargetHwnd;
        }

        if (hangul.Length < 2 || !_configManager.Config.EnableKoToEnTranslation) return;

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var token = _aiCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                string cleanHangul = hangul;
                if (cleanHangul.StartsWith("영문 ")) cleanHangul = cleanHangul[3..].Trim();
                else if (cleanHangul.StartsWith("번역 ")) cleanHangul = cleanHangul[3..].Trim();
                if (cleanHangul.Length < 2) return;

                string enAnswer = await DataSourceResolver.TranslateViaGasAsync(cleanHangul, "ko", "en", token);
                if (!token.IsCancellationRequested && !string.IsNullOrWhiteSpace(enAnswer))
                {
                    var transItem = new TemplateItem
                    {
                        Keyword = cleanHangul,
                        ContentKeyword = $"영문번역,{cleanHangul}",
                        Title = $"영문 번역 ({cleanHangul})",
                        Content = enAnswer,
                        Category = "AI번역"
                    };
                    AddOrUpdateDynamicCandidate(targetRaw, transItem, hangul.Length);
                    AiCandidateReady?.Invoke(transItem, hangul.Length, targetHwnd);
                }
            }
            catch { }
        }, token);
    }

    private void AddOrUpdateDynamicCandidate(string expectedRaw, TemplateItem item, int matchedLen = 0)
    {
        lock (_lock)
        {
            if (_buffer.GetRawText() != expectedRaw) return;
            int idx = _currentCandidates.FindIndex(c => c.Category == item.Category);
            if (idx >= 0)
                _currentCandidates[idx] = item;
            else
                _currentCandidates.Add(item);
            if (_currentMatchedLen <= 0 && matchedLen > 0)
            {
                _currentMatchedLen = matchedLen;
                _isNaturalLanguageMode = true;
            }
            _hasCandidates = _currentCandidates.Count > 0;
            _autoHideTimer.Change(GetAutoHideTimeoutMs(), Timeout.Infinite);
        }
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
