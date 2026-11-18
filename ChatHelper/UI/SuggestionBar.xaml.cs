using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

        ItemsPanel.Children.Clear();

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

        // '\' 단독 즉시 실행 모드이거나 InlinePill 모드인 경우 단일 알약으로 고정
        if (completeMode == "SlashOnly" || mode == "InlinePill")
        {
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFE1")); // Win98 툴팁 크림 옐로우
            MainBorder.BorderBrush = Brushes.Black;

            var topItem = candidates[0];

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
                FontWeight = FontWeights.Bold
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
                Focusable = false
            };
            btn.Click += (s, e) => SelectItem(topItem);
            ItemsPanel.Children.Add(btn);
        }
        else
        {
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")); // Win98 실버 그레이
            MainBorder.BorderBrush = Brushes.Black;

            // 유저가 설정한 최대 개수 (3 ~ 10개) 반영
            int maxAllowed = Math.Clamp(_configManager.Config.MaxCandidatesCount, 3, 10);
            int maxCount = Math.Min(maxAllowed, candidates.Count);
            for (int i = 0; i < maxCount; i++)
            {
                var item = candidates[i];
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
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = badgePadding,
                    Cursor = Cursors.Hand,
                    FontSize = contentFontSize - 2,
                    FontWeight = (i == 0) ? FontWeights.Bold : FontWeights.Normal,
                    Focusable = false
                };
                chip.Click += (s, e) => SelectItem(item);
                ItemsPanel.Children.Add(chip);
            }
        }

        UpdateLayout();
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        double w = ActualWidth > 0 ? ActualWidth : 320;
        double h = ActualHeight > 0 ? ActualHeight : 42;

        // 창 투명도 (흐리게 조절)
        Opacity = Math.Clamp(_configManager.Config.WindowOpacity, 0.5, 1.0);

        if (_configManager.Config.PillPlacement == "ScreenBottomRight")
        {
            Left = screenWidth - w - 24;
            Top = screenHeight - h - 64;
        }
        else
        {
            int offsetY = _configManager.Config.PillOffsetY;
            if (offsetY <= 0) offsetY = 35;

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
