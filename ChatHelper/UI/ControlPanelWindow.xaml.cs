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

        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }

        LoadData();
        _isInitializing = false;
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
        else if (cfg.CompleteKeyMode == "ShiftSpace")
            RadioShiftSpace.IsChecked = true;
        else
            RadioSlashTab.IsChecked = true;

        // 3. 표시 및 알약 크기 설정 로드
        if (cfg.PillScale == 1)
            RadioPillScale1.IsChecked = true;
        else if (cfg.PillScale == 3)
            RadioPillScale3.IsChecked = true;
        else
            RadioPillScale2.IsChecked = true;

        // 알약 위치 및 간격
        if (cfg.PillPlacement == "ScreenBottomRight")
            RadioPosFixed.IsChecked = true;
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
        UpdateUserInfoDisplay();

        // 5. 로컬 AI 설정 로드
        ChkEnableLocalAi.IsChecked = cfg.EnableLocalAi;
        if (cfg.AiPlacement == "Top")
            RadioAiPosTop.IsChecked = true;
        else
            RadioAiPosBottom.IsChecked = true;

        ChkAutoGenerateAltX.IsChecked = cfg.AutoGenerateOnAltX;
        CheckOllamaStatusAsync();

        // 6. 하단 상태표시줄
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

    // --- 템플릿 CRUD ---
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

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtKw.Text) && string.IsNullOrWhiteSpace(TxtTitle.Text))
        {
            MessageBox.Show("단축어나 제목을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = new TemplateItem
        {
            Keyword = TxtKw.Text.Trim(),
            Title = TxtTitle.Text.Trim(),
            Category = string.IsNullOrWhiteSpace(TxtCat.Text) ? "일반" : TxtCat.Text.Trim(),
            Content = TxtContent.Text
        };

        _templateManager.AddTemplate(item);
        _templatesObs.Add(item);
        BtnClearForm_Click(sender, e);
        LblStatusBar.Text = $"신규 커맨드 '{item.Title}' 추가 완료!";
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        int idx = DgTemplates.SelectedIndex;
        if (idx < 0 || idx >= _templatesObs.Count)
        {
            MessageBox.Show("수정할 항목을 목록에서 먼저 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = _templatesObs[idx];
        item.Keyword = TxtKw.Text.Trim();
        item.Title = TxtTitle.Text.Trim();
        item.Category = string.IsNullOrWhiteSpace(TxtCat.Text) ? "일반" : TxtCat.Text.Trim();
        item.Content = TxtContent.Text;

        _templateManager.UpdateTemplate(idx, item);
        DgTemplates.Items.Refresh();
        LblStatusBar.Text = $"'{item.Title}' 수정 적용 완료!";
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        int idx = DgTemplates.SelectedIndex;
        if (idx < 0 || idx >= _templatesObs.Count)
        {
            MessageBox.Show("삭제할 항목을 먼저 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = _templatesObs[idx];
        if (MessageBox.Show($"'{item.Title}' 항목을 정말 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _templateManager.DeleteTemplate(idx);
            _templatesObs.RemoveAt(idx);
            BtnClearForm_Click(sender, e);
            LblStatusBar.Text = $"'{item.Title}' 삭제 완료.";
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
            if (TxtSlashOnlyNotice != null) TxtSlashOnlyNotice.Visibility = Visibility.Visible;
        }
        else
        {
            RadioModeHorizontal.IsEnabled = true;
            if (CmbMaxCandidates != null) CmbMaxCandidates.IsEnabled = true;
            if (TxtSlashOnlyNotice != null) TxtSlashOnlyNotice.Visibility = Visibility.Collapsed;
        }
    }

    // --- 최신 정보 동기화 & 다시 로그온 ---
    private async void BtnSyncAll_Click(object sender, RoutedEventArgs e)
    {
        LblStatusBar.Text = "구글 시트에서 최신 정보 동기화 진행 중...";

        var tTask = _templateManager.SyncFromRemoteAsync();
        var uTask = _authService.SyncUserListFromRemoteAsync();

        await Task.WhenAll(tTask, uTask);

        var (tSuccess, tMsg) = tTask.Result;
        var (uSuccess, uMsg) = uTask.Result;

        _templatesObs = new ObservableCollection<TemplateItem>(_templateManager.Templates);
        DgTemplates.ItemsSource = _templatesObs;
        UpdateUserInfoDisplay();

        MessageBox.Show($"{tMsg}\n{uMsg}", "동기화 결과", MessageBoxButton.OK, MessageBoxImage.Information);
        LblStatusBar.Text = "최신 정보 동기화 완료.";
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

        // 로컬 AI 설정 저장
        cfg.EnableLocalAi = ChkEnableLocalAi.IsChecked == true;
        cfg.AiPlacement = (RadioAiPosTop.IsChecked == true) ? "Top" : "Bottom";
        cfg.AutoGenerateOnAltX = ChkAutoGenerateAltX.IsChecked == true;

        _configManager.Save();
        SettingsChanged?.Invoke();
        Close();
    }

    private async void BtnTestOllama_Click(object sender, RoutedEventArgs e)
    {
        TxtOllamaStatus.Text = "Ollama 서버 연결 확인 중...";
        TxtOllamaStatus.Foreground = Brushes.Navy;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool isOk = await OllamaService.Instance.IsServerRespondingAsync();
        sw.Stop();

        if (isOk)
        {
            TxtOllamaStatus.Text = $"✔ 정상 연결됨 (응답 시간: {sw.ElapsedMilliseconds}ms)";
            TxtOllamaStatus.Foreground = Brushes.DarkGreen;
        }
        else
        {
            TxtOllamaStatus.Text = "❌ 연결 실패 (Ollama가 실행 중인지 확인하세요)";
            TxtOllamaStatus.Foreground = Brushes.Red;
        }
    }

    private async void CheckOllamaStatusAsync()
    {
        try
        {
            bool isOk = await OllamaService.Instance.IsServerRespondingAsync();
            Dispatcher.Invoke(() =>
            {
                if (isOk)
                {
                    TxtOllamaStatus.Text = "✔ Ollama 실행 중 (정상 연결)";
                    TxtOllamaStatus.Foreground = Brushes.DarkGreen;
                }
                else
                {
                    TxtOllamaStatus.Text = "⚠️ 미실행 (Ollama 실행 필요)";
                    TxtOllamaStatus.Foreground = Brushes.DarkRed;
                }
            });
        }
        catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
