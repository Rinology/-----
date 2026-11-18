using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChatHelper.Models;
using ChatHelper.Services;
using ChatHelper.UI;

namespace ChatHelper;

public partial class MainWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly TemplateManager _templateManager;
    private readonly AuthService _authService;
    private readonly KeyboardHookService _hookService;

    private readonly SuggestionBar _suggestionBar;
    private readonly QuickSearchWindow _quickSearchWindow;
    private ControlPanelWindow? _controlPanelWindow;
    private TrayIconService? _trayIconService;

    public MainWindow()
    {
        EnsureSingleInstance();

        InitializeComponent();

        _configManager = new ConfigManager();
        ThemeManager.ApplyTheme(_configManager.Config.Theme);
        _templateManager = new TemplateManager(_configManager);
        _authService = new AuthService(_configManager);
        _hookService = new KeyboardHookService(_configManager, _templateManager);

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
            Dispatcher.Invoke(UpdateStatusUi);
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
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        InitTrayService();
        _hookService.Start();

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
            if (!string.IsNullOrWhiteSpace(lastUser))
            {
                var (success, _, _) = _authService.ValidateUser(lastUser);
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

        _suggestionBar.ItemSelected += (item, matchedLen, targetHwnd) =>
        {
            Task.Run(() =>
            {
                TextInjectionService.ReplaceText(matchedLen, item.Content, targetHwnd);
            });
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
        string themeTag = ThemeManager.CurrentTheme switch
        {
            "WindowsVista" => "Vista Aero",
            "Windows10" => "Windows 10",
            _ => "Win98 ME"
        };

        // 활성 상태 프로그램창 투명도 적용 (바탕화면 은은하게 비침)
        Opacity = Math.Clamp(_configManager.Config.StatusWindowOpacity, 0.35, 1.0);

        if (isEnabled)
        {
            TxtTitleBar.Text = $"🟢 ChatHelper ({themeTag}) - 실행 중";
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00B000"));
            LblRunningStatus.Text = "자동완성 작동 중 (ON)";
            LblRunningStatus.Foreground = (Brush)FindResource("ThemeText");
            BtnTogglePause.Content = "⏸️ 일시정지";
        }
        else
        {
            TxtTitleBar.Text = $"🟡 ChatHelper ({themeTag}) - 일시정지됨";
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D08000"));
            LblRunningStatus.Text = "자동완성 일시정지 (OFF)";
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

    private void OpenControlPanel()
    {
        _suggestionBar.Hide();
        if (_controlPanelWindow == null || !_controlPanelWindow.IsLoaded)
        {
            _controlPanelWindow = new ControlPanelWindow(_configManager, _templateManager, _authService);
            _controlPanelWindow.SettingsChanged += () =>
            {
                UpdateStatusUi();
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
        UpdateStatusUi();
        _trayIconService?.ShowBalloon("구글 시트 동기화", msg);
    }

    private void ExitApp()
    {
        _hookService.Stop();
        _trayIconService?.Dispose();
        _suggestionBar.Close();
        _quickSearchWindow.Close();
        _controlPanelWindow?.Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        ExitApp();
        base.OnClosed(e);
    }
}
