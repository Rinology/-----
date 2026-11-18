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
    private readonly KeyboardHookService _hookService;

    private readonly SuggestionBar _suggestionBar;
    private readonly QuickSearchWindow _quickSearchWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _trayIconService;

    public MainWindow()
    {
        InitializeComponent();

        _configManager = new ConfigManager();
        _templateManager = new TemplateManager(_configManager);
        _hookService = new KeyboardHookService(_configManager, _templateManager);

        _suggestionBar = new SuggestionBar(_configManager);
        _quickSearchWindow = new QuickSearchWindow(_templateManager);

        InitTrayMenu();
        InitEvents();

        _hookService.Start();

        var trigger = _configManager.Config.TriggerKey ?? "\\";
        _trayIconService?.ShowBalloon("채팅 도우미 시작됨 (C# .NET 8)",
            $"• 스마트폰 커서 추적: 타이핑 시 커서 옆 표시\n• 방해 방지 완성: [{trigger}+Tab] 또는 [{trigger}+1~3]\n• 트레이 메뉴 ➔ 설정에서 모드 변경 가능");
    }

    private void InitTrayMenu()
    {
        var menu = new ContextMenu();

        var itemOpen = new MenuItem { Header = "자주 쓰는 말 열기 (Alt+Q)" };
        itemOpen.Click += (s, e) => OpenQuickSearch();

        var itemSettings = new MenuItem { Header = "⚙️ 자동완성 모드 설정..." };
        itemSettings.Click += (s, e) => OpenSettings();

        var itemReload = new MenuItem { Header = "CSV 템플릿 다시 불러오기" };
        itemReload.Click += (s, e) => ReloadTemplates();

        var itemSync = new MenuItem { Header = "구글 시트에서 최신 동기화" };
        itemSync.Click += async (s, e) => await SyncRemoteAsync();

        var itemExit = new MenuItem { Header = "종료" };
        itemExit.Click += (s, e) => ExitApp();

        menu.Items.Add(itemOpen);
        menu.Items.Add(itemSettings);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemReload);
        menu.Items.Add(itemSync);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemExit);

        _trayIconService = new TrayIconService(this, menu);
        _trayIconService.Initialize();
        _trayIconService.DoubleClicked += OpenQuickSearch;
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

    private void OpenSettings()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_configManager, _templateManager);
            _settingsWindow.SettingsSaved += () =>
            {
                _trayIconService?.ShowBalloon("설정 적용 완료", "새로운 설정이 즉시 반영되었습니다.");
            };
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
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
        _settingsWindow?.Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        ExitApp();
        base.OnClosed(e);
    }
}
