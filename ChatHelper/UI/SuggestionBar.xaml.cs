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

    public event Action<TemplateItem, int, IntPtr>? ItemSelected;

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

        var sheetItems = candidates.Where(c => c.Category != "AI생성").ToList();
        var aiItem = candidates.FirstOrDefault(c => c.Category == "AI생성");

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
        if (!_candidates.Any(c => c.Category == "AI생성"))
        {
            _candidates.Add(aiItem);
        }
        else
        {
            int idx = _candidates.FindIndex(c => c.Category == "AI생성");
            if (idx >= 0) _candidates[idx] = aiItem;
        }

        RenderAiRow(aiItem);
        UpdateLayout();
        if (Win32Api.TryGetValidInputPosition(out double cx, out double cy, out _))
        {
            Reposition(cx, cy);
        }
        Show();
    }

    private void RenderAiRow(TemplateItem aiItem)
    {
        var targetPanel = (_configManager.Config.AiPlacement == "Top") ? TopAiRowPanel : BottomAiRowPanel;
        var otherPanel = (_configManager.Config.AiPlacement == "Top") ? BottomAiRowPanel : TopAiRowPanel;

        otherPanel.Children.Clear();
        otherPanel.Visibility = Visibility.Collapsed;

        targetPanel.Children.Clear();
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
        int maxPreviewLen = scale switch { 1 => 35, 3 => 85, _ => 65 };

        var sheetItems = _candidates.Where(c => c.Category != "AI생성").ToList();
        string aiKeyHint = sheetItems.Count == 0 ? tabKeyHint : $"{trigger}+A";

        var aiBadge = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#1D4ED8")!,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding = badgePadding,
            Margin = new Thickness(0, 0, 8, 0)
        };
        aiBadge.Child = new TextBlock
        {
            Text = aiKeyHint,
            Foreground = Brushes.White,
            FontSize = badgeFontSize,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI")
        };
        targetPanel.Children.Add(aiBadge);

        var aiPreview = aiItem.Content.Trim().Replace("\r\n", " ").Replace("\n", " ");
        if (aiPreview.Length > maxPreviewLen) aiPreview = aiPreview[..maxPreviewLen] + "...";

        var aiBtn = new Button
        {
            Content = $"🤖 AI 추천: \"{aiPreview}\"",
            Foreground = Brushes.Black,
            Background = (Brush)new BrushConverter().ConvertFromString("#EFF6FF")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#1D4ED8")!,
            BorderThickness = new Thickness(1),
            Padding = badgePadding,
            Cursor = Cursors.Hand,
            FontSize = contentFontSize - 1,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI"),
            Focusable = false
        };
        aiBtn.Click += (s, e) => SelectItem(aiItem);
        targetPanel.Children.Add(aiBtn);
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
