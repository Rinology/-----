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
    private System.Windows.Threading.DispatcherTimer? _loadingTimer;
    private bool _loadingFlip;

    public event Action<int>? MemoCountChanged;
    public event Action<string, IntPtr>? PasteSingleRequested;

    public void SetOcrLoading(bool isLoading)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetOcrLoading(isLoading));
            return;
        }

        if (isLoading)
        {
            LoadingBadge.Visibility = Visibility.Visible;
            if (_loadingTimer == null)
            {
                _loadingTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(320)
                };
                _loadingTimer.Tick += (_, _) =>
                {
                    _loadingFlip = !_loadingFlip;
                    TxtLoadingIcon.Text = _loadingFlip ? "⌛" : "⏳";
                };
            }
            _loadingTimer.Start();
        }
        else
        {
            _loadingTimer?.Stop();
            LoadingBadge.Visibility = Visibility.Collapsed;
        }
    }

    public void MarkCustomPosition() => _hasCustomPosition = true;

    public void UpdateDockState(bool docked)
    {
        // 자석 도킹은 숨은 기능(근접 스냅 / Xenie 측 버튼)으로 동작
    }

    public int MemoCount => _entries.Count;
    public IReadOnlyList<MemoEntry> Entries => _entries;

    public StickyMemoWindow(ConfigManager configManager)
    {
        InitializeComponent();
        _configManager = configManager;
        ThemeManager.ThemeChanged += () => Dispatcher.BeginInvoke(ApplyThemeColor);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Win32Api.EnforceTopmost(this, toolWindow: true, noActivate: true);
        ApplyThemeColor();
        ApplyOpacity();
        Deactivated += (_, _) => Win32Api.EnforceTopmost(this, toolWindow: true, noActivate: true);
    }

    private (string Bg, string HeaderBg, string Border, string AccentText, string AddBg) GetMemoPalette()
    {
        string accent = !string.IsNullOrWhiteSpace(ThemeManager.CurrentAccent)
            ? ThemeManager.CurrentAccent
            : (_configManager.Config.AccentColor ?? "Blue");
        return ThemeManager.GetEmphasizedMemoPalette(accent);
    }

    public void ApplyThemeColor()
    {
        var (bgHex, headerHex, borderHex, textHex, addBgHex) = GetMemoPalette();
        var bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
        var headerBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(headerHex));
        var border = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
        var text = new SolidColorBrush((Color)ColorConverter.ConvertFromString(textHex));
        var addBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(addBgHex));

        Background = bg;
        MainBorder.Background = bg;
        MainBorder.BorderBrush = border;
        HeaderBorder.Background = headerBg;
        HeaderBorder.BorderBrush = border;
        FooterBorder.Background = headerBg;
        FooterBorder.BorderBrush = border;
        TxtMemoHeader.Foreground = text;
        TxtFooterLine1.Foreground = text;
        TxtFooterLine2.Foreground = text;
        BtnPasteClip.Foreground = text;
        BtnPasteClip.BorderBrush = border;
        BtnClearAll.Foreground = text;
        BtnClearAll.BorderBrush = border;
        BtnClose.Foreground = text;
        TxtQuickMemo.BorderBrush = border;
        BtnAddQuickMemo.Background = addBg;
        BtnAddQuickMemo.Foreground = text;
        BtnAddQuickMemo.BorderBrush = border;
        CmbDatePreset.Foreground = text;
        CmbDatePreset.BorderBrush = border;
        RenderCards();
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

    /// <summary>
    /// 답변조합기(Xenie)에서 변수로 사용된 임시메모 항목들을 자동으로 소모(삭제)합니다.
    /// </summary>
    public void RemoveUsedMemos(IReadOnlyList<string> usedValues)
    {
        if (usedValues == null || usedValues.Count == 0 || _entries.Count == 0)
            return;

        bool changed = false;
        foreach (var val in usedValues)
        {
            if (string.IsNullOrWhiteSpace(val)) continue;
            string target = val.Trim();
            int idx = _entries.FindIndex(e => string.Equals(e.Content?.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _entries.RemoveAt(idx);
                changed = true;
            }
        }

        if (changed)
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
        var workArea = SystemParameters.WorkArea;
        Height = Math.Min(760, Math.Max(440, workArea.Height - 40));

        if (!_hasCustomPosition)
        {
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
        TxtMemoHeader.Text = $"ChatGENIE ({_entries.Count})";
        MemoCardsPanel.Children.Clear();

        if (_entries.Count == 0)
        {
            MemoCardsPanel.Children.Add(new TextBlock
            {
                Text = "메모 없음\n\n직접추가(Enter) · 클립보드(\\+V)\n단어담기(\\+M) · 비우기(\\+C)",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854D0E")),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
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
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFE0A8")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                ToolTip = "드래그해서 변수 칸/채팅창에 놓기 · 더블클릭으로 즉시 입력"
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
                    PasteSingleRequested?.Invoke(entry.Content, ResolveTargetWindow());
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string resolvedKind = ConsultationMemoService.ResolveMemoKind(entry);
            string badgeBgHex = resolvedKind switch
            {
                "날짜" => "#2563EB",
                "송장" => "#D97706",
                "금액" => "#059669",
                _ => "#B45309"
            };
            var numBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeBgHex)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                ToolTip = "클릭하여 변수 타입 변경 (메모 / 송장 / 날짜 / 금액)"
            };
            numBadge.Child = new TextBlock
            {
                Text = $"{ConsultationMemoService.DetectMemoLabel(entry.Content, idx + 1, entry.KindOverride)} ▾",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            numBadge.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                var menu = new ContextMenu();
                foreach (var (kindVal, labelText) in new[]
                {
                    ("메모", "📝 메모"),
                    ("송장", "📦 송장"),
                    ("날짜", "📅 날짜"),
                    ("금액", "💰 금액")
                })
                {
                    var mi = new MenuItem
                    {
                        Header = labelText,
                        IsChecked = string.Equals(resolvedKind, kindVal, StringComparison.Ordinal)
                    };
                    string chosenKind = kindVal;
                    mi.Click += (_, _) =>
                    {
                        entry.KindOverride = chosenKind;
                        RenderCards();
                    };
                    menu.Items.Add(mi);
                }
                menu.PlacementTarget = numBadge;
                menu.IsOpen = true;
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
            btnPaste.Click += (s, e) => PasteSingleRequested?.Invoke(entry.Content, ResolveTargetWindow());

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
            if (entry.Content.Contains('\n'))
            {
                var btnSplit = new Button
                {
                    Content = "줄나누기",
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 2, 0),
                    FontSize = 9.5,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    ToolTip = "여러 줄 텍스트를 줄별로 개별 메모 카드로 나눕니다"
                };
                btnSplit.Click += (s, e) =>
                {
                    var lines = entry.Content
                        .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
                    if (lines.Count > 1 && idx >= 0 && idx < _entries.Count)
                    {
                        _entries.RemoveAt(idx);
                        for (int k = 0; k < lines.Count; k++)
                        {
                            string line = lines[k];
                            _entries.Insert(idx + k, new MemoEntry
                            {
                                Keyword = line.Length > 14 ? line[..14] + "…" : line,
                                Category = "메모",
                                MemoNote = line,
                                Content = line
                            });
                        }
                        RenderCards();
                    }
                };
                actions.Children.Add(btnSplit);
            }
            actions.Children.Add(btnDel);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            MemoCardsPanel.Children.Add(card);
        }
    }

    private IntPtr ResolveTargetWindow()
    {
        if (Win32Api.IsValidTargetWindow(_targetHwnd))
            return _targetHwnd;
        IntPtr fg = Win32Api.GetForegroundWindow();
        if (Win32Api.IsValidTargetWindow(fg))
        {
            _targetHwnd = fg;
            return fg;
        }
        return IntPtr.Zero;
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

    private void CmbDatePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbDatePreset == null || CmbDatePreset.SelectedIndex <= 0) return;
        if (CmbDatePreset.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            var ko = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");
            string val = tag switch
            {
                "Today" => DateTime.Today.ToString("M월 d일 (ddd)", ko),
                "Tomorrow" => DateTime.Today.AddDays(1).ToString("M월 d일 (ddd)", ko),
                "TodayPm" => $"{DateTime.Today.ToString("M월 d일 (ddd)", ko)} 오후",
                "TomorrowAm" => $"{DateTime.Today.AddDays(1).ToString("M월 d일 (ddd)", ko)} 오전",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(val))
                AddMemo(val, ResolveTargetWindow());
        }
        CmbDatePreset.SelectedIndex = 0;
    }

    private void AddQuickMemoFromInput()
    {
        string text = TxtQuickMemo.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        TxtQuickMemo.Clear();
        AddMemo(text, _targetHwnd);
    }

    private async void BtnPasteClip_Click(object sender, RoutedEventArgs e)
    {
        SetOcrLoading(true);
        try
        {
            var res = await ClipboardSmartService.GetCustomerMessageAsync();
            string clip = res.ExtractedText.Trim();
            if (!string.IsNullOrWhiteSpace(clip))
            {
                AddMemo(clip, _targetHwnd);
            }
        }
        finally
        {
            SetOcrLoading(false);
        }
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e) => ClearMemos();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

    private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !(e.OriginalSource is TextBox) && !(e.OriginalSource is Button) && !CmbDatePreset.IsMouseOver)
        {
            _hasCustomPosition = true;
            try { DragMove(); } catch { }
            SnapToMonitorEdge();
        }
    }

    private void SnapToMonitorEdge()
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            const double snap = 18;
            if (Math.Abs(Left - wa.Left) <= snap) Left = wa.Left;
            else if (Math.Abs((Left + Width) - wa.Right) <= snap) Left = wa.Right - Width;
            if (Math.Abs(Top - wa.Top) <= snap) Top = wa.Top;
            else if (Math.Abs((Top + Height) - wa.Bottom) <= snap) Top = wa.Bottom - Height;
        }
        catch { }
    }
}
