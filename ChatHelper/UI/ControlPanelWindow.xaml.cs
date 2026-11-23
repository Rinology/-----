using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatHelper.Constants;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class ControlPanelWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly TemplateManager _templateManager;
    private readonly AuthService _authService;

    private ObservableCollection<TemplateItem> _templatesObs = new();

    public event Action? SettingsChanged;

    private bool _isInitializing = true;

    public ControlPanelWindow(ConfigManager configManager, TemplateManager templateManager, AuthService authService)
    {
        _configManager = configManager;
        _templateManager = templateManager;
        _authService = authService;

        InitializeComponent();

        var work = SystemParameters.WorkArea;
        Width = Math.Min(1320, Math.Max(980, work.Width * 0.94));
        Height = Math.Min(840, Math.Max(660, work.Height * 0.92));

        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }

        _templateManager.TemplatesSynced += OnTemplatesSynced;
        Closed += (_, _) => _templateManager.TemplatesSynced -= OnTemplatesSynced;

        LoadData();
        LoadProfiles();
        _isInitializing = false;
        UpdateDesignPreview();
    }

    private void OnTemplatesSynced()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _templatesObs = new ObservableCollection<TemplateItem>(_templateManager.Templates);
            TxtCmdFilter_TextChanged(this, null!);
            LoadProfiles();
            LblStatusBar.Text = $"템플릿: {_templatesObs.Count}개 동기화됨 | 로그인: {_configManager.Config.CurrentUserId}";
        });
    }

    private void TxtContent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtContentLength != null && TxtContent != null)
        {
            TxtContentLength.Text = $"{TxtContent.Text.Length}자";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void LoadData()
    {
        // 1. 템플릿 목록 로드
        _templatesObs = new ObservableCollection<TemplateItem>(_templateManager.Templates);
        DgTemplates.ItemsSource = _templatesObs;

        // 2. 단축키 설정 로드
        var cfg = _configManager.Config;
        TxtGlobalHotkey.Text = cfg.GlobalHotkey ?? "Alt+Q";
        TxtTriggerKey.Text = cfg.TriggerKey ?? "\\";
        ChkNumbers.IsChecked = cfg.EnableNumberSelection;

        if (cfg.CompleteKeyMode == "SlashOnly")
            RadioSlashOnly.IsChecked = true;
        else if (cfg.CompleteKeyMode == "SlashTab")
            RadioSlashTab.IsChecked = true;
        else
            RadioShiftSpace.IsChecked = true;

        // 3. UI 포인트 컬러 및 알약 크기 설정 로드
        switch ((cfg.AccentColor ?? "Blue").Trim())
        {
            case "Green": RadioAccentGreen.IsChecked = true; break;
            case "Lavender": RadioAccentLavender.IsChecked = true; break;
            case "Peach": RadioAccentPeach.IsChecked = true; break;
            case "Pink": RadioAccentPink.IsChecked = true; break;
            default: RadioAccentBlue.IsChecked = true; break;
        }

        if (cfg.PillScale == 1)
            RadioPillScale1.IsChecked = true;
        else if (cfg.PillScale == 3)
            RadioPillScale3.IsChecked = true;
        else
            RadioPillScale2.IsChecked = true;

        // 알약 위치 및 간격
        if (cfg.PillPlacement == "ScreenBottomRight")
            RadioPosFixed.IsChecked = true;
        else if (cfg.PillPlacement == "MousePointer")
            RadioPosMouse.IsChecked = true;
        else if (cfg.PillOffsetY >= 55)
            RadioPosFar.IsChecked = true;
        else if (cfg.PillOffsetY <= 20)
            RadioPosNearby1.IsChecked = true;
        else
            RadioPosNearby2.IsChecked = true;

        // 창 투명도 (흐리게)
        if (cfg.WindowOpacity <= 0.75)
            RadioOpacity70.IsChecked = true;
        else if (cfg.WindowOpacity <= 0.85)
            RadioOpacity80.IsChecked = true;
        else if (cfg.WindowOpacity <= 0.95)
            RadioOpacity90.IsChecked = true;
        else
            RadioOpacity100.IsChecked = true;

        // 상태창 투명도 (바탕화면 비침)
        if (cfg.StatusWindowOpacity <= 0.50)
            RadioStatusOpacity45.IsChecked = true;
        else if (cfg.StatusWindowOpacity <= 0.70)
            RadioStatusOpacity65.IsChecked = true;
        else if (cfg.StatusWindowOpacity <= 0.90)
            RadioStatusOpacity85.IsChecked = true;
        else
            RadioStatusOpacity100.IsChecked = true;

        if (cfg.DisplayMode == "HorizontalBar")
            RadioModeHorizontal.IsChecked = true;
        else if (cfg.DisplayMode == "ManualOnly")
            RadioModeManual.IsChecked = true;
        else
            RadioModeInline.IsChecked = true;

        // 멀티 알약 최대 개수 (3~10)
        int maxCountIdx = Math.Clamp(cfg.MaxCandidatesCount, AppConstants.MinCandidatesCount, AppConstants.MaxCandidatesCount) - AppConstants.MinCandidatesCount;
        CmbMaxCandidates.SelectedIndex = maxCountIdx;

        UpdateSlashOnlyLock();

        ChkShowStatusWindow.IsChecked = cfg.ShowStatusWindow;
        ChkEnableMathCalc.IsChecked = cfg.EnableMathCalc;
        ChkEnableTranslation.IsChecked = cfg.EnableKoToEnTranslation;
        if (ChkLogoutOnExit != null)
            ChkLogoutOnExit.IsChecked = cfg.LogoutOnExit;

        int hideSec = Math.Clamp(cfg.AutoHideSeconds, AppConstants.MinAutoHideSeconds, AppConstants.MaxAutoHideSeconds);
        foreach (System.Windows.Controls.ComboBoxItem item in CmbAutoHideSeconds.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int tagSec) && tagSec == hideSec)
            {
                CmbAutoHideSeconds.SelectedItem = item;
                break;
            }
        }

        // 4. 자동 동기화 주기 및 유저 정보
        int syncInterval = cfg.AutoSyncIntervalMinutes;
        int comboIdx = syncInterval switch
        {
            5 => 0,
            10 => 1,
            15 => 2,
            30 => 3,
            60 => 4,
            0 => 5,
            _ => 2 // 기본값 15분
        };
        CmbAutoSyncInterval.SelectedIndex = comboIdx;
        ChkQuickSaveWithoutDialog.IsChecked = cfg.QuickSaveWithoutDialog;
        UpdateUserInfoDisplay();

        // 5. 하단 상태표시줄
        LblStatusBar.Text = $"템플릿: {_templatesObs.Count}개 로드됨 | 로그인: {cfg.CurrentUserId}";
    }

    private void UpdateUserInfoDisplay()
    {
        var user = _authService.CurrentUser;
        if (user != null)
        {
            LblCurrentUserInfo.Text = $"아이디: {user.UserId}  |  등급: {user.Plan}  |  만료일: {user.ExpireDate:yyyy-MM-dd} (D-{user.DaysRemaining}일 남음)";
        }
        else
        {
            LblCurrentUserInfo.Text = "로그온된 사용자가 없습니다.";
        }
    }

    // --- 템플릿 CRUD (필터 검색 + 구글 시트 즉시 동기화) ---
    private void TxtCmdFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = TxtCmdFilter.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            DgTemplates.ItemsSource = _templatesObs;
            return;
        }
        DgTemplates.ItemsSource = _templateManager.Search(q, "전체");
    }

    private void DgTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgTemplates.SelectedItem is TemplateItem item)
        {
            TxtKw.Text = item.Keyword;
            TxtTitle.Text = item.Title;
            TxtCat.Text = item.Category;
            TxtContent.Text = item.Content;
        }
    }

    private void DgTemplates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgTemplates.SelectedItem is TemplateItem)
        {
            TxtContent.Focus();
            TxtContent.CaretIndex = TxtContent.Text.Length;
            LblStatusBar.Text = "하단 입력란에서 수정 후 [✏️ 수정 적용 (시트 반영)]을 누르세요.";
        }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtKw.Text) && string.IsNullOrWhiteSpace(TxtTitle.Text))
        {
            MessageBox.Show("단축어나 제목을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = new TemplateItem
        {
            Keyword = TxtKw.Text.Trim(),
            ContentKeyword = TxtKw.Text.Trim(),
            Title = TxtTitle.Text.Trim(),
            Category = string.IsNullOrWhiteSpace(TxtCat.Text) ? "일반" : TxtCat.Text.Trim(),
            Content = TxtContent.Text
        };

        _templateManager.AddTemplate(item);
        _templatesObs.Add(item);
        BtnClearForm_Click(sender, e);
        LblStatusBar.Text = $"⏳ '{item.Title}' 구글 시트에 추가 중...";
        var (ok, msg) = await _templateManager.UploadTemplateToRemoteSheetAsync(item);
        LblStatusBar.Text = ok ? $"✅ '{item.Title}' 시트 추가 완료!" : $"⚠️ 로컬 추가됨 (시트 오류: {msg})";
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (DgTemplates.SelectedItem is not TemplateItem selected)
        {
            MessageBox.Show("수정할 항목을 목록에서 먼저 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int idx = _templatesObs.IndexOf(selected);
        if (idx < 0) idx = _templateManager.Templates.IndexOf(selected);
        string originalKw = selected.Keyword;

        selected.Keyword = TxtKw.Text.Trim();
        selected.ContentKeyword = string.IsNullOrWhiteSpace(selected.ContentKeyword) ? selected.Keyword : selected.ContentKeyword;
        selected.Title = TxtTitle.Text.Trim();
        selected.Category = string.IsNullOrWhiteSpace(TxtCat.Text) ? "일반" : TxtCat.Text.Trim();
        selected.Content = TxtContent.Text;

        if (idx >= 0) _templateManager.UpdateTemplate(idx, selected);
        DgTemplates.Items.Refresh();
        LblStatusBar.Text = $"⏳ '{selected.Title}' 구글 시트 수정 반영 중...";
        var (ok, msg) = await _templateManager.UpdateTemplateOnRemoteSheetAsync(originalKw, selected);
        LblStatusBar.Text = ok ? $"✅ '{selected.Title}' 시트 수정 완료!" : $"⚠️ 로컬 수정됨 (시트 오류: {msg})";
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (DgTemplates.SelectedItem is not TemplateItem item)
        {
            MessageBox.Show("삭제할 항목을 먼저 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"'{item.Title}' 항목을 로컬 및 구글 시트에서 삭제할까요?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            int idx = _templateManager.Templates.IndexOf(item);
            if (idx >= 0) _templateManager.DeleteTemplate(idx);
            _templatesObs.Remove(item);
            TxtCmdFilter_TextChanged(sender, null!);
            BtnClearForm_Click(sender, e);

            LblStatusBar.Text = $"⏳ '{item.Title}' 구글 시트에서 삭제 중...";
            var (ok, msg) = await _templateManager.DeleteTemplateFromRemoteSheetAsync(item.Keyword);
            LblStatusBar.Text = ok ? $"✅ '{item.Title}' 시트 삭제 완료." : $"⚠️ 로컬 삭제됨 (시트 오류: {msg})";
        }
    }

    private void BtnClearForm_Click(object sender, RoutedEventArgs e)
    {
        TxtKw.Clear();
        TxtTitle.Clear();
        TxtCat.Clear();
        TxtContent.Clear();
        DgTemplates.SelectedItem = null;
    }

    // --- 단축키 충돌 검사 ---
    private void TxtGlobalHotkey_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LblHotkeyWarning == null || TxtGlobalHotkey == null) return;

        string hk = TxtGlobalHotkey.Text.Trim();
        var (isConflict, msg) = HotkeyValidator.ValidateHotkey(hk);

        if (isConflict)
        {
            LblHotkeyWarning.Text = msg;
            LblHotkeyWarning.Foreground = Brushes.Red;
        }
        else
        {
            LblHotkeyWarning.Text = msg;
            LblHotkeyWarning.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#006000"));
        }
    }

    // --- 완성 키 모드 변경 (SlashOnly 시 단일 알약 고정 및 안내) ---
    private void CompleteKeyMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateSlashOnlyLock();
    }

    private void UpdateSlashOnlyLock()
    {
        if (RadioSlashOnly == null || RadioModeHorizontal == null || RadioModeInline == null) return;

        if (RadioSlashOnly.IsChecked == true)
        {
            RadioModeInline.IsChecked = true;
            RadioModeHorizontal.IsEnabled = false;
            if (CmbMaxCandidates != null) CmbMaxCandidates.IsEnabled = false;
            if (ChkNumbers != null) ChkNumbers.IsEnabled = false;
            if (TxtSlashOnlyNotice != null) TxtSlashOnlyNotice.Visibility = Visibility.Visible;
        }
        else
        {
            RadioModeHorizontal.IsEnabled = true;
            bool isMulti = RadioModeHorizontal.IsChecked == true;
            if (CmbMaxCandidates != null) CmbMaxCandidates.IsEnabled = isMulti;
            if (ChkNumbers != null) ChkNumbers.IsEnabled = isMulti;
            if (TxtSlashOnlyNotice != null) TxtSlashOnlyNotice.Visibility = Visibility.Collapsed;
        }
    }

    // --- 최신 정보 동기화 & 다시 로그온 ---
    private async void BtnSyncAll_Click(object sender, RoutedEventArgs e)
    {
        LblStatusBar.Text = "구글 시트에서 최신 템플릿 동기화 진행 중...";

        await DataSourceResolver.RefreshRemoteProfilesAsync();
        var (tSuccess, tMsg) = await _templateManager.SyncFromRemoteAsync();

        _templatesObs = new ObservableCollection<TemplateItem>(_templateManager.Templates);
        DgTemplates.ItemsSource = _templatesObs;
        LoadProfiles();
        DataSourceResolver.RaiseChanged();

        MessageBox.Show(tMsg, "동기화 결과", MessageBoxButton.OK,
            tSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        LblStatusBar.Text = tSuccess ? "최신 정보 동기화 완료." : "동기화 실패.";
    }

    private void BtnRelogin_Click(object sender, RoutedEventArgs e)
    {
        var loginWin = new LoginWindow(_authService, _configManager);
        if (loginWin.ShowDialog() == true)
        {
            UpdateUserInfoDisplay();
            LblStatusBar.Text = $"사용자 '{_authService.CurrentUser?.UserId}' 로그인 완료.";
        }
    }

    // --- 확인 및 저장 ---
    private void BtnSaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _configManager.Config;
        cfg.GlobalHotkey = TxtGlobalHotkey.Text.Trim();
        cfg.TriggerKey = string.IsNullOrEmpty(TxtTriggerKey.Text) ? "\\" : TxtTriggerKey.Text;
        cfg.EnableNumberSelection = ChkNumbers.IsChecked == true;

        if (RadioSlashOnly.IsChecked == true)
        {
            cfg.CompleteKeyMode = "SlashOnly";
            cfg.DisplayMode = "InlinePill"; // SlashOnly는 1개 단일 칩으로 고정
        }
        else if (RadioShiftSpace.IsChecked == true)
        {
            cfg.CompleteKeyMode = "ShiftSpace";
            if (RadioModeHorizontal.IsChecked == true)
                cfg.DisplayMode = "HorizontalBar";
            else if (RadioModeManual.IsChecked == true)
                cfg.DisplayMode = "ManualOnly";
            else
                cfg.DisplayMode = "InlinePill";
        }
        else
        {
            cfg.CompleteKeyMode = "SlashTab";
            if (RadioModeHorizontal.IsChecked == true)
                cfg.DisplayMode = "HorizontalBar";
            else if (RadioModeManual.IsChecked == true)
                cfg.DisplayMode = "ManualOnly";
            else
                cfg.DisplayMode = "InlinePill";
        }

        // 멀티 알약 최대 개수 저장 (3~10)
        if (CmbMaxCandidates != null && CmbMaxCandidates.SelectedIndex >= 0)
        {
            cfg.MaxCandidatesCount = CmbMaxCandidates.SelectedIndex + AppConstants.MinCandidatesCount;
        }

        cfg.Theme = ThemeManager.ThemeFluentLight;
        cfg.AccentColor = GetSelectedAccentColor();
        ThemeManager.ApplyTheme(cfg.Theme, cfg.AccentColor);

        if (RadioPillScale1.IsChecked == true)
            cfg.PillScale = AppConstants.PillScaleNormal;
        else if (RadioPillScale3.IsChecked == true)
            cfg.PillScale = AppConstants.PillScaleExtraLarge;
        else
            cfg.PillScale = AppConstants.PillScaleLarge;

        // 알약 위치 및 간격 저장
        if (RadioPosFixed.IsChecked == true)
        {
            cfg.PillPlacement = "ScreenBottomRight";
        }
        else if (RadioPosMouse.IsChecked == true)
        {
            cfg.PillPlacement = "MousePointer";
        }
        else
        {
            cfg.PillPlacement = "CursorNearby";
            if (RadioPosFar.IsChecked == true) cfg.PillOffsetY = AppConstants.PillOffsetFar;
            else if (RadioPosNearby1.IsChecked == true) cfg.PillOffsetY = AppConstants.PillOffsetTight;
            else cfg.PillOffsetY = AppConstants.PillOffsetNormal;
        }

        // 창 투명도 (흐리게) 저장
        if (RadioOpacity70.IsChecked == true) cfg.WindowOpacity = 0.70;
        else if (RadioOpacity80.IsChecked == true) cfg.WindowOpacity = 0.80;
        else if (RadioOpacity90.IsChecked == true) cfg.WindowOpacity = 0.90;
        else cfg.WindowOpacity = 1.0;

        // 상태창 투명도 저장
        if (RadioStatusOpacity45.IsChecked == true) cfg.StatusWindowOpacity = 0.45;
        else if (RadioStatusOpacity65.IsChecked == true) cfg.StatusWindowOpacity = 0.65;
        else if (RadioStatusOpacity85.IsChecked == true) cfg.StatusWindowOpacity = 0.85;
        else cfg.StatusWindowOpacity = 1.0;

        cfg.ShowStatusWindow = ChkShowStatusWindow.IsChecked == true;
        cfg.EnableMathCalc = ChkEnableMathCalc.IsChecked == true;
        cfg.EnableKoToEnTranslation = ChkEnableTranslation.IsChecked == true;
        if (CmbAutoHideSeconds.SelectedItem is System.Windows.Controls.ComboBoxItem hideItem &&
            int.TryParse(hideItem.Tag?.ToString(), out int hideSec))
        {
            cfg.AutoHideSeconds = Math.Clamp(hideSec, AppConstants.MinAutoHideSeconds, AppConstants.MaxAutoHideSeconds);
        }

        // 자동 동기화 주기 저장
        if (CmbAutoSyncInterval.SelectedItem is ComboBoxItem selectedSync &&
            int.TryParse(selectedSync.Tag?.ToString(), out int minutes))
        {
            cfg.AutoSyncIntervalMinutes = minutes;
        }

        cfg.QuickSaveWithoutDialog = ChkQuickSaveWithoutDialog.IsChecked == true;
        if (ChkLogoutOnExit != null)
        {
            cfg.LogoutOnExit = ChkLogoutOnExit.IsChecked == true;
        }

        _configManager.Save();
        SettingsChanged?.Invoke();
        Close();
    }

    private string GetSelectedAccentColor()
    {
        if (RadioAccentGreen?.IsChecked == true) return "Green";
        if (RadioAccentLavender?.IsChecked == true) return "Lavender";
        if (RadioAccentPeach?.IsChecked == true) return "Peach";
        if (RadioAccentPink?.IsChecked == true) return "Pink";
        return "Blue";
    }

    private void RadioAccent_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        ThemeManager.ApplyTheme(ThemeManager.ThemeFluentLight, GetSelectedAccentColor());
        UpdateDesignPreview();
    }

    private void DesignOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateSlashOnlyLock();
        UpdateDesignPreview();
    }

    private void UpdateDesignPreview()
    {
        if (PreviewPillBorder == null || PreviewPillItemsPanel == null || PreviewDockBorder == null) return;

        var (accentHex, _, softHex) = ThemeManager.GetAccentPalette(GetSelectedAccentColor());
        var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex));
        var softBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(softHex));

        double pillAlpha = RadioOpacity70?.IsChecked == true ? 0.70
            : RadioOpacity80?.IsChecked == true ? 0.80
            : RadioOpacity90?.IsChecked == true ? 0.90 : 1.0;

        double statusAlpha = RadioStatusOpacity45?.IsChecked == true ? 0.45
            : RadioStatusOpacity65?.IsChecked == true ? 0.65
            : RadioStatusOpacity85?.IsChecked == true ? 0.85 : 1.0;

        PreviewPillBorder.Opacity = pillAlpha;
        PreviewPillBorder.BorderBrush = accentBrush;
        PreviewDockBorder.Opacity = statusAlpha;

        // 실제 메인 마스터창에도 투명도 실시간 미리보기 반영
        if (Application.Current?.MainWindow != null)
        {
            Win32Api.SetWindowAlpha(Application.Current.MainWindow, statusAlpha);
        }

        int scale = RadioPillScale1?.IsChecked == true ? 1 : (RadioPillScale3?.IsChecked == true ? 3 : 2);
        double chipFont = scale switch { 1 => 11, 3 => 15.5, _ => 13 };
        Thickness chipPad = scale switch { 1 => new Thickness(6, 2, 6, 2), 3 => new Thickness(12, 6, 12, 6), _ => new Thickness(9, 4, 9, 4) };

        PreviewPillItemsPanel.Children.Clear();
        bool isManual = RadioModeManual?.IsChecked == true;
        bool isMulti = RadioModeHorizontal?.IsChecked == true;
        bool useNum = ChkNumbers?.IsChecked == true;

        if (isManual)
        {
            PreviewPillItemsPanel.Children.Add(new TextBlock
            {
                Text = "수동 검색 전용 모드 (타이핑 중 자동 알약 미표시 · Alt+Q 사용)",
                FontSize = 11.5,
                Foreground = Brushes.DimGray
            });
        }
        else if (!isMulti)
        {
            var badge = new Border { Background = accentBrush, CornerRadius = new CornerRadius(5), Padding = chipPad, Margin = new Thickness(0, 0, 6, 0) };
            badge.Child = new TextBlock { Text = "[Shift+Space] 배송안내 — 오늘 출고됩니다.", Foreground = Brushes.White, FontSize = chipFont, FontWeight = FontWeights.SemiBold };
            PreviewPillItemsPanel.Children.Add(badge);
        }
        else
        {
            int count = (CmbMaxCandidates?.SelectedIndex >= 0) ? Math.Min(4, CmbMaxCandidates.SelectedIndex + 3) : 3;
            string[] samples = { "배송안내", "출고일정", "송장조회", "배송지변경" };
            for (int i = 0; i < count; i++)
            {
                string keyHint = i == 0 ? "Shift+Space" : (useNum ? $"\\+{i + 1}" : $"후보{i + 1}");
                var chip = new Border
                {
                    Background = i == 0 ? accentBrush : softBrush,
                    BorderBrush = accentBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = chipPad,
                    Margin = new Thickness(0, 0, 6, 4)
                };
                chip.Child = new TextBlock
                {
                    Text = $"[{keyHint}] {samples[i % samples.Length]}",
                    Foreground = i == 0 ? Brushes.White : Brushes.Black,
                    FontSize = chipFont - 1,
                    FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal
                };
                PreviewPillItemsPanel.Children.Add(chip);
            }
        }

        string posDesc = RadioPosFixed?.IsChecked == true ? "모니터 우측 하단 고정"
            : RadioPosMouse?.IsChecked == true ? "마우스 포인터 옆 표시"
            : RadioPosFar?.IsChecked == true ? "채팅 커서 기준 원거리 (+65px)"
            : RadioPosNearby1?.IsChecked == true ? "채팅 커서 밀착 (+15px)"
            : "채팅 커서 기준 (+35px 권장)";

        if (PreviewLocationText != null)
            PreviewLocationText.Text = $"표시 위치 ➔ [{posDesc}]";

        if (PreviewSummaryText != null)
            PreviewSummaryText.Text = $"현재 미리보기: 알약 투명도 {(int)(pillAlpha * 100)}% · 마스터창 투명도 {(int)(statusAlpha * 100)}% · 크기 {scale}단계 · {posDesc}";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
