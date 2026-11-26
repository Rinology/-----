using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ChatHelper.Constants;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

/// <summary>
/// 상담 중 복사한 송장번호·고객명·단어 조각을 가볍게 모아두는 초경량 플로팅 포스트잇 메모지.
/// 스무고개 답변 조합기와 분리되어 있으며, 드래그 앤 드롭 및 {1}/{2} 변수 자동 공급을 지원합니다.
/// </summary>
public partial class StickyMemoWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly List<MemoEntry> _entries = new();
    private IntPtr _targetHwnd = IntPtr.Zero;
    private bool _hasCustomPosition = false;

    public event Action<int>? MemoCountChanged;
    public event Action<IntPtr>? OpenCombinerRequested;
    public event Action<string, IntPtr>? PasteSingleRequested;

    public int MemoCount => _entries.Count;
    public IReadOnlyList<MemoEntry> Entries => _entries;

    public StickyMemoWindow(ConfigManager configManager)
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
        ApplyOpacity();
    }

    public void ApplyOpacity()
    {
        double alpha = Math.Clamp(_configManager.Config.MemoOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);
        Win32Api.SetWindowAlpha(this, alpha);
    }

    public void AddMemo(string text, IntPtr targetHwnd)
    {
        if (!_configManager.Config.EnableConsultationMemo) return;
        string clean = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean)) return;

        if (Win32Api.IsValidTargetWindow(targetHwnd))
            _targetHwnd = targetHwnd;

        _entries.Add(new MemoEntry
        {
            Keyword = clean.Length > 14 ? clean[..14] + "…" : clean,
            Category = "메모",
            MemoNote = clean,
            Content = clean
        });

        RenderCards();
        if (!IsVisible)
            ShowAtPosition();
    }

    public void ClearMemos()
    {
        _entries.Clear();
        RenderCards();
    }

    public void ToggleWindow(IntPtr targetHwnd)
    {
        if (Win32Api.IsValidTargetWindow(targetHwnd))
            _targetHwnd = targetHwnd;

        if (IsVisible)
        {
            Hide();
            return;
        }

        if (!_configManager.Config.EnableConsultationMemo)
        {
            _configManager.Config.EnableConsultationMemo = true;
            _configManager.Save();
        }

        RenderCards();
        ShowAtPosition();
    }

    private void ShowAtPosition()
    {
        if (!_hasCustomPosition)
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left + 12, workArea.Right - Width - 20);
            Top = Math.Max(workArea.Top + 12, workArea.Bottom - Height - 72);
        }

        ApplyOpacity();
        if (!IsVisible)
            Show();
        ApplyOpacity();
    }

    private void RenderCards()
    {
        MemoCountChanged?.Invoke(_entries.Count);
        TxtMemoHeader.Text = $"📌 포스트잇 메모 ({_entries.Count})";
        MemoCardsPanel.Children.Clear();

        if (_entries.Count == 0)
        {
            MemoCardsPanel.Children.Add(new TextBlock
            {
                Text = "아직 메모가 없습니다.\n\n• 위 입력창에 직접 적거나 📋 클립보드 클릭\n• 채팅창에서 단어 입력 후 \\+M\n• 복사 후 \\+V (이미지 OCR 포함)",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E")),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(8, 28, 8, 8)
            });
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            int idx = i;
            var entry = _entries[i];

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                ToolTip = "드래그해서 스무고개 변수 칸이나 채팅창에 놓기 · 더블클릭으로 즉시 입력"
            };
            card.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && !(e.OriginalSource is Button))
                {
                    if (!string.IsNullOrWhiteSpace(entry.Content))
                    {
                        try { DragDrop.DoDragDrop(card, entry.Content, DragDropEffects.Copy); } catch { }
                    }
                }
            };
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    PasteSingleRequested?.Invoke(entry.Content, _targetHwnd);
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var numBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            numBadge.Child = new TextBlock
            {
                Text = $"{{{idx + 1}}}",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(numBadge, 0);
            grid.Children.Add(numBadge);

            var txt = new TextBlock
            {
                Text = entry.Content,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 56,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txt, 1);
            grid.Children.Add(txt);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 0, 0, 0) };
            var btnPaste = new Button
            {
                Content = "입력",
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 2, 0),
                FontSize = 10,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D")),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = false,
                ToolTip = "이 메모를 채팅창에 바로 입력"
            };
            btnPaste.Click += (s, e) => PasteSingleRequested?.Invoke(entry.Content, _targetHwnd);

            var btnDel = new Button
            {
                Content = "✕",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                FontSize = 10,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C")),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            btnDel.Click += (s, e) =>
            {
                if (idx >= 0 && idx < _entries.Count)
                {
                    _entries.RemoveAt(idx);
                    RenderCards();
                }
            };

            actions.Children.Add(btnPaste);
            actions.Children.Add(btnDel);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            MemoCardsPanel.Children.Add(card);
        }
    }

    private void TxtQuickMemo_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        Win32Api.ForceForegroundWindow(helper.Handle);
        Activate();
        TxtQuickMemo.Focus();
    }

    private void TxtQuickMemo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddQuickMemoFromInput();
            e.Handled = true;
        }
    }

    private void BtnAddQuickMemo_Click(object sender, RoutedEventArgs e) => AddQuickMemoFromInput();

    private void AddQuickMemoFromInput()
    {
        string text = TxtQuickMemo.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        TxtQuickMemo.Clear();
        AddMemo(text, _targetHwnd);
    }

    private async void BtnPasteClip_Click(object sender, RoutedEventArgs e)
    {
        var res = await ClipboardSmartService.GetCustomerMessageAsync();
        string clip = res.ExtractedText.Trim();
        if (!string.IsNullOrWhiteSpace(clip))
        {
            AddMemo(clip, _targetHwnd);
        }
    }

    private void BtnOpenCombiner_Click(object sender, RoutedEventArgs e) => OpenCombinerRequested?.Invoke(_targetHwnd);

    private void BtnClearAll_Click(object sender, RoutedEventArgs e) => ClearMemos();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

    private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !(e.OriginalSource is TextBox) && !(e.OriginalSource is Button))
        {
            _hasCustomPosition = true;
            try { DragMove(); } catch { }
        }
    }
}
