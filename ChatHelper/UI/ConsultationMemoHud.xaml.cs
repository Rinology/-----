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
/// 채팅창 포커스를 빼앗지 않는 별도의 상담 스무고개 메모 HUD.
/// 메모가 0개이거나 기능이 OFF이면 숨겨지며, 1개 이상 누적되면 대화를 이어가는 동안 계속 유지됩니다.
/// </summary>
public partial class ConsultationMemoHud : Window
{
    private readonly TemplateManager _templateManager;
    private readonly ConfigManager _configManager;
    private readonly List<MemoEntry> _entries = new();
    private IntPtr _targetHwnd = IntPtr.Zero;
    private bool _hasCustomPosition = false;
    private bool _suppressPreviewSync = false;
    private bool _isPreviewEdited = false;

    public event Action<string, IntPtr, int>? FinalInsertRequested;
    public event Action<IntPtr>? OpenDetailRequested;
    public event Action<string>? RegisterSheetRequested;
    public event Action<int>? MemoCountChanged;

    public int MemoCount => _entries.Count;
    public IReadOnlyList<MemoEntry> Entries => _entries;

    public ConsultationMemoHud(TemplateManager templateManager, ConfigManager configManager)
    {
        InitializeComponent();
        _templateManager = templateManager;
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
        double alpha = Math.Clamp(_configManager.Config.WindowOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);
        Win32Api.SetWindowAlpha(this, alpha);
    }

    public void AddMemo(TemplateItem? candidate, string rawKeyword, IntPtr targetHwnd)
    {
        if (!_configManager.Config.EnableConsultationMemo) return;
        if (Win32Api.IsValidTargetWindow(targetHwnd))
            _targetHwnd = targetHwnd;

        string kw = !string.IsNullOrWhiteSpace(candidate?.Keyword)
            ? candidate.Keyword
            : (string.IsNullOrWhiteSpace(rawKeyword) ? "메모" : rawKeyword.Trim());

        string content = !string.IsNullOrWhiteSpace(candidate?.Content)
            ? candidate.Content
            : rawKeyword.Trim();

        if (string.IsNullOrWhiteSpace(kw) && string.IsNullOrWhiteSpace(content))
            return;

        _entries.Add(new MemoEntry
        {
            Keyword = kw,
            Category = candidate?.Category ?? "메모",
            MemoNote = candidate?.Title ?? kw,
            Content = content
        });

        _isPreviewEdited = false;
        RefreshHud();
    }

    public void ReplaceEntries(IEnumerable<MemoEntry> entries)
    {
        _entries.Clear();
        if (entries != null)
            _entries.AddRange(entries);
        _isPreviewEdited = false;
        RefreshHud();
    }

    public void ClearMemos()
    {
        _entries.Clear();
        _isPreviewEdited = false;
        RefreshHud();
    }

    private MemoJoinMode GetSelectedJoinMode()
    {
        if (CmbHudJoinMode?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "LineBreak" => MemoJoinMode.LineBreak,
                "Numbered" => MemoJoinMode.Numbered,
                "Bulleted" => MemoJoinMode.Bulleted,
                _ => MemoJoinMode.Paragraph
            };
        }
        return MemoJoinMode.Paragraph;
    }

    private void CmbHudJoinMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && _entries.Count > 0)
        {
            _isPreviewEdited = false;
            RefreshHud();
        }
    }

    public string BuildFinalText()
    {
        if (_entries.Count == 0) return string.Empty;

        var chips = _entries.Select(e => e.Keyword).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        if (chips.Count >= 2)
        {
            var narrowed = ConsultationMemoService.FilterTemplates(_templateManager.Templates, chips, null);
            if (narrowed.Count == 1 && !string.IsNullOrWhiteSpace(narrowed[0].Content))
            {
                var freeMemos = _entries
                    .Select((e, idx) => (EntryIndex: idx, Entry: e))
                    .Where(x => string.Equals(x.Entry.Category, "메모", StringComparison.OrdinalIgnoreCase))
                    .Select(x => (x.EntryIndex, string.IsNullOrWhiteSpace(x.Entry.Content) ? x.Entry.MemoNote : x.Entry.Content))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Item2))
                    .ToList();
                var consumed = new HashSet<int>();
                int nextIdx = 0;
                return ConsultationMemoService.ReplacePlaceholders(narrowed[0].Content, freeMemos, consumed, ref nextIdx);
            }
        }

        return ConsultationMemoService.ComposeFinalText(_entries, GetSelectedJoinMode());
    }

    public void RefreshHud()
    {
        MemoCountChanged?.Invoke(_entries.Count);

        if (!_configManager.Config.EnableConsultationMemo || _entries.Count == 0)
        {
            Hide();
            return;
        }

        TxtMemoHeader.Text = $"📝 상담 스무고개 메모 ({_entries.Count}개 누적)";
        ChipsPanel.Children.Clear();
        MemoCardsPanel.Children.Clear();

        int freeMemoOrder = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            int idx = i;
            var entry = _entries[i];
            bool isFreeMemo = string.Equals(entry.Category, "메모", StringComparison.OrdinalIgnoreCase);
            if (isFreeMemo) freeMemoOrder++;

            // 상단 키워드 칩
            var border = new Border
            {
                Background = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"))),
                BorderBrush = (Brush)(TryFindResource("ThemeAccentBorder") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD"))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 3, 6, 3),
                Margin = new Thickness(0, 0, 6, 4)
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = $"{idx + 1}. {entry.Keyword}",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                VerticalAlignment = VerticalAlignment.Center
            });

            var delBtn = new Button
            {
                Content = "✕",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            delBtn.Click += (s, e) =>
            {
                if (idx >= 0 && idx < _entries.Count)
                {
                    _entries.RemoveAt(idx);
                    _isPreviewEdited = false;
                    RefreshHud();
                }
            };
            stack.Children.Add(delBtn);

            border.Child = stack;
            ChipsPanel.Children.Add(border);

            // 중앙 메모 카드 (깔끔한 카드 목록 + ▲/▼ 순서 이동)
            var card = new Border
            {
                Background = (Brush)(TryFindResource("ThemeBg") ?? Brushes.White),
                BorderBrush = (Brush)(TryFindResource("ThemePanelBorder") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var numBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            numBadge.Child = new TextBlock { Text = (idx + 1).ToString(), Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold };
            Grid.SetColumn(numBadge, 0);
            headerGrid.Children.Add(numBadge);

            var kwBadge = new Border
            {
                Background = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"))),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            kwBadge.Child = new TextBlock
            {
                Text = isFreeMemo ? $"{{{freeMemoOrder}}} {entry.Keyword}" : entry.Keyword,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(kwBadge, 1);
            headerGrid.Children.Add(kwBadge);

            var titleText = new TextBlock
            {
                Text = entry.MemoNote,
                Foreground = (Brush)(TryFindResource("ThemeText") ?? Brushes.Black),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleText, 2);
            headerGrid.Children.Add(titleText);

            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var btnUp = new Button
            {
                Content = "▲",
                Padding = new Thickness(5, 1, 5, 1),
                FontSize = 10,
                Margin = new Thickness(0, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false,
                IsEnabled = idx > 0,
                ToolTip = "순서 위로 이동"
            };
            btnUp.Click += (s, e) =>
            {
                if (idx > 0)
                {
                    (_entries[idx - 1], _entries[idx]) = (_entries[idx], _entries[idx - 1]);
                    _isPreviewEdited = false;
                    RefreshHud();
                }
            };
            var btnDown = new Button
            {
                Content = "▼",
                Padding = new Thickness(5, 1, 5, 1),
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false,
                IsEnabled = idx < _entries.Count - 1,
                ToolTip = "순서 아래로 이동"
            };
            btnDown.Click += (s, e) =>
            {
                if (idx < _entries.Count - 1)
                {
                    (_entries[idx + 1], _entries[idx]) = (_entries[idx], _entries[idx + 1]);
                    _isPreviewEdited = false;
                    RefreshHud();
                }
            };
            var cardDelBtn = new Button
            {
                Content = "삭제",
                Padding = new Thickness(6, 1, 6, 1),
                FontSize = 10.5,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C")),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            cardDelBtn.Click += (s, e) =>
            {
                if (idx >= 0 && idx < _entries.Count)
                {
                    _entries.RemoveAt(idx);
                    _isPreviewEdited = false;
                    RefreshHud();
                }
            };
            actionPanel.Children.Add(btnUp);
            actionPanel.Children.Add(btnDown);
            actionPanel.Children.Add(cardDelBtn);
            Grid.SetColumn(actionPanel, 3);
            headerGrid.Children.Add(actionPanel);

            Grid.SetRow(headerGrid, 0);
            cardGrid.Children.Add(headerGrid);

            var contentPreview = new TextBlock
            {
                Text = entry.Content,
                Foreground = (Brush)(TryFindResource("ThemeSubText") ?? Brushes.Gray),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(contentPreview, 1);
            cardGrid.Children.Add(contentPreview);

            card.Child = cardGrid;
            MemoCardsPanel.Children.Add(card);
        }

        var chips = _entries.Select(e => e.Keyword).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        var narrowed = chips.Count >= 2
            ? ConsultationMemoService.FilterTemplates(_templateManager.Templates, chips, null)
            : new List<TemplateItem>();

        string finalPreviewText;
        if (narrowed.Count == 1)
        {
            TxtMatchStatus.Text = $"🎯 스무고개 일치: [{narrowed[0].Keyword}] {narrowed[0].Title} (직접 수정 가능)";
            finalPreviewText = BuildFinalText();
        }
        else
        {
            TxtMatchStatus.Text = narrowed.Count > 1
                ? $"🔍 스무고개 후보 {narrowed.Count}건 남음 · 누적 {_entries.Count}개 조합 (직접 수정 가능)"
                : $"✨ 누적 {_entries.Count}개 답변 조합 (직접 수정 가능)";
            finalPreviewText = ConsultationMemoService.ComposeFinalText(_entries, GetSelectedJoinMode());
        }

        if (!_isPreviewEdited)
        {
            _suppressPreviewSync = true;
            TxtFinalPreview.Text = finalPreviewText;
            _suppressPreviewSync = false;
        }

        UpdateLayout();
        if (!_hasCustomPosition)
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left + 12, workArea.Right - Width - 20);
            Top = Math.Max(workArea.Top + 12, workArea.Bottom - Height - 72);
        }

        ApplyOpacity();
        if (!IsVisible)
            Show();
    }

    public void TriggerFinalInsert(IntPtr targetHwnd, int backspaceCount = 0)
    {
        if (!_configManager.Config.EnableConsultationMemo || _entries.Count == 0) return;

        string finalText = !string.IsNullOrWhiteSpace(TxtFinalPreview.Text)
            ? TxtFinalPreview.Text.Trim()
            : BuildFinalText();
        if (string.IsNullOrWhiteSpace(finalText)) return;

        bool wasEdited = _isPreviewEdited;
        IntPtr dst = Win32Api.IsValidTargetWindow(targetHwnd) ? targetHwnd : _targetHwnd;
        ClearMemos();
        FinalInsertRequested?.Invoke(finalText, dst, backspaceCount);

        if (wasEdited)
        {
            var answer = MessageBox.Show(
                "수정하신 최종 답변을 Google 시트(채팅템플릿)에도 새 템플릿으로 등록할까요?",
                "수정된 답변 시트 등록",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                RegisterSheetRequested?.Invoke(finalText);
            }
        }
    }

    private void TxtFinalPreview_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        Win32Api.ForceForegroundWindow(helper.Handle);
        Activate();
        TxtFinalPreview.Focus();
    }

    private void TxtFinalPreview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPreviewSync) return;
        _isPreviewEdited = true;
    }

    private void BtnPasteClipboardMemo_Click(object sender, RoutedEventArgs e)
    {
        string clip = TextInjectionService.GetClipboardTextSafe().Trim();
        if (string.IsNullOrWhiteSpace(clip)) return;
        AddMemo(null, clip, _targetHwnd);
    }

    private void BtnRegisterSheet_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtFinalPreview.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        RegisterSheetRequested?.Invoke(text);
    }

    private void BtnInsertFinal_Click(object sender, RoutedEventArgs e) => TriggerFinalInsert(_targetHwnd, 0);

    private void BtnOpenDetail_Click(object sender, RoutedEventArgs e) => OpenDetailRequested?.Invoke(_targetHwnd);

    private void BtnClearMemos_Click(object sender, RoutedEventArgs e) => ClearMemos();

    private void BtnHideHud_Click(object sender, RoutedEventArgs e) => Hide();

    private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !(e.OriginalSource is TextBox))
        {
            _hasCustomPosition = true;
            try { DragMove(); } catch { }
        }
    }
}
