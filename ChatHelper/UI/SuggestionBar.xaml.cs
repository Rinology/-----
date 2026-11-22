using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ChatHelper.Constants;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class SuggestionBar : Window
{
    private readonly ConfigManager _configManager;
    private readonly System.Windows.Threading.DispatcherTimer _countdownTimer;
    private int _remainingSeconds;
    private List<TemplateItem> _candidates = new();
    private int _matchedLen = 0;
    private IntPtr _targetHwnd = IntPtr.Zero;
    private string _recognizedToken = string.Empty;

    public event Action<TemplateItem, int, IntPtr>? ItemSelected;
    public event Action<string>? QuickRegisterRequested;

    public void SetRecognizedToken(string token)
    {
        _recognizedToken = token ?? string.Empty;
    }

    public void ShowStatusNotice(string recognizedToken, string statusMode)
    {
        if (_configManager.Config.DisplayMode == "ManualOnly" || !_configManager.Config.IsEnabled)
        {
            Hide();
            return;
        }

        bool useMouse = _configManager.Config.PillPlacement == "MousePointer";
        if (!Win32Api.TryGetValidInputPosition(out double caretX, out double caretY, out _, useMouse))
        {
            Hide();
            return;
        }

        _recognizedToken = recognizedToken ?? string.Empty;
        ItemsPanel.Children.Clear();
        ItemsPanel.Visibility = Visibility.Collapsed;
        TopAiRowPanel.Children.Clear();
        TopAiRowPanel.Visibility = Visibility.Collapsed;
        BottomAiRowPanel.Children.Clear();
        BottomAiRowPanel.Visibility = Visibility.Collapsed;

        ApplyRecognitionBanner(statusMode, _recognizedToken, _recognizedToken.Length);
        RecognitionHeaderBorder.Margin = new Thickness(0);
        RecognitionHeaderBorder.BorderThickness = new Thickness(0);

        UpdateLayout();
        StartCountdown();
        Reposition(caretX, caretY);
        Show();
    }

    private void ApplyRecognitionBanner(string statusMode, string token, int matchedLen)
    {
        RecognitionHeaderBorder.Visibility = Visibility.Visible;
        RecognitionHeaderBorder.Margin = new Thickness(0, 0, 0, 4);
        RecognitionHeaderBorder.BorderThickness = new Thickness(0, 0, 0, 1);

        if (statusMode == "UNKNOWN")
        {
            RecognitionHeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            RecognitionHeaderBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECACA"));
            TxtRecognitionHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
            TxtRecognitionHeader.Text = $"[미일치] \"{token}\"";
        }
        else if (statusMode == "WAITING")
        {
            RecognitionHeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
            RecognitionHeaderBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
            TxtRecognitionHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
            TxtRecognitionHeader.Text = string.IsNullOrWhiteSpace(token)
                ? "[대기중] 입력 대기..."
                : $"[인식중] \"{token}\"";
        }
        else
        {
            RecognitionHeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            RecognitionHeaderBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
            TxtRecognitionHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
            string displayToken = string.IsNullOrWhiteSpace(token) ? $"{matchedLen}자" : $"\"{token}\" ({matchedLen}자)";
            TxtRecognitionHeader.Text = $"인식: {displayToken}";
        }

        if (BtnQuickRegisterToken != null)
        {
            string clean = (token ?? string.Empty).Trim().TrimStart('\\', '/');
            BtnQuickRegisterToken.Visibility = string.IsNullOrWhiteSpace(clean) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void BtnQuickRegisterToken_Click(object sender, RoutedEventArgs e)
    {
        string clean = (_recognizedToken ?? string.Empty).Trim().TrimStart('\\', '/');
        Hide();
        QuickRegisterRequested?.Invoke(clean);
    }

    public SuggestionBar(ConfigManager configManager)
    {
        InitializeComponent();
        _configManager = configManager;
        _countdownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (s, e) =>
        {
            if (_remainingSeconds > 0) _remainingSeconds--;
            UpdateCountdownText();
            if (_remainingSeconds <= 0) _countdownTimer.Stop();
        };
    }

    private void StartCountdown()
    {
        _remainingSeconds = Math.Clamp(_configManager.Config.AutoHideSeconds, AppConstants.MinAutoHideSeconds, AppConstants.MaxAutoHideSeconds);
        UpdateCountdownText();
        _countdownTimer.Stop();
        _countdownTimer.Start();
    }

    private void UpdateCountdownText()
    {
        if (TxtAutoHideCountdown != null)
            TxtAutoHideCountdown.Text = $"⏱ {_remainingSeconds}s";
    }

    public new void Hide()
    {
        _countdownTimer.Stop();
        base.Hide();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        int exStyle = Win32Api.GetWindowLong(helper.Handle, Win32Api.GWL_EXSTYLE);
        Win32Api.SetWindowLong(helper.Handle, Win32Api.GWL_EXSTYLE,
            exStyle | Win32Api.WS_EX_NOACTIVATE | Win32Api.WS_EX_TOOLWINDOW | Win32Api.WS_EX_TOPMOST);
    }

    public void ShowCandidates(List<TemplateItem> candidates, int matchedLen, IntPtr targetHwnd)
    {
        var mode = _configManager.Config.DisplayMode;
        if (candidates == null || candidates.Count == 0 || mode == "ManualOnly" || !_configManager.Config.IsEnabled)
        {
            Hide();
            return;
        }

        // 유효한 텍스트 입력 창인지 확인 (바탕화면, 자기 자신 창 등 제외)
        bool useMouse = _configManager.Config.PillPlacement == "MousePointer";
        if (!Win32Api.TryGetValidInputPosition(out double caretX, out double caretY, out _, useMouse))
        {
            Hide();
            return;
        }

        _candidates = candidates;
        _matchedLen = matchedLen;
        _targetHwnd = targetHwnd;

        ApplyRecognitionBanner("MATCH", _recognizedToken, matchedLen);

        var screenWidthForWrap = SystemParameters.PrimaryScreenWidth;
        ItemsPanel.MaxWidth = Math.Min(780, Math.Max(420, screenWidthForWrap * 0.56));
        ItemsPanel.Children.Clear();
        TopAiRowPanel.Children.Clear();
        BottomAiRowPanel.Children.Clear();
        TopAiRowPanel.Visibility = Visibility.Collapsed;
        BottomAiRowPanel.Visibility = Visibility.Collapsed;
        ItemsPanel.Visibility = Visibility.Visible;

        var trigger = _configManager.Config.TriggerKey ?? "\\";
        var completeMode = _configManager.Config.CompleteKeyMode ?? "SlashTab";
        string tabKeyHint = completeMode switch
        {
            "SlashOnly" => trigger,
            "ShiftSpace" => "Shift+Space",
            _ => $"{trigger}+Tab"
        };

        int scale = _configManager.Config.PillScale;
        if (scale < 1) scale = 2;

        double badgeFontSize = scale switch { 1 => 11, 3 => 16, _ => 13 };
        double contentFontSize = scale switch { 1 => 12, 3 => 19, _ => 16 };
        Thickness badgePadding = scale switch { 1 => new Thickness(6, 2, 6, 2), 3 => new Thickness(12, 5, 12, 5), _ => new Thickness(9, 3, 9, 3) };
        Thickness mainPadding = scale switch { 1 => new Thickness(8, 4, 8, 4), 3 => new Thickness(18, 10, 18, 10), _ => new Thickness(14, 7, 14, 7) };
        int maxPreviewLen = scale switch { 1 => 35, 3 => 85, _ => 65 };

        MainBorder.Padding = mainPadding;

        var sheetItems = candidates.Where(c => c.Category != "AI생성" && c.Category != "AI번역").ToList();

        if (completeMode == "SlashOnly" || mode == "InlinePill")
        {
            MainBorder.CornerRadius = new CornerRadius(10);
            MainBorder.Background = (Brush)(TryFindResource("ThemePillBg") ?? Brushes.White);
            MainBorder.BorderBrush = (Brush)(TryFindResource("ThemePillBorder") ?? Brushes.LightGray);

            if (sheetItems.Count > 0)
            {
                var topItem = sheetItems[0];

                var badge = new Border
                {
                    Background = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"))),
                    CornerRadius = new CornerRadius(5),
                    Padding = badgePadding,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                badge.Child = new TextBlock
                {
                    Text = tabKeyHint,
                    Foreground = Brushes.White,
                    FontSize = badgeFontSize,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Malgun Gothic, Segoe UI")
                };
                ItemsPanel.Children.Add(badge);

                var preview = topItem.Content.Trim().Replace("\r\n", " ").Replace("\n", " ");
                if (preview.Length > maxPreviewLen) preview = preview[..maxPreviewLen] + "...";

                var btn = new Button
                {
                    Content = $"{topItem.Title} — {preview}",
                    Foreground = (Brush)(TryFindResource("ThemeText") ?? Brushes.Black),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontSize = contentFontSize,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
                    Focusable = false
                };
                btn.Click += (s, e) => SelectItem(topItem);
                ItemsPanel.Children.Add(btn);
            }
            else
            {
                ItemsPanel.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            MainBorder.CornerRadius = new CornerRadius(10);
            MainBorder.Background = (Brush)(TryFindResource("ThemePillBg") ?? Brushes.White);
            MainBorder.BorderBrush = (Brush)(TryFindResource("ThemePillBorder") ?? Brushes.LightGray);

            int maxAllowed = Math.Clamp(_configManager.Config.MaxCandidatesCount, 3, 10);
            var visibleSheetItems = sheetItems.Take(maxAllowed).ToList();

            if (visibleSheetItems.Count == 0)
            {
                ItemsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                var accentBrush = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")));
                var kbdBg = (Brush)(TryFindResource("ThemeKbdBg") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")));
                var borderBrush = (Brush)(TryFindResource("ThemeBorder") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")));

                for (int i = 0; i < visibleSheetItems.Count; i++)
                {
                    var item = visibleSheetItems[i];
                    string keyHint = (i == 0) ? tabKeyHint : $"{trigger}+{i + 1}";

                    var chip = new Button
                    {
                        Content = $"[{keyHint}] {item.Title}",
                        Foreground = (i > 0) ? (Brush)(TryFindResource("ThemeText") ?? Brushes.Black) : Brushes.White,
                        Background = (i == 0) ? accentBrush : kbdBg,
                        BorderBrush = (i == 0) ? accentBrush : borderBrush,
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 6, 5),
                        Padding = badgePadding,
                        Cursor = Cursors.Hand,
                        FontSize = contentFontSize - 2,
                        FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
                        FontWeight = (i == 0) ? FontWeights.SemiBold : FontWeights.Normal,
                        Focusable = false
                    };
                    chip.Click += (s, e) => SelectItem(item);
                    ItemsPanel.Children.Add(chip);
                }
            }
        }

        UpdateLayout();
        StartCountdown();
        Reposition(caretX, caretY);
        Show();
    }

    public void ShowAiGeneratingPlaceholder(string promptHint = "")
    {
        // Ollama 제거됨
    }

    public void UpdateAiCandidate(TemplateItem aiItem)
    {
        // Ollama 제거됨
    }

    private void Reposition(double caretX, double caretY)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        double w = ActualWidth > 0 ? ActualWidth : 320;
        double h = ActualHeight > 0 ? ActualHeight : 42;

        Opacity = Math.Clamp(_configManager.Config.WindowOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);

        if (_configManager.Config.PillPlacement == "ScreenBottomRight")
        {
            Left = screenWidth - w - 24;
            Top = screenHeight - h - 64;
        }
        else
        {
            int offsetY = _configManager.Config.PillOffsetY;
            if (offsetY <= 0) offsetY = AppConstants.PillOffsetNormal;

            Left = Math.Max(10, Math.Min(caretX, screenWidth - w - 10));
            if (caretY + offsetY + h > screenHeight - 10)
            {
                Top = Math.Max(10, caretY - h - 10);
            }
            else
            {
                Top = caretY + offsetY;
            }
        }

        Show();
    }

    private void SelectItem(TemplateItem item)
    {
        Hide();
        ItemSelected?.Invoke(item, _matchedLen, _targetHwnd);
    }
}
