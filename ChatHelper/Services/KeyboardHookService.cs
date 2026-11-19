using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
    public event Action? RequestCloseCandidates;
    public event Action? GlobalHotkeyTriggered;
    public event Action<IntPtr>? AiHotkeyTriggered;
    public event Action<TemplateItem>? ItemAutoCompleted;

    public IntPtr LastTargetHwnd => _lastTargetHwnd;
    public bool IsNaturalLanguageMode => _isNaturalLanguageMode;
    private List<TemplateItem> _currentCandidates = new();
    private int _currentMatchedLen = 0;
    private bool _isNaturalLanguageMode = false;
    private IntPtr _lastTargetHwnd = IntPtr.Zero;
    private bool _isReplacing = false;
    private bool _triggerArmed = false;

    // 일정 시간(3.5초) 입력이 없으면 추천 알약을 자동으로 숨기는 타이머
    private readonly System.Threading.Timer _autoHideTimer;
    // 자연어 입력 시 Ollama + 시트 의미 검색을 디바운스(450ms)로 실행하는 타이머
    private readonly System.Threading.Timer _aiDebounceTimer;
    private CancellationTokenSource? _aiCts;
    private string _pendingAiRaw = string.Empty;

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

            // Chrome, Google Chat, Edge 등 브라우저의 한글 IME 조합 중(0xE5: VK_PROCESSKEY)일 때 물리 키코드로 완벽 복원
            if (vkCode == 0xE5)
            {
                int mapped = Win32Api.MapVirtualKey((uint)scanCode, Win32Api.MAPVK_VSC_TO_VK_EX);
                if (mapped > 0)
                {
                    vkCode = mapped;
                }
            }

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

                // \ + A : AI 추천 항목 즉시 선택 (Tab/숫자 외 전용 키)
                if ((isSlashHeld || _triggerArmed) && vkCode == 0x41)
                {
                    var aiCandidate = _currentCandidates.FirstOrDefault(c => c.Category == "AI생성");
                    if (aiCandidate != null)
                    {
                        CompleteSelection(aiCandidate, _currentMatchedLen);
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

            // 3. 엔터, ESC, 방향키 -> 단어/문장 종료로 간주하여 버퍼 및 알약 초기화
            if (vkCode is Win32Api.VK_RETURN or Win32Api.VK_ESCAPE
                or Win32Api.VK_LEFT or Win32Api.VK_UP or Win32Api.VK_RIGHT or Win32Api.VK_DOWN)
            {
                _buffer.Clear();
                ClearCandidates();
                return false;
            }

            // 스페이스: Shift+Space 모드가 아니고 이미 입력 중인 버퍼가 있으면 자연어 구문("화난 고객 달래주기") 지원
            if (vkCode == Win32Api.VK_SPACE)
            {
                if (_buffer.Length >= 2)
                {
                    _buffer.Append(' ');
                    if (_buffer.Length > 30)
                        _buffer.Remove(0, _buffer.Length - 30);
                    EvaluateCandidates();
                }
                else
                {
                    _buffer.Clear();
                    ClearCandidates();
                }
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

                char ch = isShiftPressed ? char.ToUpper((char)vkCode) : char.ToLower((char)vkCode);
                _buffer.Append(ch);
                if (_buffer.Length > 30)
                {
                    _buffer.Remove(0, _buffer.Length - 30);
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
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Length < 2)
        {
            ClearCandidates();
            return;
        }

        // 1. 영문 단축키 즉시 매칭 (예: qa, wtt)
        var list = _templateManager.FindMatchingCandidates(raw, out int matchedLen);
        if (list.Count > 0)
        {
            _aiDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _aiCts?.Cancel();
            ShowCandidateList(list, matchedLen, isNaturalLanguage: false);
            return;
        }

        // 2. 한글 조합 변환 후 한글 단축키 즉시 매칭 (예: 반품, 환불)
        string hangul = HangulAssembler.ConvertQwertyToHangul(raw).Trim();
        if (!string.IsNullOrWhiteSpace(hangul) && hangul.Length >= 2)
        {
            var hangulList = _templateManager.FindMatchingCandidates(hangul, out int hMatchedLen);
            if (hangulList.Count > 0)
            {
                _aiDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _aiCts?.Cancel();
                ShowCandidateList(hangulList, hMatchedLen, isNaturalLanguage: true);
                return;
            }

            // 3. 자연어 의도(예: "인사말생성", "배송안내", "화난 고객 달래주기") -> 로컬 퍼지 즉시 + 450ms 디바운스 Ollama 추천
            var fuzzyPills = _suggestionEngine.SearchRelatedPills(hangul, maxResults: 5);
            if (fuzzyPills.Count > 0)
            {
                ShowCandidateList(fuzzyPills, hangul.Length, isNaturalLanguage: true);
            }
            else
            {
                ClearCandidates(cancelAi: false);
            }

            _pendingAiRaw = raw;
            _aiDebounceTimer.Change(300, Timeout.Infinite);
            return;
        }

        ClearCandidates();
    }

    private void ShowCandidateList(List<TemplateItem> list, int matchedLen, bool isNaturalLanguage)
    {
        _currentCandidates = list;
        _currentMatchedLen = matchedLen;
        _isNaturalLanguageMode = isNaturalLanguage;
        _autoHideTimer.Change(5000, Timeout.Infinite);
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

        // 일반 단타 채팅 오작동 방지: 시트/용어집에 관련 항목이 있거나, 생성/안내/추천 등 의도 키워드 포함 또는 4글자 이상일 때 Ollama 실행
        var glossaryMatches = _glossaryService?.FindRelevantTerms(hangul, maxResults: 5);
        bool hasIntent = hangul.Contains("생성") || hangul.Contains("작성") || hangul.Contains("추천")
                      || hangul.Contains("안내") || hangul.Contains("만들") || hangul.Contains("답변")
                      || hangul.Contains("달래") || (glossaryMatches != null && glossaryMatches.Count > 0)
                      || hangul.Length >= 4;
        var quickPills = _suggestionEngine.SearchRelatedPills(hangul, maxResults: 3);
        if (!hasIntent && quickPills.Count == 0)
            return;

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var token = _aiCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // 시트 추천은 즉시(<1ms) 로컬 검색을 사용하고, Ollama는 답변 생성 1회만 호출해 지연 최소화
                var pills = _suggestionEngine.SearchRelatedPills(hangul, maxResults: 5);
                string inferredKw = pills.FirstOrDefault()?.ContentKeyword ?? hangul;

                string aiAnswer = string.Empty;
                try
                {
                    if (await OllamaService.Instance.IsServerRespondingAsync())
                    {
                        aiAnswer = await OllamaService.Instance.GenerateCsAnswerAsync(hangul, glossaryMatches, token);
                    }
                }
                catch { }

                if (token.IsCancellationRequested) return;

                var combined = new List<TemplateItem>();
                foreach (var p in pills)
                {
                    if (!combined.Contains(p))
                        combined.Add(p);
                }

                if (!string.IsNullOrWhiteSpace(aiAnswer))
                {
                    combined.Add(new TemplateItem
                    {
                        Keyword = hangul,
                        ContentKeyword = inferredKw,
                        Title = "🤖 AI 추천",
                        Content = aiAnswer,
                        Category = "AI생성"
                    });
                }

                if (combined.Count == 0) return;

                lock (_lock)
                {
                    if (_buffer.ToString() != targetRaw) return;
                    _lastTargetHwnd = targetHwnd;
                    ShowCandidateList(combined, hangul.Length, isNaturalLanguage: true);
                }
            }
            catch
            {
                // 취소 또는 Ollama 미연결 시 무시
            }
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
        _currentMatchedLen = 0;
        _triggerArmed = false;
        RequestCloseCandidates?.Invoke();
    }

    public void Dispose()
    {
        _autoHideTimer.Dispose();
        _aiDebounceTimer.Dispose();
        _aiCts?.Cancel();
        Stop();
    }
}
