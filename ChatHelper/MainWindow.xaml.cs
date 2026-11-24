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
    private bool _hotkeyHookAttached;
    private bool _hotkeysRegistered;

    public MainWindow()
    {
        EnsureSingleInstance();

        InitializeComponent();

        _configManager = new ConfigManager();
        ThemeManager.ApplyTheme(_configManager.Config.Theme, _configManager.Config.AccentColor);
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
        // 메인 모니터 중앙 하단 배치 (우측 하단 시스템 알림과 겹침 방지)
        Left = workArea.Left + (workArea.Width - Width) / 2.0;
        Top = workArea.Bottom - Height - 16;

        InitTrayService();
        EnsureGlobalHotkeyHookRegistered();
        _hookService.Start();
        InitPeriodicSyncTimer();
        InitRollingTicker();
        UpdateStatusUi();

        // 앱 시작 시 구글 스프레드시트 최신 템플릿 비동기 동기화
        AddStatusLog("🔄 앱 시작 - 시트 데이터 불러오는 중...");
        if (!string.IsNullOrEmpty(_authService.LastLoginLogMessage))
        {
            AddStatusLog(_authService.LastLoginLogMessage);
        }
        else if (_authService.LastLoginReportTask != null)
        {
            var loginTask = _authService.LastLoginReportTask;
            _ = Task.Run(async () =>
            {
                await loginTask;
                if (!string.IsNullOrEmpty(_authService.LastLoginLogMessage))
                    AddStatusLog(_authService.LastLoginLogMessage);
            });
        }
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
                    if (TxtDockRecognitionStatus != null)
                        TxtDockRecognitionStatus.Text = $"❓ [미일치] 입력됨: \"{token}\"";
                    _suggestionBar.ShowStatusNotice(token, "UNKNOWN");
                }
                else if (statusMode == "WAITING")
                {
                    if (TxtDockRecognitionStatus != null)
                        TxtDockRecognitionStatus.Text = string.IsNullOrWhiteSpace(token)
                            ? $"⏳ 대기중 — 현재 시트({DataSourceResolver.ActiveName}) {_templateManager.Templates.Count}개"
                            : $"⏳ [입력 감지] \"{token}\"";
                }
                else if (statusMode == "MATCH")
                {
                    if (TxtDockRecognitionStatus != null)
                        TxtDockRecognitionStatus.Text = $"🔎 [매칭 인식됨] \"{token}\"";
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

        _hookService.TranslateHotkeyTriggered += (targetHwnd) =>
        {
            _ = HandleSelectionTranslateAsync(targetHwnd);
        };

        _hookService.QuickRegisterHotkeyTriggered += (targetHwnd) =>
        {
            _ = HandleSelectionQuickRegisterAsync(targetHwnd);
        };

        _suggestionBar.SelectionReplaceRequested += (item, targetHwnd) =>
        {
            Task.Run(() => TextInjectionService.FastPasteText(item.Content, targetHwnd));
        };

        _suggestionBar.AutoHideExpired += () =>
        {
            _hookService.ClearAll();
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

        // 활성 상태 프로그램창 투명도 적용 (Win32 LWA_ALPHA로 바탕화면·뒤 창이 실제로 은은하게 비침)
        double stAlpha = Math.Clamp(_configManager.Config.StatusWindowOpacity, AppConstants.MinStatusWindowOpacity, AppConstants.MaxStatusWindowOpacity);
        Win32Api.SetWindowAlpha(this, stAlpha);

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

        // 스프레드시트 동기화 실시간 상태 표시 (1/3 도킹 바 & 2/3 인식 현황 항상 동기화)
        LblSyncStatus.Text = $"↻ {_templateManager.Templates.Count}";
        LblSyncStatus.ToolTip = $"[{DataSourceResolver.ActiveName}] 마지막 동기화: {_lastSyncTime:HH:mm} ({_templateManager.Templates.Count}개) — 클릭 시 즉시 동기화";
        if (TxtDockRecognitionStatus != null && !_hookService.HasActiveCandidates && string.IsNullOrEmpty(_hookService.LastRecognizedToken))
        {
            TxtDockRecognitionStatus.Text = isEnabled
                ? $"⏳ 대기중 — 현재 시트({DataSourceResolver.ActiveName}) {_templateManager.Templates.Count}개"
                : "⏸️ 자동완성 일시정지됨 (OFF)";
        }
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

    private async Task HandleSelectionTranslateAsync(IntPtr targetHwnd)
    {
        if (!_configManager.Config.EnableKoToEnTranslation)
        {
            Dispatcher.BeginInvoke(() =>
            {
                AddStatusLog("ℹ️ 영문 번역 기능이 꺼져 있습니다. 제어판에서 '구글 영문 번역'을 켜주세요.");
                ShowNoticeBadge("ℹ️ 제어판에서 영문 번역을 켜주세요", "#B45309");
            });
            return;
        }

        string selected = await Task.Run(() => TextInjectionService.CaptureSelectedText(targetHwnd));
        if (string.IsNullOrWhiteSpace(selected))
        {
            Dispatcher.BeginInvoke(() =>
            {
                AddStatusLog("⚠️ 번역할 문장을 먼저 마우스로 드래그(선택)한 뒤 Alt+T를 눌러주세요.");
                ShowNoticeBadge("⚠️ 번역할 문장을 드래그 후 Alt+T", "#B45309");
            });
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            AddStatusLog($"🌐 선택 문장 번역 중... (\"{selected}\")");
            _suggestionBar.ShowAiGeneratingPlaceholder(selected, targetHwnd, isSelectionMode: true);
        });

        string enAnswer = await DataSourceResolver.TranslateViaGasAsync(selected, "ko", "en");
        await Dispatcher.InvokeAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(enAnswer))
            {
                _suggestionBar.Hide();
                AddStatusLog("❌ Google 영문 번역 응답이 비어 있거나 실패했습니다.");
                ShowNoticeBadge("❌ 번역 실패 (로그 확인)", "#B91C1C");
                return;
            }

            var transItem = new Models.TemplateItem
            {
                Keyword = selected,
                ContentKeyword = "영문번역",
                Title = "Google 영문 번역",
                Content = enAnswer,
                Category = "AI번역"
            };
            _suggestionBar.UpdateAiCandidate(transItem, 0, targetHwnd);
            AddStatusLog($"✅ 번역 완료: {enAnswer}");
        });
    }

    private async Task HandleSelectionQuickRegisterAsync(IntPtr targetHwnd)
    {
        string selected = await Task.Run(() => TextInjectionService.CaptureSelectedText(targetHwnd));
        await Dispatcher.InvokeAsync(() => _ = HandleQuickRegisterAsync(null, selected));
    }

    private async Task HandleQuickRegisterAsync(string? initialKeyword, string? initialContent = null)
    {
        if (_configManager.Config.QuickSaveWithoutDialog)
        {
            string clip = !string.IsNullOrWhiteSpace(initialContent)
                ? initialContent.Trim()
                : TextInjectionService.GetClipboardTextSafe().Trim();

            if (string.IsNullOrWhiteSpace(clip))
            {
                AddStatusLog("⚠️ 즉시 저장 실패: 드래그 선택 또는 복사된 답변이 없습니다.");
                ShowNoticeBadge("⚠️ 선택/복사된 답변이 없습니다", "#B91C1C");
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

        AddStatusLog("📝 시트 빠른 등록 창 열림");
        QuickRegisterDialog.Show(_templateManager, this, initialKeyword, initialContent, (newItem, ok, msg) =>
        {
            AddStatusLog(ok ? $"✅ '{newItem.Keyword}' 시트 저장 완료!" : $"⚠️ '{newItem.Keyword}' 시트 저장 실패: {msg}");
            ShowNoticeBadge(ok ? $"✅ '{newItem.Keyword}' 저장 완료!" : "❌ 시트 저장 실패", ok ? "#006000" : "#B91C1C");
            UpdateStatusUi();
        });
    }

    private double _dockTopBeforeLog = double.NaN;

    private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
    {
        bool show = LogPanelBorder.Visibility != Visibility.Visible;
        const double collapsedH = 48;
        const double expandedH = 180;
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

    private int _dockViewIndex = 0; // 0: 메뉴&시트, 1: 인식현황, 2: 최근로그

    private void BtnDockViewPrev_Click(object sender, RoutedEventArgs e)
    {
        _dockViewIndex = (_dockViewIndex + 2) % 3;
        ApplyDockView();
    }

    private void BtnDockViewNext_Click(object sender, RoutedEventArgs e)
    {
        _dockViewIndex = (_dockViewIndex + 1) % 3;
        ApplyDockView();
    }

    private void ApplyDockView()
    {
        if (DockViewMenu == null || DockViewRecognition == null || DockViewLogSummary == null) return;
        DockViewMenu.Visibility = _dockViewIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        DockViewRecognition.Visibility = _dockViewIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        DockViewLogSummary.Visibility = _dockViewIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (TxtDockViewIndex != null)
            TxtDockViewIndex.Text = $"{_dockViewIndex + 1}/3";
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
            if (TxtDockLastLog != null)
                TxtDockLastLog.Text = line;
        });
    }

    private void OpenSymbolPicker(IntPtr? targetHwnd = null)
    {
        _hookService.ClearAll();
        _suggestionBar.Hide();
        IntPtr hwnd = targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero
            ? targetHwnd.Value
            : Win32Api.GetForegroundWindow();

        if (Win32Api.IsValidTargetWindow(hwnd))
        {
            _hookService.RememberTargetWindow(hwnd);
        }
        else if (_hookService.LastTargetHwnd != IntPtr.Zero)
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
        var dlg = new ExitConfirmWindow(_configManager.Config.LogoutOnExit);
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
        _hookService.ClearAll();
        _suggestionBar.Hide();
        IntPtr hwnd = targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero
            ? targetHwnd.Value
            : Win32Api.GetForegroundWindow();

        if (Win32Api.IsValidTargetWindow(hwnd))
        {
            _hookService.RememberTargetWindow(hwnd);
        }
        else if (_hookService.LastTargetHwnd != IntPtr.Zero)
        {
            hwnd = _hookService.LastTargetHwnd;
        }

        if (_quickSearchWindow == null || !_quickSearchWindow.IsLoaded)
        {
            _quickSearchWindow = new QuickSearchWindow(_templateManager, _configManager);
            _quickSearchWindow.ItemSelected += (item, target) =>
            {
                IntPtr dst = Win32Api.IsValidTargetWindow(target) ? target : _hookService.LastTargetHwnd;
                Task.Run(() => TextInjectionService.FastPasteText(item.Content, dst));
            };
        }

        try
        {
            AppLogger.Info("MainWindow", $"OpenQuickSearch 호출 (target=0x{hwnd.ToInt64():X})");
            _quickSearchWindow.ShowAtCursor(hwnd);
        }
        catch (Exception ex)
        {
            AppLogger.Error("MainWindow", "QuickSearchWindow 열기 실패", ex);
            AddStatusLog($"❌ Alt+Q 검색창 열기 실패: {ex.Message}");
        }
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
        if (Win32Api.IsValidTargetWindow(out var currentFg))
        {
            _hookService.RememberTargetWindow(currentFg);
        }
        _hookService.ClearAll();
        _suggestionBar.Hide();
        if (_controlPanelWindow == null || !_controlPanelWindow.IsLoaded)
        {
            _controlPanelWindow = new ControlPanelWindow(_configManager, _templateManager, _authService);
            _controlPanelWindow.SettingsChanged += () =>
            {
                UpdateStatusUi();
                RefreshGlobalHotkeys();
                RestartPeriodicSyncTimer();
                if (_configManager.Config.ShowStatusWindow)
                    ShowStatusWindow();
                else
                    HideToTray(showNotification: false);

                // 제어판 저장 후 직전 외부 채팅창이 있으면 포커스를 자연스럽게 되돌려 즉시 Alt+Q 및 타이핑이 동작하도록 보장
                if (Win32Api.IsValidTargetWindow(_hookService.LastTargetHwnd))
                {
                    Win32Api.ForceForegroundWindow(_hookService.LastTargetHwnd);
                }

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
        EnsureGlobalHotkeyHookRegistered();
    }

    private void EnsureGlobalHotkeyHookRegistered()
    {
        if (_hotkeysRegistered) return;

        IntPtr handle = new WindowInteropHelper(this).Handle;
        _hwndSource ??= (handle != IntPtr.Zero ? HwndSource.FromHwnd(handle) : null)
            ?? PresentationSource.FromVisual(this) as HwndSource;

        if (_hwndSource == null || _hwndSource.Handle == IntPtr.Zero)
        {
            AppLogger.Warn("MainWindow", $"EnsureGlobalHotkeyHookRegistered 보류: hwnd=0x{handle.ToInt64():X}, hwndSourceNull={_hwndSource == null}");
            return;
        }

        if (!_hotkeyHookAttached)
        {
            _hwndSource.AddHook(HwndHook);
            _hotkeyHookAttached = true;
        }

        RegisterGlobalHotkeys(_hwndSource.Handle);
        _hotkeysRegistered = true;
    }

    private void RefreshGlobalHotkeys()
    {
        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            UnregisterGlobalHotkeys(_hwndSource.Handle);
            RegisterGlobalHotkeys(_hwndSource.Handle);
        }
    }

    private void RegisterGlobalHotkeys(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var hotkey = HotkeyParser.ResolveOrDefault(_configManager.Config.GlobalHotkey);
        bool okQ = Win32Api.RegisterHotKey(hwnd, HOTKEY_SEARCH_ID, hotkey.ModFlags | Win32Api.MOD_NOREPEAT, (uint)hotkey.VkCode);
        int errQ = okQ ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (!okQ)
        {
            AppLogger.Warn("MainWindow", $"RegisterHotKey({hotkey.NormalizedLabel}) 실패 (err={errQ}) — 저수준 키훅 폴백 사용");
            AddStatusLog($"⚠️ {hotkey.NormalizedLabel} OS 핫키 선점됨(err={errQ}) — 키훅 모드로 동작");
        }
        bool okX = Win32Api.RegisterHotKey(hwnd, HOTKEY_AI_ID, Win32Api.MOD_ALT | Win32Api.MOD_NOREPEAT, 0x58);
        int errX = okX ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (!okX)
        {
            AppLogger.Warn("MainWindow", $"RegisterHotKey(Alt+X) 실패 (err={errX}) — 저수준 키훅 폴백 사용");
        }
        AppLogger.Info("MainWindow", $"RegisterGlobalHotkeys: hwnd=0x{hwnd.ToInt64():X}, {hotkey.NormalizedLabel}={okQ}(err={errQ}), Alt+X={okX}(err={errX})");
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
                AppLogger.Info("MainWindow", "WM_HOTKEY(Alt+Q) 수신");
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
