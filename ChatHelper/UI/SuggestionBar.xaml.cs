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
    private bool _isSelectionTranslationMode = false;

    public event Action<TemplateItem, int, IntPtr>? ItemSelected;
    public event Action<TemplateItem, IntPtr>? SelectionReplaceRequested;
    public event Action<string>? QuickRegisterRequested;
    public event Action? AutoHideExpired;

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
        _candidates.Clear();
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

    private void BtnCloseSuggestion_Click(object sender, RoutedEventArgs e)
    {
        DismissSuggestion();
    }

    public void DismissSuggestion()
    {
        _isSelectionTranslationMode = false;
        Hide();
        AutoHideExpired?.Invoke();
    }

    private Button CreateCloseIconButton(string tooltip = "창 닫기")
    {
        var btn = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 20,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip
        };
        btn.Click += (s, e) => DismissSuggestion();
        return btn;
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
            if (_remainingSeconds <= 0)
            {
                _countdownTimer.Stop();
                if (!_isSelectionTranslationMode)
                {
                    Hide();
                    AutoHideExpired?.Invoke();
                }
            }
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
        double winAlpha = Math.Clamp(_configManager.Config.WindowOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);
        Win32Api.SetWindowAlpha(this, winAlpha);
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

        _isSelectionTranslationMode = false;
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
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.Background = (Brush)(TryFindResource("ThemePillBg") ?? Brushes.White);
            MainBorder.BorderBrush = (Brush)(TryFindResource("ThemePillBorder") ?? Brushes.LightGray);

            if (sheetItems.Count > 0)
            {
                var topItem = sheetItems[0];

                var badge = new Border
                {
                    Background = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"))),
                    BorderBrush = (Brush)(TryFindResource("ThemeAccentBorder") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD"))),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = badgePadding,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                badge.Child = new TextBlock
                {
                    Text = tabKeyHint,
                    Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
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
                    Style = TryFindResource("ThemeDockButtonStyle") as Style,
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
            MainBorder.CornerRadius = new CornerRadius(0);
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
                var accentBrush = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")));
                var accentBorder = (Brush)(TryFindResource("ThemeAccentBorder") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD")));
                var accentText = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A")));
                var kbdBg = (Brush)(TryFindResource("ThemeKbdBg") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")));
                var borderBrush = (Brush)(TryFindResource("ThemeBorder") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")));
                var btnStyle = TryFindResource("ThemeButtonStyle") as Style;

                for (int i = 0; i < visibleSheetItems.Count; i++)
                {
                    var item = visibleSheetItems[i];
                    string keyHint = (i == 0) ? tabKeyHint : $"{trigger}+{i + 1}";

                    var chip = new Button
                    {
                        Content = $"[{keyHint}] {item.Title}",
                        Style = btnStyle,
                        Foreground = (i == 0) ? accentText : (Brush)(TryFindResource("ThemeText") ?? Brushes.Black),
                        Background = (i == 0) ? accentBrush : kbdBg,
                        BorderBrush = (i == 0) ? accentBorder : borderBrush,
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

    public void ShowAiGeneratingPlaceholder(string promptHint = "", IntPtr targetHwnd = default, bool isSelectionMode = false)
    {
        if (!_configManager.Config.EnableKoToEnTranslation || !_configManager.Config.IsEnabled) return;

        _isSelectionTranslationMode = isSelectionMode;
        if (targetHwnd != IntPtr.Zero) _targetHwnd = targetHwnd;

        if (isSelectionMode)
        {
            _candidates.Clear();
            _matchedLen = 0;
            ItemsPanel.Children.Clear();
            ItemsPanel.Visibility = Visibility.Collapsed;
            if (RecognitionHeaderBorder != null)
                RecognitionHeaderBorder.Visibility = Visibility.Collapsed;
        }

        BottomAiRowPanel.Children.Clear();
        BottomAiRowPanel.Visibility = Visibility.Visible;
        BottomAiRowPanel.Margin = isSelectionMode ? new Thickness(0) : new Thickness(0, 6, 0, 0);

        var loadingStack = new StackPanel { MinWidth = 300, MaxWidth = 480 };
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var loadingTitle = new TextBlock
        {
            Text = "🌐 Google 영문 번역 중...",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(loadingTitle, 0);
        headerGrid.Children.Add(loadingTitle);

        var closeLoadingBtn = CreateCloseIconButton("번역 창 닫기");
        Grid.SetColumn(closeLoadingBtn, 1);
        headerGrid.Children.Add(closeLoadingBtn);

        loadingStack.Children.Add(headerGrid);

        if (!string.IsNullOrWhiteSpace(promptHint))
        {
            string preview = promptHint.Trim().Replace("\r\n", " ").Replace("\n", " ");
            if (preview.Length > 60) preview = preview[..60] + "...";
            loadingStack.Children.Add(new TextBlock
            {
                Text = $"선택 원문: \"{preview}\"",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        loadingStack.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
            BorderThickness = new Thickness(0)
        });

        BottomAiRowPanel.Children.Add(loadingStack);

        bool useMouse = _configManager.Config.PillPlacement == "MousePointer";
        if (Win32Api.TryGetValidInputPosition(out double cx, out double cy, out _, useMouse))
        {
            UpdateLayout();
            Reposition(cx, cy);
        }
        Show();
    }

    public void UpdateAiCandidate(TemplateItem aiItem, int matchedLen = 0, IntPtr targetHwnd = default)
    {
        if (aiItem == null || aiItem.Category != "AI번역") return;

        if (_matchedLen <= 0 && matchedLen > 0) _matchedLen = matchedLen;
        if (_targetHwnd == IntPtr.Zero && targetHwnd != IntPtr.Zero) _targetHwnd = targetHwnd;

        int existingIdx = _candidates.FindIndex(c => c.Category == "AI번역");
        if (existingIdx >= 0) _candidates[existingIdx] = aiItem;
        else _candidates.Add(aiItem);

        RenderTranslationRow(aiItem);
        UpdateLayout();
        bool useMouse = _configManager.Config.PillPlacement == "MousePointer";
        if (Win32Api.TryGetValidInputPosition(out double cx, out double cy, out _, useMouse))
        {
            StartCountdown();
            Reposition(cx, cy);
        }
        Show();
    }

    private void RenderTranslationRow(TemplateItem item)
    {
        BottomAiRowPanel.Children.Clear();
        BottomAiRowPanel.Visibility = Visibility.Visible;
        BottomAiRowPanel.Margin = new Thickness(0, 6, 0, 0);

        var trigger = _configManager.Config.TriggerKey ?? "\\";
        var sheetItems = _candidates.Where(c => c.Category != "AI번역").ToList();
        string keyHint = sheetItems.Count == 0 ? $"{trigger}+Tab" : $"{trigger}+E";

        // 구글 번역기 스타일 2열 비교 카드 (원문 ↔ 영문) + 상단 헤더(우측 상단 X 닫기 포함) + 하단 액션 바
        var outer = new StackPanel { MinWidth = 420, MaxWidth = 640 };

        if (_isSelectionTranslationMode || RecognitionHeaderBorder.Visibility != Visibility.Visible)
        {
            var transHeader = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var transHeaderGrid = new Grid();
            transHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            transHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var transTitle = new TextBlock
            {
                Text = "🌐 Google 영문 번역 결과",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(transTitle, 0);
            transHeaderGrid.Children.Add(transTitle);

            var closeTransBtn = CreateCloseIconButton("번역 창 닫기");
            Grid.SetColumn(closeTransBtn, 1);
            transHeaderGrid.Children.Add(closeTransBtn);

            transHeader.Child = transHeaderGrid;
            outer.Children.Add(transHeader);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 좌측: 한국어 원문 카드
        var srcBox = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 6, 9, 6)
        };
        var srcStack = new StackPanel();
        srcStack.Children.Add(new TextBlock
        {
            Text = "한국어 원문",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
            Margin = new Thickness(0, 0, 0, 3)
        });
        srcStack.Children.Add(new TextBlock
        {
            Text = item.Keyword,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 64
        });
        srcBox.Child = srcStack;
        Grid.SetColumn(srcBox, 0);
        grid.Children.Add(srcBox);

        // 우측: Google 영문 번역 카드
        var dstBox = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 6, 9, 6)
        };
        var dstStack = new StackPanel();
        dstStack.Children.Add(new TextBlock
        {
            Text = "Google 영문 번역",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")),
            Margin = new Thickness(0, 0, 0, 3)
        });
        dstStack.Children.Add(new TextBlock
        {
            Text = item.Content,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 64
        });
        dstBox.Child = dstStack;
        Grid.SetColumn(dstBox, 2);
        grid.Children.Add(dstBox);

        outer.Children.Add(grid);

        // 하단 액션 버튼 바
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 0) };

        var btnEnOnly = new Button
        {
            Content = _isSelectionTranslationMode ? "선택 영역을 영문으로 바꾸기" : $"[{keyHint}] 영문만 입력",
            Style = TryFindResource("ThemeButtonStyle") as Style,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 5, 0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Focusable = false
        };
        btnEnOnly.Click += (s, e) =>
        {
            if (_isSelectionTranslationMode)
            {
                Hide();
                SelectionReplaceRequested?.Invoke(item, _targetHwnd);
            }
            else
            {
                SelectItem(item);
            }
        };
        actions.Children.Add(btnEnOnly);

        var btnBoth = new Button
        {
            Content = "원문+영문 함께 입력",
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 5, 0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Focusable = false
        };
        btnBoth.Click += (s, e) =>
        {
            var bothItem = new TemplateItem
            {
                Keyword = item.Keyword,
                Title = item.Title,
                Content = $"{item.Keyword}\n{item.Content}",
                Category = item.Category
            };
            if (_isSelectionTranslationMode)
            {
                Hide();
                SelectionReplaceRequested?.Invoke(bothItem, _targetHwnd);
            }
            else
            {
                SelectItem(bothItem);
            }
        };
        actions.Children.Add(btnBoth);

        var btnCopy = new Button
        {
            Content = "복사",
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Focusable = false
        };
        btnCopy.Click += (s, e) =>
        {
            try { Clipboard.SetText(item.Content); } catch { }
            Hide();
        };
        actions.Children.Add(btnCopy);

        outer.Children.Add(actions);
        BottomAiRowPanel.Children.Add(outer);
    }

    /// <summary>
    /// 커서/알약 크기가 작업 영역을 벗어나는지 계산하고, 필요 시 마우스 커서 폴백 및 경계 보정 여부(notice)를 반환합니다.
    /// </summary>
    public static (double Left, double Top, string? PositionNotice) ComputeAdjustedPosition(
        double dipX, double dipY,
        double mouseDipX, double mouseDipY,
        double w, double h,
        double workLeft, double workTop, double workRight, double workBottom,
        string placement, int offsetY)
    {
        if (placement == "ScreenBottomRight")
        {
            return (Math.Max(workLeft + 10, workRight - w - 24),
                    Math.Max(workTop + 10, workBottom - h - 24),
                    null);
        }

        double workW = Math.Max(300, workRight - workLeft);
        double workH = Math.Max(300, workBottom - workTop);
        if (offsetY <= 0) offsetY = AppConstants.PillOffsetNormal;

        string? notice = null;
        double anchorX = dipX;
        double anchorY = dipY;

        // 입력 커서가 현재 모니터 작업 영역을 벗어났거나 표시하기 애매한 경우 마우스 커서로 폴백
        bool caretOutOfScreen = dipX < workLeft - 10 || dipX > workRight + 10 || dipY < workTop - 10 || dipY > workBottom + 10;
        if (caretOutOfScreen && placement != "MousePointer")
        {
            anchorX = mouseDipX;
            anchorY = mouseDipY + 16;
            notice = "📍 입력창이 화면 밖에 있어 마우스 위치로 일시 조정되었습니다.";
        }

        double rawLeft = anchorX;
        double clampedLeft = Math.Max(workLeft + 10, Math.Min(rawLeft, workRight - w - 10));

        bool preferAbove = anchorY > workTop + workH * 0.65 || (anchorY + offsetY + h > workBottom - 10);
        double rawTop = preferAbove ? (anchorY - h - 12) : (anchorY + offsetY);
        double clampedTop = Math.Max(workTop + 10, Math.Min(rawTop, workBottom - h - 10));

        if (notice == null && (Math.Abs(clampedLeft - rawLeft) > 2 || Math.Abs(clampedTop - rawTop) > 2))
        {
            notice = "📍 화면 경계에 닿아 잘림 방지를 위해 표시 위치가 일시 조정되었습니다.";
        }

        return (clampedLeft, clampedTop, notice);
    }

    private void Reposition(double caretX, double caretY)
    {
        var (dipX, dipY, workLeft, workTop, workRight, workBottom) =
            Win32Api.ConvertPhysicalPointAndGetWorkArea(this, caretX, caretY);

        Win32Api.GetCursorPos(out var mPt);
        var (mouseDipX, mouseDipY, _, _, _, _) =
            Win32Api.ConvertPhysicalPointAndGetWorkArea(this, mPt.X, mPt.Y);

        double workW = Math.Max(300, workRight - workLeft);
        double workH = Math.Max(300, workBottom - workTop);

        if (TxtPositionNotice != null)
        {
            TxtPositionNotice.Text = string.Empty;
            TxtPositionNotice.Visibility = Visibility.Collapsed;
        }

        // 현재 콘텐츠의 정확한 크기를 먼저 측정 (이전 상태의 ActualWidth/Height로 위치가 튀는 현상 방지)
        MainBorder.Measure(new Size(workW, workH));
        double w = MainBorder.DesiredSize.Width > 0 ? MainBorder.DesiredSize.Width : (ActualWidth > 0 ? ActualWidth : 320);
        double h = MainBorder.DesiredSize.Height > 0 ? MainBorder.DesiredSize.Height : (ActualHeight > 0 ? ActualHeight : 42);

        double winAlpha = Math.Clamp(_configManager.Config.WindowOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);
        Win32Api.SetWindowAlpha(this, winAlpha);

        string placement = _configManager.Config.PillPlacement ?? "CursorNearby";
        int offsetY = _configManager.Config.PillOffsetY;
        var (adjLeft, adjTop, notice) = ComputeAdjustedPosition(
            dipX, dipY, mouseDipX, mouseDipY, w, h,
            workLeft, workTop, workRight, workBottom, placement, offsetY);

        if (!string.IsNullOrEmpty(notice) && TxtPositionNotice != null)
        {
            TxtPositionNotice.Text = notice;
            TxtPositionNotice.Visibility = Visibility.Visible;
            MainBorder.Measure(new Size(workW, workH));
            w = MainBorder.DesiredSize.Width > 0 ? MainBorder.DesiredSize.Width : w;
            h = MainBorder.DesiredSize.Height > 0 ? MainBorder.DesiredSize.Height : h;
            (adjLeft, adjTop, _) = ComputeAdjustedPosition(
                dipX, dipY, mouseDipX, mouseDipY, w, h,
                workLeft, workTop, workRight, workBottom, placement, offsetY);
        }

        Left = adjLeft;
        Top = adjTop;
        Show();
        Win32Api.SetWindowAlpha(this, winAlpha);
    }

    private void SelectItem(TemplateItem item)
    {
        Hide();
        ItemSelected?.Invoke(item, _matchedLen, _targetHwnd);
    }
}
