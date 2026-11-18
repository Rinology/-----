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
        if (candidates == null || candidates.Count == 0 || mode == "ManualOnly")
        {
            Hide();
            return;
        }

        _candidates = candidates;
        _matchedLen = matchedLen;
        _targetHwnd = targetHwnd;

        ItemsPanel.Children.Clear();

        var trigger = _configManager.Config.TriggerKey ?? "\\";
        var tabKeyHint = $"{trigger}+Tab";

        if (mode == "InlinePill")
        {
            MainBorder.CornerRadius = new CornerRadius(12);
            MainBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));

            var topItem = candidates[0];

            var badge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 6, 0)
            };
            var badgeText = new TextBlock
            {
                Text = tabKeyHint,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            badge.Child = badgeText;
            ItemsPanel.Children.Add(badge);

            var preview = topItem.Content.Trim().Replace("\n", " ");
            if (preview.Length > 28) preview = preview[..28] + "...";

            var btn = new Button
            {
                Content = $"{topItem.Title}: \"{preview}\"",
                Foreground = (Brush)new BrushConverter().ConvertFromString("#F3F4F6")!,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Focusable = false
            };
            btn.Click += (s, e) => SelectItem(topItem);
            ItemsPanel.Children.Add(btn);
        }
        else
        {
            MainBorder.CornerRadius = new CornerRadius(7);
            MainBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F3F46"));

            int maxCount = Math.Min(3, candidates.Count);
            for (int i = 0; i < maxCount; i++)
            {
                var item = candidates[i];
                string keyHint = (i == 0) ? tabKeyHint : $"{trigger}+{i + 1}";

                var chip = new Button
                {
                    Content = $"[{keyHint}] {item.Title}",
                    Foreground = Brushes.White,
                    Background = (i == 0) ? (Brush)new BrushConverter().ConvertFromString("#1E3A8A")! : (Brush)new BrushConverter().ConvertFromString("#27272A")!,
                    BorderBrush = (i == 0) ? (Brush)new BrushConverter().ConvertFromString("#3B82F6")! : (Brush)new BrushConverter().ConvertFromString("#3F3F46")!,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(6, 2, 6, 2),
                    Cursor = Cursors.Hand,
                    FontSize = 10,
                    FontWeight = (i == 0) ? FontWeights.Bold : FontWeights.Normal,
                    Focusable = false
                };
                chip.Click += (s, e) => SelectItem(item);
                ItemsPanel.Children.Add(chip);
            }
        }

        UpdatePositionNearCaret();
        Show();
    }

    private void UpdatePositionNearCaret()
    {
        double x = 0, y = 0;

        IntPtr hwndFg = Win32Api.GetForegroundWindow();
        uint tid = Win32Api.GetWindowThreadProcessId(hwndFg, IntPtr.Zero);
        var gui = new Win32Api.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32Api.GUITHREADINFO)) };

        bool gotCaret = false;
        if (Win32Api.GetGUIThreadInfo(tid, ref gui))
        {
            IntPtr target = gui.hwndCaret != IntPtr.Zero ? gui.hwndCaret : gui.hwndFocus;
            if (target != IntPtr.Zero)
            {
                var pt = new Win32Api.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Bottom };
                if (Win32Api.ClientToScreen(target, ref pt) && pt.X > 0 && pt.Y > 0)
                {
                    x = pt.X;
                    y = pt.Y;
                    gotCaret = true;
                }
            }
        }

        if (!gotCaret)
        {
            Win32Api.GetCursorPos(out var mPt);
            x = mPt.X;
            y = mPt.Y;
        }

        var screenWidth = SystemParameters.PrimaryScreenWidth;

        Left = Math.Min(x, screenWidth - ActualWidth - 10);
        Top = (y - ActualHeight - 4 > 0) ? y - ActualHeight - 4 : y + 22;
    }

    private void SelectItem(TemplateItem item)
    {
        Hide();
        ItemSelected?.Invoke(item, _matchedLen, _targetHwnd);
    }
}
