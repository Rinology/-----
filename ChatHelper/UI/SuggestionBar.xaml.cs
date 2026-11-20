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
    private List<TemplateItem> _candidates = new();
    private int _matchedLen = 0;
    private IntPtr _targetHwnd = IntPtr.Zero;
    private string _recognizedToken = string.Empty;

    public event Action<TemplateItem, int, IntPtr>? ItemSelected;

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

        if (!Win32Api.TryGetValidInputPosition(out double caretX, out double caretY, out _))
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
            RecognitionHeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
            RecognitionHeaderBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            TxtRecognitionHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"));
            TxtRecognitionHeader.Text = $"❓ [알아듣지 못함] 인식된 입력: \"{token}\" (일치하는 키워드 없음)";
        }
        else if (statusMode == "WAITING")
        {
            RecognitionHeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
            RecognitionHeaderBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            TxtRecognitionHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
            TxtRecognitionHeader.Text = string.IsNullOrWhiteSpace(token)
                ? "⏳ [대기중] 커서 입력 대기 중..."
                : $"⏳ [대기중] 입력 인식 중: \"{token}\"";
        }
        else
        {
            RecognitionHeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0F2FE"));
            RecognitionHeaderBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
            TxtRecognitionHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#075985"));
            string displayToken = string.IsNullOrWhiteSpace(token) ? $"{matchedLen}글자" : $"\"{token}\" ({matchedLen}글자 치환)";
            TxtRecognitionHeader.Text = $"🔎 인식 구간: {displayToken}";
        }
    }

    public SuggestionBar(ConfigManager configManager)
    {
        InitializeComponent();
        _configManager = configManager;
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
        if (!Win32Api.TryGetValidInputPosition(out double caretX, out double caretY, out _))
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

        var theme = _configManager.Config.Theme ?? "Win98";
        int scale = _configManager.Config.PillScale;
        if (scale < 1) scale = 2; // 기본 2배(크게)

        // 3단계 크기 배율 계수
        double badgeFontSize = scale switch { 1 => 11, 3 => 16, _ => 13 };
        double contentFontSize = scale switch { 1 => 12, 3 => 19, _ => 16 };
        Thickness badgePadding = scale switch { 1 => new Thickness(6, 2, 6, 2), 3 => new Thickness(12, 5, 12, 5), _ => new Thickness(9, 3, 9, 3) };
        Thickness mainPadding = scale switch { 1 => new Thickness(8, 4, 8, 4), 3 => new Thickness(18, 10, 18, 10), _ => new Thickness(14, 7, 14, 7) };
        int maxPreviewLen = scale switch { 1 => 35, 3 => 85, _ => 65 };

        MainBorder.Padding = mainPadding;

        var sheetItems = candidates.Where(c => c.Category != "AI생성" && c.Category != "AI번역").ToList();
        var aiItem = candidates.FirstOrDefault(c => c.Category == "AI생성") ?? candidates.FirstOrDefault(c => c.Category == "AI번역");

        // '\' 단독 즉시 실행 모드이거나 InlinePill 모드인 경우 단일 알약으로 고정 (AI 추천은 별도 아래 줄에 표시)
        if (completeMode == "SlashOnly" || mode == "InlinePill")
        {
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFE1")); // Win98 툴팁 크림 옐로우
            MainBorder.BorderBrush = Brushes.Black;

            if (sheetItems.Count > 0)
            {
                var topItem = sheetItems[0];

                var badge = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000080")), // Win98 네이비
                    CornerRadius = new CornerRadius(0),
                    Padding = badgePadding,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var badgeText = new TextBlock
                {
                    Text = tabKeyHint,
                    Foreground = Brushes.White,
                    FontSize = badgeFontSize,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI")
                };
                badge.Child = badgeText;
                ItemsPanel.Children.Add(badge);

                var preview = topItem.Content.Trim().Replace("\r\n", " ").Replace("\n", " ");
                if (preview.Length > maxPreviewLen) preview = preview[..maxPreviewLen] + "...";

                var btn = new Button
                {
                    Content = $"{topItem.Title}: \"{preview}\"",
                    Foreground = Brushes.Black,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontSize = contentFontSize,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI"),
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
            MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")); // Win98 실버 그레이
            MainBorder.BorderBrush = Brushes.Black;

            // 유저가 설정한 최대 개수 (3 ~ 10개) 반영
            int maxAllowed = Math.Clamp(_configManager.Config.MaxCandidatesCount, 3, 10);
            var visibleSheetItems = sheetItems.Take(maxAllowed).ToList();

            if (visibleSheetItems.Count == 0)
            {
                ItemsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                for (int i = 0; i < visibleSheetItems.Count; i++)
                {
                    var item = visibleSheetItems[i];
                    string keyHint = (i == 0) ? tabKeyHint : $"{trigger}+{i + 1}";

                    var chip = new Button
                    {
                        Content = $"[{keyHint}] {item.Title}",
                        Foreground = (i > 0) ? Brushes.Black : Brushes.White,
                        Background = (i == 0)
                            ? (Brush)new BrushConverter().ConvertFromString("#000080")!
                            : (Brush)new BrushConverter().ConvertFromString("#D4D0C8")!,
                        BorderBrush = (Brush)new BrushConverter().ConvertFromString("#808080")!,
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 6, 5),
                        Padding = badgePadding,
                        Cursor = Cursors.Hand,
                        FontSize = contentFontSize - 2,
                        FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI"),
                        FontWeight = (i == 0) ? FontWeights.Bold : FontWeights.Normal,
                        Focusable = false
                    };
                    chip.Click += (s, e) => SelectItem(item);
                    ItemsPanel.Children.Add(chip);
                }
            }
        }

        // 🤖 AI 추천 전용 별도 라인 (설정에 따라 상단 또는 하단에 배치)
        if (aiItem != null)
        {
            RenderAiRow(aiItem);
        }

        UpdateLayout();
        Reposition(caretX, caretY);
        Show();
    }

    public void ShowAiGeneratingPlaceholder(string promptHint = "")
    {
        if (!_configManager.Config.EnableLocalAi || !_configManager.Config.IsEnabled) return;

        if (!Win32Api.TryGetValidInputPosition(out double caretX, out double caretY, out _))
            return;

        var targetPanel = (_configManager.Config.AiPlacement == "Top") ? TopAiRowPanel : BottomAiRowPanel;
        var otherPanel = (_configManager.Config.AiPlacement == "Top") ? BottomAiRowPanel : TopAiRowPanel;

        otherPanel.Children.Clear();
        otherPanel.Visibility = Visibility.Collapsed;

        targetPanel.Children.Clear();
        targetPanel.Visibility = Visibility.Visible;

        int scale = _configManager.Config.PillScale;
        if (scale < 1) scale = 2;
        double badgeFontSize = scale switch { 1 => 11, 3 => 16, _ => 13 };
        double contentFontSize = scale switch { 1 => 12, 3 => 19, _ => 16 };
        Thickness badgePadding = scale switch { 1 => new Thickness(6, 2, 6, 2), 3 => new Thickness(12, 5, 12, 5), _ => new Thickness(9, 3, 9, 3) };

        var aiBadge = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#4F46E5")!,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding = badgePadding,
            Margin = new Thickness(0, 0, 8, 0)
        };
        aiBadge.Child = new TextBlock
        {
            Text = "AI",
            Foreground = Brushes.White,
            FontSize = badgeFontSize,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI")
        };
        targetPanel.Children.Add(aiBadge);

        var placeholderBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#EEF2FF")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#6366F1")!,
            BorderThickness = new Thickness(1),
            Padding = badgePadding
        };
        string text = string.IsNullOrWhiteSpace(promptHint)
            ? "⚡ 로컬 AI 답변 생성 중..."
            : $"⚡ 로컬 AI 답변 생성 중... (\"{promptHint}\")";
        var placeholderText = new TextBlock
        {
            Text = text,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#1E1B4B")!,
            FontSize = contentFontSize - 1,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI")
        };
        placeholderBorder.Child = placeholderText;
        targetPanel.Children.Add(placeholderBorder);

        UpdateLayout();
        Reposition(caretX, caretY);
        Show();
    }

    public void UpdateAiCandidate(TemplateItem aiItem)
    {
        if (aiItem == null) return;
        int existingIdx = _candidates.FindIndex(c => c.Category == aiItem.Category);
        if (existingIdx >= 0)
        {
            _candidates[existingIdx] = aiItem;
        }
        else
        {
            _candidates.Add(aiItem);
        }

        RenderAiRow(aiItem);
        UpdateLayout();
        if (Win32Api.TryGetValidInputPosition(out double cx, out double cy, out _))
        {
            Reposition(cx, cy);
        }
        Show();
    }

    private void RenderAiRow(TemplateItem _)
    {
        var targetPanel = (_configManager.Config.AiPlacement == "Top") ? TopAiRowPanel : BottomAiRowPanel;
        var otherPanel = (_configManager.Config.AiPlacement == "Top") ? BottomAiRowPanel : TopAiRowPanel;

        otherPanel.Children.Clear();
        otherPanel.Visibility = Visibility.Collapsed;

        targetPanel.Children.Clear();
        var aiItems = _candidates.Where(c => c.Category == "AI생성" || c.Category == "AI번역").ToList();
        if (aiItems.Count == 0)
        {
            targetPanel.Visibility = Visibility.Collapsed;
            return;
        }
        targetPanel.Visibility = Visibility.Visible;

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
        int maxPreviewLen = scale switch { 1 => 35, 3 => 75, _ => 55 };

        var sheetItems = _candidates.Where(c => c.Category != "AI생성" && c.Category != "AI번역").ToList();

        foreach (var item in aiItems)
        {
            bool isTrans = item.Category == "AI번역";
            string keyHint = (sheetItems.Count == 0 && item == aiItems[0])
                ? tabKeyHint
                : (isTrans ? $"{trigger}+E" : $"{trigger}+A");

            string badgeHex = isTrans ? "#047857" : "#1D4ED8";
            string bgHex = isTrans ? "#ECFDF5" : "#EFF6FF";
            string prefixLabel = isTrans ? "🌐 영문 번역" : "🤖 AI 추천";

            var aiBadge = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(badgeHex)!,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = badgePadding,
                Margin = new Thickness(0, 0, 6, 0)
            };
            aiBadge.Child = new TextBlock
            {
                Text = keyHint,
                Foreground = Brushes.White,
                FontSize = badgeFontSize,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI")
            };
            targetPanel.Children.Add(aiBadge);

            var aiPreview = item.Content.Trim().Replace("\r\n", " ").Replace("\n", " ");
            if (aiPreview.Length > maxPreviewLen) aiPreview = aiPreview[..maxPreviewLen] + "...";

            var aiBtn = new Button
            {
                Content = $"{prefixLabel}: \"{aiPreview}\"",
                Foreground = Brushes.Black,
                Background = (Brush)new BrushConverter().ConvertFromString(bgHex)!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(badgeHex)!,
                BorderThickness = new Thickness(1),
                Padding = badgePadding,
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                FontSize = contentFontSize - 1,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI"),
                Focusable = false
            };
            var captured = item;
            aiBtn.Click += (s, e) => SelectItem(captured);
            targetPanel.Children.Add(aiBtn);
        }
    }

    private void Reposition(double caretX, double caretY)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        double w = ActualWidth > 0 ? ActualWidth : 320;
        double h = ActualHeight > 0 ? ActualHeight : 42;

        // 창 투명도 (흐리게 조절)
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
