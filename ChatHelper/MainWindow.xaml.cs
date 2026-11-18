using System.Windows;
using System.Windows.Controls;
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
        InitializeComponent();

        _configManager = new ConfigManager();
        _templateManager = new TemplateManager(_configManager);
        _authService = new AuthService(_configManager);
        _hookService = new KeyboardHookService(_configManager, _templateManager);

        _suggestionBar = new SuggestionBar(_configManager);
        _quickSearchWindow = new QuickSearchWindow(_templateManager);

        // 1. 로그인/라이선스 검증
        if (!PerformInitialLogin())
        {
            Application.Current.Shutdown();
            return;
        }

        // 2. 트레이 및 이벤트 초기화
        InitTrayMenu();
        InitEvents();

        // 3. 백그라운드 훅 시작
        _hookService.Start();

        var user = _authService.CurrentUser;
        var trigger = _configManager.Config.TriggerKey ?? "\\";
        _trayIconService?.ShowBalloon($"로그온 완료: {user?.UserId} (v2.0.0 ME)",
            $"• 만료일: {user?.ExpireDate:yyyy-MM-dd} (D-{user?.DaysRemaining}일 남음)\n• 추천 바: 일반 타이핑 ➔ [{trigger}+Tab] 완성\n• 트레이 우클릭 ➔ 제어판에서 커맨드 추가/수정 가능");
    }

    private bool PerformInitialLogin()
    {
        string lastUser = _configManager.Config.CurrentUserId;
        if (!string.IsNullOrWhiteSpace(lastUser))
        {
            var (success, _, _) = _authService.ValidateUser(lastUser);
            if (success)
            {
                return true; // 자동 로그인 성공
            }
        }

        // 로그인 팝업
        var loginWin = new LoginWindow(_authService, _configManager);
        var result = loginWin.ShowDialog();
        return result == true && loginWin.IsAuthenticated;
    }

    private void InitTrayMenu()
    {
        var menu = new ContextMenu();

        var itemOpen = new MenuItem { Header = "자주 쓰는 말 열기 (Alt+Q)" };
        itemOpen.Click += (s, e) => OpenQuickSearch();

        var itemControlPanel = new MenuItem { Header = "💻 ChatHelper 제어판 (커맨드/단축키/설정)..." };
        itemControlPanel.Click += (s, e) => OpenControlPanel();

        var itemReload = new MenuItem { Header = "CSV 템플릿 다시 불러오기" };
        itemReload.Click += (s, e) => ReloadTemplates();

        var itemSync = new MenuItem { Header = "구글 시트에서 최신 동기화" };
        itemSync.Click += async (s, e) => await SyncRemoteAsync();

        var itemRelogin = new MenuItem { Header = "🔑 다른 사용자로 다시 로그온..." };
        itemRelogin.Click += (s, e) => Relogin();

        var itemExit = new MenuItem { Header = "종료" };
        itemExit.Click += (s, e) => ExitApp();

        menu.Items.Add(itemOpen);
        menu.Items.Add(itemControlPanel);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemReload);
        menu.Items.Add(itemSync);
        menu.Items.Add(itemRelogin);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemExit);

        _trayIconService = new TrayIconService(this, menu);
        _trayIconService.Initialize();
        _trayIconService.DoubleClicked += OpenControlPanel;
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

    private void OpenQuickSearch()
    {
        IntPtr hwnd = Win32Api.GetForegroundWindow();
        _quickSearchWindow.ShowAtCursor(hwnd);
    }

    private void OpenControlPanel()
    {
        if (_controlPanelWindow == null || !_controlPanelWindow.IsLoaded)
        {
            _controlPanelWindow = new ControlPanelWindow(_configManager, _templateManager, _authService);
            _controlPanelWindow.SettingsChanged += () =>
            {
                _trayIconService?.ShowBalloon("제어판 설정 적용", "커맨드 및 단축키 설정이 실시간 적용되었습니다.");
            };
        }
        _controlPanelWindow.Show();
        _controlPanelWindow.Activate();
    }

    private void Relogin()
    {
        var loginWin = new LoginWindow(_authService, _configManager);
        if (loginWin.ShowDialog() == true)
        {
            var user = _authService.CurrentUser;
            _trayIconService?.ShowBalloon("로그온 변경", $"'{user?.UserId}' 계정으로 전환되었습니다. (만료일: {user?.ExpireDate:yyyy-MM-dd})");
        }
    }

    private void ReloadTemplates()
    {
        int count = _templateManager.LoadTemplates();
        _trayIconService?.ShowBalloon("템플릿 갱신", $"{count}개 템플릿 로드 완료");
    }

    private async Task SyncRemoteAsync()
    {
        var (success, msg) = await _templateManager.SyncFromRemoteAsync();
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
