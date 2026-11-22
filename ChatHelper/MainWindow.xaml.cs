using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private QuickSearchWindow? _quickSearchWindow;
    private SymbolPickerWindow? _symbolPickerWindow;
    private GlossaryWindow? _glossaryWindow;
    private ControlPanelWindow? _controlPanelWindow;
    private TrayIconService? _trayIconService;
    private bool _isOfflineMode = false;
    private System.Threading.Timer? _periodicSyncTimer;
    private System.Windows.Threading.DispatcherTimer? _tickerTimer;
    private System.Windows.Threading.DispatcherTimer? _noticeTimer;
    private DateTime _lastSyncTime = DateTime.Now;
    private readonly Random _rand = new();

    private const int HOTKEY_SEARCH_ID = 9001;
    private const int HOTKEY_AI_ID = 9002;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        EnsureSingleInstance();

        InitializeComponent();

        _configManager = new ConfigManager();
        ThemeManager.ApplyTheme(_configManager.Config.Theme);
        _templateManager = new TemplateManager(_configManager);
        _glossaryService = new GlossaryService(_configManager);
        _authService = new AuthService(_configManager);
        _authService.LastLoginReported += msg => AddStatusLog(msg);
        _hookService = new KeyboardHookService(_configManager, _templateManager, _glossaryService);

        _suggestionBar = new SuggestionBar(_configManager);

        if (!PerformInitialLogin())
        {
            Application.Current.Shutdown();
            return;
        }

        InitEvents();
        UpdateStatusUi();

        // 데이터 소스 프로필 전환/수정 시 키워드·용어집·톤앤매너 즉시 재동기화
        DataSourceResolver.ProfileChanged += () => Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await SyncRemoteAsync(); } catch (Exception ex) { AppLogger.Warn("MainWindow", "프로필 전환 동기화 실패", ex); }
        }));

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
        InitRollingTicker();

        // 앱 시작 시 구글 스프레드시트 최신 템플릿 비동기 동기화
        AddStatusLog("🔄 앱 시작 - 시트 데이터 불러오는 중...");
        _ = Task.Run(async () =>
        {
            await DataSourceResolver.RefreshRemoteProfilesAsync();
            var (success, syncMsg) = await _templateManager.SyncFromRemoteAsync();
            AddStatusLog(success ? $"✅ 시트 불러오기 완료 ({_templateManager.Templates.Count}개)" : $"⚠️ 시트 불러오기 실패: {syncMsg}");
            // 템플릿이 이미 로컬/원격으로 정상 로드되어 있으면 오프라인 경고를 띄우지 않음
            _isOfflineMode = !success && _templateManager.Templates.Count == 0;
            if (success) _lastSyncTime = DateTime.Now;
            Dispatcher.BeginInvoke(() =>
            {
                UpdateStatusUi();
                if (success)
                {
                    ShowNoticeBadge($"✅ 갱신완료! ({_templateManager.Templates.Count}개)", "#006000");
                }
                else
                {
                    AppLogger.Warn("MainWindow", $"초기 시트 동기화 실패 ({syncMsg}) -> 로컬 캐시({_templateManager.Templates.Count}개) 사용");
                }
            });
        });

        if (!_configManager.Config.ShowStatusWindow)
        {
            HideToTray(showNotification: true);
        }
        else
        {
            var user = _authService.CurrentUser;
            _trayIconService?.ShowBalloon($"ChatHelper 실행 중 ({user?.UserId})",
                "도킹 바 또는 트레이 아이콘에서 설정 및 검색을 열 수 있습니다.");
        }
    }

    private bool PerformInitialLogin()
    {
        // 비밀번호를 저장하지 않으므로 매 실행 시 로그인 창에서 인증 수행
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
        _trayIconService.OpenQuickSearchRequested += () => OpenQuickSearch(null);
        _trayIconService.ReloadTemplatesRequested += ReloadTemplates;
        _trayIconService.SyncRemoteRequested += async () => await SyncRemoteAsync();
        _trayIconService.ReloginRequested += Relogin;
        _trayIconService.ExitRequested += ExitApp;
    }

    private void InitEvents()
    {
        _hookService.CandidatesUpdated += (candidates, matchedLen, targetHwnd) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_hookService.HasActiveCandidates)
                {
                    _suggestionBar.SetRecognizedToken(_hookService.LastRecognizedToken);
                    _suggestionBar.ShowCandidates(candidates, matchedLen, targetHwnd);
                }
            });
        };

        _hookService.RecognitionStateChanged += (token, statusMode) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _suggestionBar.SetRecognizedToken(token);
                if (!_configManager.Config.IsEnabled) return;

                if (statusMode == "UNKNOWN")
                {
                    LblRunningStatus.Text = $"❓ [알아듣지 못함] 인식됨: \"{token}\"";
                    _suggestionBar.ShowStatusNotice(token, "UNKNOWN");
                }
                else if (statusMode == "WAITING")
                {
                    LblRunningStatus.Text = string.IsNullOrWhiteSpace(token)
                        ? "자동완성 작동 중 [⏳ 대기중]"
                        : $"⏳ [대기중] 입력 감지: \"{token}\"";
                }
                else if (statusMode == "MATCH")
                {
                    LblRunningStatus.Text = $"🔎 인식됨: \"{token}\"";
                }
            });
        };

        _hookService.RequestCloseCandidates += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _suggestionBar.Hide();
            });
        };

        _hookService.GlobalHotkeyTriggered += (targetHwnd) =>
        {
            Dispatcher.BeginInvoke(() => OpenQuickSearch(targetHwnd));
        };

        _hookService.AiHotkeyTriggered += (targetHwnd) =>
        {
            Dispatcher.BeginInvoke(() => OpenSymbolPicker(targetHwnd));
        };

        _hookService.AiCandidateReady += (aiItem, matchedLen, targetHwnd) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_hookService.HasActiveCandidates)
                    _suggestionBar.UpdateAiCandidate(aiItem, matchedLen, targetHwnd);
            });
        };

        _hookService.AiGeneratingStarted += (promptHint) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_configManager.Config.EnableKoToEnTranslation)
                    _suggestionBar.ShowAiGeneratingPlaceholder(promptHint);
            });
        };

        _suggestionBar.ItemSelected += (item, matchedLen, targetHwnd) =>
        {
            bool useShiftHome = _hookService.IsNaturalLanguageMode;
            _hookService.ClearAll();
            Task.Run(async () =>
            {
                TextInjectionService.ReplaceText(matchedLen, item.Content, targetHwnd, useShiftHome);
            });
        };

        _suggestionBar.QuickRegisterRequested += (recognizedWord) =>
        {
            Dispatcher.BeginInvoke(() => _ = HandleQuickRegisterAsync(recognizedWord));
        };

        _hookService.ItemAutoCompleted += (item) => { };
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
            StatusDot.Fill = _isOfflineMode ? ThemeColors.StatusAmber : ThemeColors.StatusGreen;
            LblRunningStatus.Text = _isOfflineMode ? "OFFLINE" : "ON";
            LblRunningStatus.Foreground = (Brush)(TryFindResource("ThemeText") ?? Brushes.Black);
        }
        else
        {
            TxtTitleBar.Text = AppConstants.TitleStatusPaused;
            StatusDot.Fill = ThemeColors.StatusAmber;
            LblRunningStatus.Text = "OFF";
            LblRunningStatus.Foreground = ThemeColors.StatusDarkRed;
            _suggestionBar.Hide();
        }

        LblSubStatus.Text = $"{userId} ({planInfo}) | {keyHint}";

        // 스프레드시트 동기화 실시간 상태 표시 (초슬림 도킹 바 포맷)
        LblSyncStatus.Text = $"↻ {_templateManager.Templates.Count}";
        LblSyncStatus.ToolTip = $"[{DataSourceResolver.ActiveName}] 마지막 동기화: {_lastSyncTime:HH:mm} ({_templateManager.Templates.Count}개) — 클릭 시 즉시 동기화";
        RefreshMainProfileCombo();
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

    private void BtnSymbolPicker_Click(object sender, RoutedEventArgs e)
    {
        OpenSymbolPicker();
    }

    private void BtnQuickAddTemplate_Click(object sender, RoutedEventArgs e)
    {
        _ = HandleQuickRegisterAsync(null);
    }

    private async Task HandleQuickRegisterAsync(string? initialKeyword)
    {
        if (_configManager.Config.QuickSaveWithoutDialog)
        {
            string clip = "";
            try { if (Clipboard.ContainsText()) clip = Clipboard.GetText().Trim(); } catch { }

            if (string.IsNullOrWhiteSpace(clip))
            {
                AddStatusLog("⚠️ 즉시 저장 실패: 클립보드에 복사된 답변이 없습니다.");
                ShowNoticeBadge("⚠️ 복사된 답변이 없습니다", "#B91C1C");
                return;
            }

            string kw = !string.IsNullOrWhiteSpace(initialKeyword)
                ? initialKeyword.Trim().TrimStart('\\', '/')
                : (clip.Length > 10 ? clip[..10].Replace("\r", "").Replace("\n", " ").Trim() : clip);

            var item = new Models.TemplateItem
            {
                Keyword = kw,
                ContentKeyword = kw,
                Title = kw,
                Category = "일반",
                Content = clip
            };

            AddStatusLog($"💾 '{kw}' 시트에 바로 저장 중...");
            ShowNoticeBadge($"⏳ '{kw}' 시트 저장 중...", "#B45309");
            _templateManager.AddTemplate(item);
            var (ok, msg) = await _templateManager.UploadTemplateToRemoteSheetAsync(item);
            AddStatusLog(ok ? $"✅ '{kw}' 시트 저장 완료!" : $"❌ '{kw}' 시트 저장 실패: {msg}");
            ShowNoticeBadge(ok ? $"✅ '{kw}' 저장 완료!" : "❌ 시트 저장 실패", ok ? "#006000" : "#B91C1C");
            UpdateStatusUi();
            return;
        }

        if (_quickSearchWindow == null || !_quickSearchWindow.IsLoaded)
        {
            _quickSearchWindow = new QuickSearchWindow(_templateManager, _configManager);
        }
        AddStatusLog("📝 새 답변 시트 등록 창 열림");
        _quickSearchWindow.OpenAddAndUploadDialog(this, initialKeyword);
        UpdateStatusUi();
    }

    private double _dockTopBeforeLog = double.NaN;

    private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
    {
        bool show = LogPanelBorder.Visibility != Visibility.Visible;
        const double collapsedH = 64;
        const double expandedH = 196;
        double delta = expandedH - collapsedH;
        var workArea = SystemParameters.WorkArea;

        if (show)
        {
            _dockTopBeforeLog = Top;
            LogPanelBorder.Visibility = Visibility.Visible;
            Height = expandedH;
            // 화면 하단 경계를 넘어가면 위로 자동 확장하여 잘리지 않게 함
            if (Top + expandedH > workArea.Bottom - 8)
            {
                Top = Math.Max(workArea.Top + 8, Top - delta);
            }
            BtnToggleLog.Content = "닫기";
        }
        else
        {
            LogPanelBorder.Visibility = Visibility.Collapsed;
            Height = collapsedH;
            if (!double.IsNaN(_dockTopBeforeLog))
            {
                Top = Math.Min(workArea.Bottom - collapsedH - 8, _dockTopBeforeLog);
                _dockTopBeforeLog = double.NaN;
            }
            BtnToggleLog.Content = "로그";
        }
    }

    public void AddStatusLog(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LstLiveLog == null) return;
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LstLiveLog.Items.Insert(0, line);
            while (LstLiveLog.Items.Count > 50)
                LstLiveLog.Items.RemoveAt(LstLiveLog.Items.Count - 1);
        });
    }

    private void OpenSymbolPicker(IntPtr? targetHwnd = null)
    {
        _suggestionBar.Hide();
        IntPtr hwnd = targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero
            ? targetHwnd.Value
            : Win32Api.GetForegroundWindow();

        uint myPid = (uint)Process.GetCurrentProcess().Id;
        Win32Api.GetWindowThreadProcessId(hwnd, out uint fgPid);
        if (fgPid == myPid && _hookService.LastTargetHwnd != IntPtr.Zero)
        {
            hwnd = _hookService.LastTargetHwnd;
        }

        if (_symbolPickerWindow == null || !_symbolPickerWindow.IsLoaded)
        {
            _symbolPickerWindow = new SymbolPickerWindow(_configManager);
            _symbolPickerWindow.SymbolSelected += (sym, dstHwnd) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TextInjectionService.ReplaceText(0, sym, dstHwnd);
                });
            };
        }

        _symbolPickerWindow.ShowForTarget(hwnd);
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
        var dlg = new ExitConfirmWindow();
        dlg.ShowDialog();

        if (dlg.Choice == ExitConfirmWindow.ExitChoice.Cancel)
            return;

        if (dlg.Choice == ExitConfirmWindow.ExitChoice.LogoutAndExit)
        {
            _configManager.Config.KeepLoggedIn = false;
            _configManager.Config.CurrentUserId = "";
            _configManager.Save();
        }
        else if (dlg.Choice == ExitConfirmWindow.ExitChoice.KeepLoginAndExit)
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

    private void OpenQuickSearch() => OpenQuickSearch(null);

    private void OpenQuickSearch(IntPtr? targetHwnd)
    {
        _suggestionBar.Hide();
        IntPtr hwnd = targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero
            ? targetHwnd.Value
            : Win32Api.GetForegroundWindow();

        uint myPid = (uint)Process.GetCurrentProcess().Id;
        Win32Api.GetWindowThreadProcessId(hwnd, out uint fgPid);
        if (fgPid == myPid && _hookService.LastTargetHwnd != IntPtr.Zero)
        {
            hwnd = _hookService.LastTargetHwnd;
        }

        if (_quickSearchWindow == null || !_quickSearchWindow.IsLoaded)
        {
            _quickSearchWindow = new QuickSearchWindow(_templateManager, _configManager);
            _quickSearchWindow.ItemSelected += (item, target) =>
            {
                Task.Run(() => TextInjectionService.FastPasteText(item.Content, target));
            };
        }

        _quickSearchWindow.ShowAtCursor(hwnd);
    }

    private void InitRollingTicker()
    {
        _tickerTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _tickerTimer.Tick += (s, e) => UpdateRollingTicker();
        _tickerTimer.Start();
        UpdateRollingTicker();
    }

    private void UpdateRollingTicker()
    {
        var templates = _templateManager.Templates;
        if (templates.Count == 0)
        {
            TxtRollingTicker.Text = "등록된 템플릿이 없습니다.";
            return;
        }

        int idx = _rand.Next(templates.Count);
        var item = templates[idx];
        string trigger = _configManager.Config.TriggerKey ?? "\\";
        TxtRollingTicker.Text = $"{trigger}{item.Keyword}  ➔  {item.Title}";
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (_hwndSource != null)
        {
            _hwndSource.AddHook(HwndHook);
            RegisterGlobalHotkeys(_hwndSource.Handle);
        }
    }

    private void RegisterGlobalHotkeys(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Alt + Q (0x51), Alt + X (0x58) 시스템 전역 핫키 등록
        Win32Api.RegisterHotKey(hwnd, HOTKEY_SEARCH_ID, Win32Api.MOD_ALT | Win32Api.MOD_NOREPEAT, 0x51);
        Win32Api.RegisterHotKey(hwnd, HOTKEY_AI_ID, Win32Api.MOD_ALT | Win32Api.MOD_NOREPEAT, 0x58);
    }

    private void UnregisterGlobalHotkeys(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Win32Api.UnregisterHotKey(hwnd, HOTKEY_SEARCH_ID);
        Win32Api.UnregisterHotKey(hwnd, HOTKEY_AI_ID);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_SEARCH_ID)
            {
                OpenQuickSearch();
                handled = true;
            }
            else if (id == HOTKEY_AI_ID)
            {
                OpenSymbolPicker();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void ShowNoticeBadge(string msg, string hexBgColor = "#006000")
    {
        Dispatcher.Invoke(() =>
        {
            TxtNotice.Text = msg;
            BadgeNotice.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexBgColor));
            BadgeNotice.Visibility = Visibility.Visible;

            _noticeTimer?.Stop();
            _noticeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _noticeTimer.Tick += (s, e) =>
            {
                _noticeTimer.Stop();
                BadgeNotice.Visibility = Visibility.Collapsed;
            };
            _noticeTimer.Start();
        });
    }

    private bool _mainProfileUpdating;

    private void RefreshMainProfileCombo()
    {
        if (CmbMainProfile == null) return;
        _mainProfileUpdating = true;
        try
        {
            CmbMainProfile.Items.Clear();
            var all = DataSourceResolver.GetAllProfiles();
            if (all.Count == 0)
                CmbMainProfile.Items.Add(DataSourceResolver.DefaultProfileName);
            foreach (var (p, _) in all)
                CmbMainProfile.Items.Add(p.Name);

            string active = !string.IsNullOrWhiteSpace(_configManager.Config.ActiveDataSourceProfile)
                ? _configManager.Config.ActiveDataSourceProfile
                : DataSourceResolver.ActiveName;
            int idx = CmbMainProfile.Items.IndexOf(active);
            CmbMainProfile.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally { _mainProfileUpdating = false; }
    }

    private async void CmbMainProfile_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_mainProfileUpdating || CmbMainProfile.SelectedItem is not string selected) return;

        bool isDefault = string.Equals(selected, DataSourceResolver.DefaultProfileName, StringComparison.OrdinalIgnoreCase);
        _configManager.Config.ActiveDataSourceProfile = isDefault ? "" : selected;
        _configManager.Save();

        CmbMainProfile.IsEnabled = false;
        ShowNoticeBadge($"⏳ '{selected}' 동기화 중...", "#B45309");
        try
        {
            await SyncRemoteAsync();
        }
        finally
        {
            CmbMainProfile.IsEnabled = true;
        }
    }

    private bool _isManualSyncing;

    private async void LblSyncStatus_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isManualSyncing) return;
        _isManualSyncing = true;
        ShowNoticeBadge("⏳ 시트 동기화 중...", "#B45309");
        try
        {
            await SyncRemoteAsync();
        }
        finally
        {
            _isManualSyncing = false;
        }
    }

    private async Task SyncRemoteAsync()
    {
        AddStatusLog($"🔄 '{DataSourceResolver.ActiveName}' 시트 불러오는 중...");
        await DataSourceResolver.RefreshRemoteProfilesAsync();
        var (success, msg) = await _templateManager.SyncFromRemoteAsync();
        _isOfflineMode = !success && _templateManager.Templates.Count == 0;
        _lastSyncTime = DateTime.Now;
        UpdateStatusUi();
        AddStatusLog(success ? $"✅ 동기화 완료 ({_templateManager.Templates.Count}개)" : $"❌ 동기화 실패: {msg}");
        if (success)
        {
            ShowNoticeBadge($"✅ 갱신완료! ({_templateManager.Templates.Count}개)", "#006000");
        }
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

                if (success)
                {
                    _isOfflineMode = false;
                    _lastSyncTime = DateTime.Now;
                    Dispatcher.BeginInvoke(() =>
                    {
                        UpdateStatusUi();
                        ShowNoticeBadge($"✅ 갱신완료! ({_templateManager.Templates.Count}개)", "#006000");
                    });
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

    private void ExitApp()
    {
        if (_hwndSource != null)
        {
            UnregisterGlobalHotkeys(_hwndSource.Handle);
            _hwndSource.RemoveHook(HwndHook);
        }
        _tickerTimer?.Stop();
        _noticeTimer?.Stop();
        _periodicSyncTimer?.Dispose();
        _hookService.Stop();
        _trayIconService?.Dispose();
        _suggestionBar.Close();
        _quickSearchWindow?.Close();
        _glossaryWindow?.Close();
        _controlPanelWindow?.Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        ExitApp();
        base.OnClosed(e);
    }
}
