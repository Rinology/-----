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
        loadingStack.Children.Add(new TextBlock
        {
            Text = "🌐 Google 영문 번역 중...",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
            Margin = new Thickness(0, 0, 0, 4)
        });

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

        // 구글 번역기 스타일 2열 비교 카드 (원문 ↔ 영문) + 하단 액션 바
        var outer = new StackPanel { MinWidth = 420, MaxWidth = 640 };

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
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
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

    private void Reposition(double caretX, double caretY)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        // 현재 콘텐츠의 정확한 크기를 먼저 측정 (이전 상태의 ActualWidth/Height로 위치가 튀는 현상 방지)
        MainBorder.Measure(new Size(screenWidth, screenHeight));
        double w = MainBorder.DesiredSize.Width > 0 ? MainBorder.DesiredSize.Width : (ActualWidth > 0 ? ActualWidth : 320);
        double h = MainBorder.DesiredSize.Height > 0 ? MainBorder.DesiredSize.Height : (ActualHeight > 0 ? ActualHeight : 42);

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

            // 화면 하단 35% 영역(일반 채팅 입력창 위치)에서는 UNKNOWN(1줄)이든 MATCH(여러 줄)든 항상 커서 위쪽에 일관되게 배치
            bool preferAbove = caretY > screenHeight * 0.65 || (caretY + offsetY + h > screenHeight - 10);
            if (preferAbove)
            {
                Top = Math.Max(10, caretY - h - 12);
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
