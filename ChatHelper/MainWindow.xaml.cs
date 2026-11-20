using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChatHelper.Constants;
using ChatHelper.Models;
using ChatHelper.Services;
using ChatHelper.UI;

namespace ChatHelper;

public partial class MainWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly TemplateManager _templateManager;
    private readonly GlossaryService _glossaryService;
    private readonly AuthService _authService;
    private readonly KeyboardHookService _hookService;

    private readonly SuggestionBar _suggestionBar;
    private readonly QuickSearchWindow _quickSearchWindow;
    private QuickAiWindow? _quickAiWindow;
    private GlossaryWindow? _glossaryWindow;
    private ControlPanelWindow? _controlPanelWindow;
    private TrayIconService? _trayIconService;
    private System.Threading.Timer? _periodicSyncTimer;

    public MainWindow()
    {
        EnsureSingleInstance();

        InitializeComponent();

        _configManager = new ConfigManager();
        ThemeManager.ApplyTheme(_configManager.Config.Theme);
        _templateManager = new TemplateManager(_configManager);
        _glossaryService = new GlossaryService(_configManager);
        _authService = new AuthService(_configManager);
        _hookService = new KeyboardHookService(_configManager, _templateManager, _glossaryService);

        _suggestionBar = new SuggestionBar(_configManager);
        _quickSearchWindow = new QuickSearchWindow(_templateManager, _configManager);

        if (!PerformInitialLogin())
        {
            Application.Current.Shutdown();
            return;
        }

        InitEvents();
        UpdateStatusUi();

        ThemeManager.ThemeChanged += () =>
        {
            Dispatcher.BeginInvoke(UpdateStatusUi);
        };
    }

    private static void EnsureSingleInstance()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var others = Process.GetProcessesByName(current.ProcessName)
                .Where(p => p.Id != current.Id);

            foreach (var p in others)
            {
                try
                {
                    p.CloseMainWindow();
                    p.Kill();
                }
                catch { }
            }
        }
        catch { }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        InitTrayService();
        _hookService.Start();
        InitPeriodicSyncTimer();

        // 앱 시작 시 구글 스프레드시트 최신 템플릿 + 2번 시트(용어집) + 원격 AI 규칙 비동기 동기화 및 Ollama 예열
        _ = Task.Run(async () =>
        {
            var (success, _) = await _templateManager.SyncFromRemoteAsync();
            await _glossaryService.SyncFromRemoteAsync();
            await OllamaService.Instance.SyncRemoteAiRulesAsync();
            _ = OllamaService.Instance.WarmUpAsync();
            if (success)
            {
                Dispatcher.BeginInvoke(UpdateStatusUi);
            }
        });

        if (!_configManager.Config.ShowStatusWindow)
        {
            HideToTray(showNotification: true);
        }
        else
        {
            var user = _authService.CurrentUser;
            _trayIconService?.ShowBalloon($"ChatHelper v2.0 실행 중 ({user?.UserId})",
                "화면 우측 하단 상태표시창 또는 트레이 아이콘에서 [💻 제어판]을 열 수 있습니다.");
        }
    }

    private bool PerformInitialLogin()
    {
        // 로그인 상태 유지가 켜져 있고 유효한 계정이면 자동 로그인
        if (_configManager.Config.KeepLoggedIn)
        {
            string lastUser = _configManager.Config.CurrentUserId;
            string lastPw = _configManager.Config.SavedPassword;
            if (!string.IsNullOrWhiteSpace(lastUser))
            {
                var (success, _, _) = _authService.ValidateUser(lastUser, lastPw);
                if (success)
                {
                    return true;
                }
            }
        }

        // 그 외에는 항상 로그인 창을 먼저 띄워 로그인 정보를 확인
        var loginWin = new LoginWindow(_authService, _configManager);
        var result = loginWin.ShowDialog();
        return result == true && loginWin.IsAuthenticated;
    }

    private void InitTrayService()
    {
        _trayIconService = new TrayIconService(this);
        _trayIconService.Initialize();

        _trayIconService.OpenStatusWindowRequested += ShowStatusWindow;
        _trayIconService.OpenControlPanelRequested += OpenControlPanel;
        _trayIconService.OpenQuickSearchRequested += OpenQuickSearch;
        _trayIconService.ReloadTemplatesRequested += ReloadTemplates;
        _trayIconService.SyncRemoteRequested += async () => await SyncRemoteAsync();
        _trayIconService.ReloginRequested += Relogin;
        _trayIconService.ExitRequested += ExitApp;
    }

    private void InitEvents()
    {
        _hookService.CandidatesUpdated += (candidates, matchedLen, targetHwnd) =>
        {
            Dispatcher.Invoke(() =>
            {
                _suggestionBar.ShowCandidates(candidates, matchedLen, targetHwnd);
            });
        };

        _hookService.RequestCloseCandidates += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _suggestionBar.Hide();
            });
        };

        _hookService.GlobalHotkeyTriggered += () =>
        {
            Dispatcher.Invoke(OpenQuickSearch);
        };

        _hookService.AiHotkeyTriggered += (targetHwnd) =>
        {
            Dispatcher.Invoke(() => OpenQuickAi(targetHwnd));
        };

        _suggestionBar.ItemSelected += (item, matchedLen, targetHwnd) =>
        {
            bool useShiftHome = _hookService.IsNaturalLanguageMode;
            _hookService.ClearAll();
            Task.Run(async () =>
            {
                TextInjectionService.ReplaceText(matchedLen, item.Content, targetHwnd, useShiftHome);
                await TryAutoUploadAiSelectionAsync(item);
            });
        };

        _hookService.ItemAutoCompleted += (item) =>
        {
            _ = Task.Run(() => TryAutoUploadAiSelectionAsync(item));
        };

        _quickSearchWindow.ItemSelected += (item, targetHwnd) =>
        {
            Task.Run(() =>
            {
                TextInjectionService.FastPasteText(item.Content, targetHwnd);
            });
        };
    }

    private void UpdateStatusUi()
    {
        var user = _authService.CurrentUser;
        string userId = user?.UserId ?? _configManager.Config.CurrentUserId;
        string planInfo = user != null ? $"{user.Plan} / D-{user.DaysRemaining}일" : "인증됨";
        var trigger = _configManager.Config.TriggerKey ?? "\\";
        var completeMode = _configManager.Config.CompleteKeyMode ?? "SlashTab";
        string keyHint = completeMode switch
        {
            "SlashOnly" => trigger,
            "ShiftSpace" => "Shift + Space",
            _ => $"{trigger} + Tab"
        };

        bool isEnabled = _configManager.Config.IsEnabled;

        // 활성 상태 프로그램창 투명도 적용 (바탕화면 은은하게 비침)
        Opacity = Math.Clamp(_configManager.Config.StatusWindowOpacity, AppConstants.MinStatusWindowOpacity, AppConstants.MaxStatusWindowOpacity);

        if (isEnabled)
        {
            TxtTitleBar.Text = AppConstants.TitleStatusRunning;
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00B000"));
            LblRunningStatus.Text = AppConstants.StatusRunningText;
            LblRunningStatus.Foreground = (Brush)FindResource("ThemeText");
            BtnTogglePause.Content = "⏸️ 일시정지";
        }
        else
        {
            TxtTitleBar.Text = AppConstants.TitleStatusPaused;
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D08000"));
            LblRunningStatus.Text = AppConstants.StatusPausedText;
            LblRunningStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#800000"));
            BtnTogglePause.Content = "▶️ 다시켜기";
            _suggestionBar.Hide();
        }

        LblSubStatus.Text = $"👤 계정: {userId} ({planInfo})   |   📋 템플릿: {_templateManager.Templates.Count}개   |   ⌨️ 완성키: {keyHint}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnControlPanel_Click(object sender, RoutedEventArgs e)
    {
        OpenControlPanel();
    }

    private void BtnQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickSearch();
    }

    private void BtnQuickAi_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickAi();
    }

    private void BtnGlossary_Click(object sender, RoutedEventArgs e)
    {
        if (_glossaryWindow == null || !_glossaryWindow.IsLoaded)
        {
            _glossaryWindow = new GlossaryWindow(_glossaryService);
        }
        _glossaryWindow.Show();
        _glossaryWindow.WindowState = WindowState.Normal;
        _glossaryWindow.Activate();
    }

    private void BtnTogglePause_Click(object sender, RoutedEventArgs e)
    {
        _configManager.Config.IsEnabled = !_configManager.Config.IsEnabled;
        _configManager.Save();
        _hookService.ClearAll();
        UpdateStatusUi();
    }

    private void BtnHideToTray_Click(object sender, RoutedEventArgs e)
    {
        HideToTray(showNotification: true);
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        PromptExitAndLogout();
    }

    private void PromptExitAndLogout()
    {
        var res = MessageBox.Show(
            "ChatHelper 프로그램을 종료합니다.\n\n[예 (Yes)] 다음 실행 시 로그인 상태를 유지하고 종료\n[아니오 (No)] 로그아웃하고 세션을 초기화한 뒤 종료\n[취소 (Cancel)] 종료 취소",
            "프로그램 종료 및 로그아웃 확인",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Cancel)
        {
            return;
        }

        if (res == MessageBoxResult.No)
        {
            _configManager.Config.KeepLoggedIn = false;
            _configManager.Config.CurrentUserId = "";
            _configManager.Save();
        }
        else if (res == MessageBoxResult.Yes)
        {
            _configManager.Config.KeepLoggedIn = true;
            _configManager.Save();
        }

        ExitApp();
    }

    private void ShowStatusWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdateStatusUi();
    }

    private void HideToTray(bool showNotification)
    {
        _suggestionBar.Hide();
        ShowInTaskbar = false;
        Hide();
        if (showNotification)
        {
            _trayIconService?.ShowBalloon("트레이로 이동됨", "작업표시줄 우측 하단 트레이 아이콘을 클릭하면 언제든 다시 열립니다.");
        }
    }

    private void OpenQuickSearch()
    {
        _suggestionBar.Hide();
        IntPtr hwnd = Win32Api.GetForegroundWindow();
        _quickSearchWindow.ShowAtCursor(hwnd);
    }

    private void OpenQuickAi(IntPtr? targetHwnd = null)
    {
        _suggestionBar.Hide();
        IntPtr hwnd = targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero
            ? targetHwnd.Value
            : Win32Api.GetForegroundWindow();

        // 만약 포그라운드가 ChatHelper 자신이거나 없으면, 마지막 외부 작업 창으로 대체
        uint myPid = (uint)Process.GetCurrentProcess().Id;
        Win32Api.GetWindowThreadProcessId(hwnd, out uint fgPid);
        if (fgPid == myPid && _hookService.LastTargetHwnd != IntPtr.Zero)
        {
            hwnd = _hookService.LastTargetHwnd;
        }

        if (_quickAiWindow == null || !_quickAiWindow.IsLoaded)
        {
            _quickAiWindow = new QuickAiWindow(_templateManager, _configManager, _glossaryService);
        }

        _quickAiWindow.ShowForTarget(hwnd);
    }

    private void OpenControlPanel()
    {
        _suggestionBar.Hide();
        if (_controlPanelWindow == null || !_controlPanelWindow.IsLoaded)
        {
            _controlPanelWindow = new ControlPanelWindow(_configManager, _templateManager, _authService);
            _controlPanelWindow.SettingsChanged += () =>
            {
                UpdateStatusUi();
                RestartPeriodicSyncTimer();
                if (_configManager.Config.ShowStatusWindow)
                    ShowStatusWindow();
                else
                    HideToTray(showNotification: false);

                _trayIconService?.ShowBalloon("제어판 설정 적용", "커맨드 및 표시 설정이 즉시 반영되었습니다.");
            };
        }
        _controlPanelWindow.Show();
        _controlPanelWindow.WindowState = WindowState.Normal;
        _controlPanelWindow.Activate();
    }

    private void Relogin()
    {
        _suggestionBar.Hide();
        var loginWin = new LoginWindow(_authService, _configManager);
        if (loginWin.ShowDialog() == true)
        {
            UpdateStatusUi();
            var user = _authService.CurrentUser;
            _trayIconService?.ShowBalloon("로그온 변경", $"'{user?.UserId}' 계정으로 전환되었습니다. (만료일: {user?.ExpireDate:yyyy-MM-dd})");
        }
    }

    private void ReloadTemplates()
    {
        int count = _templateManager.LoadTemplates();
        UpdateStatusUi();
        _trayIconService?.ShowBalloon("템플릿 갱신", $"{count}개 템플릿 로드 완료");
    }

    private async Task SyncRemoteAsync()
    {
        var (success, msg) = await _templateManager.SyncFromRemoteAsync();
        await _glossaryService.SyncFromRemoteAsync();
        await OllamaService.Instance.SyncRemoteAiRulesAsync();
        UpdateStatusUi();
        _trayIconService?.ShowBalloon("구글 시트 동기화", msg);
    }

    /// <summary>
    /// 구글 스프레드시트 최신 템플릿/용어집을 백그라운드에서 주기적으로 조용히 자동 다운로드(기본 15분)
    /// 사용자의 타이핑 렉(Zero-Lag)에 전혀 영향을 주지 않고 안전하게 최신 정보 유지
    /// </summary>
    private void InitPeriodicSyncTimer()
    {
        int minutes = _configManager.Config.AutoSyncIntervalMinutes;
        if (minutes <= 0) return; // 0이면 비활성화

        var interval = TimeSpan.FromMinutes(minutes);
        _periodicSyncTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                var (success, _) = await _templateManager.SyncFromRemoteAsync();
                await _glossaryService.SyncFromRemoteAsync();
                await OllamaService.Instance.SyncRemoteAiRulesAsync();

                if (success)
                {
                    Dispatcher.BeginInvoke(UpdateStatusUi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PeriodicSync] 주기적 갱신 실패: {ex.Message}");
            }
        }, null, interval, interval);
    }

    private void RestartPeriodicSyncTimer()
    {
        _periodicSyncTimer?.Dispose();
        _periodicSyncTimer = null;
        InitPeriodicSyncTimer();
    }

    private async Task TryAutoUploadAiSelectionAsync(TemplateItem item)
    {
        if (!_configManager.Config.AutoUploadAiSelection || item.Category != "AI생성")
            return;

        try
        {
            var existingKeywords = _templateManager.Templates.Select(t => t.Keyword);
            var structured = await OllamaService.Instance.StructureTemplateForSheetAsync(
                item.Keyword, item.Content, existingKeywords);

            _templateManager.AddTemplate(structured);
            var (success, msg) = await _templateManager.UploadTemplateToRemoteSheetAsync(structured);
            Dispatcher.BeginInvoke(() =>
            {
                UpdateStatusUi();
                if (success)
                {
                    _trayIconService?.ShowBalloon("AI 추천 시트 자동 등록", $"'{structured.Title}' ({structured.Keyword}) 항목이 구글 시트에 추가되었습니다.");
                }
                else
                {
                    _trayIconService?.ShowBalloon("AI 추천 시트 자동 등록 실패", msg);
                }
            });
        }
        catch { }
    }

    private void ExitApp()
    {
        _periodicSyncTimer?.Dispose();
        _hookService.Stop();
        _trayIconService?.Dispose();
        _suggestionBar.Close();
        _quickSearchWindow.Close();
        _quickAiWindow?.Close();
        _glossaryWindow?.Close();
        _controlPanelWindow?.Close();
        OllamaService.Instance.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        ExitApp();
        base.OnClosed(e);
    }
}
