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
/// 채팅창 포커스를 절대 빼앗지 않는(WS_EX_NOACTIVATE) 별도의 비방해형 상담 스무고개 메모 HUD.
/// 메모가 0개이거나 기능이 OFF이면 숨겨지며, 1개 이상 누적되면 대화를 이어가는 동안 계속 유지됩니다.
/// </summary>
public partial class ConsultationMemoHud : Window
{
    private readonly TemplateManager _templateManager;
    private readonly ConfigManager _configManager;
    private readonly List<MemoEntry> _entries = new();
    private IntPtr _targetHwnd = IntPtr.Zero;
    private bool _hasCustomPosition = false;

    public event Action<string, IntPtr, int>? FinalInsertRequested;
    public event Action<IntPtr>? OpenDetailRequested;
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

        RefreshHud();
    }

    public void ReplaceEntries(IEnumerable<MemoEntry> entries)
    {
        _entries.Clear();
        if (entries != null)
            _entries.AddRange(entries);
        RefreshHud();
    }

    public void ClearMemos()
    {
        _entries.Clear();
        RefreshHud();
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
                return narrowed[0].Content;
            }
        }

        return ConsultationMemoService.ComposeFinalText(_entries, MemoJoinMode.Paragraph);
    }

    public void RefreshHud()
    {
        MemoCountChanged?.Invoke(_entries.Count);

        if (!_configManager.Config.EnableConsultationMemo || _entries.Count == 0)
        {
            Hide();
            return;
        }

        TxtMemoHeader.Text = $"📝 상담 메모 ({_entries.Count}개 누적)";
        ChipsPanel.Children.Clear();

        for (int i = 0; i < _entries.Count; i++)
        {
            int idx = i;
            var entry = _entries[i];

            var border = new Border
            {
                Background = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"))),
                BorderBrush = (Brush)(TryFindResource("ThemeAccentBorder") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD"))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 2, 5, 2),
                Margin = new Thickness(0, 0, 5, 3)
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = $"{idx + 1}. {entry.Keyword}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                VerticalAlignment = VerticalAlignment.Center
            });

            var delBtn = new Button
            {
                Content = "✕",
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 10,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            delBtn.Click += (s, e) =>
            {
                if (idx >= 0 && idx < _entries.Count)
                {
                    _entries.RemoveAt(idx);
                    RefreshHud();
                }
            };
            stack.Children.Add(delBtn);

            border.Child = stack;
            ChipsPanel.Children.Add(border);
        }

        var chips = _entries.Select(e => e.Keyword).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        var narrowed = chips.Count >= 2
            ? ConsultationMemoService.FilterTemplates(_templateManager.Templates, chips, null)
            : new List<TemplateItem>();

        if (narrowed.Count == 1)
        {
            TxtMatchStatus.Text = $"🎯 스무고개 일치 템플릿 발견: [{narrowed[0].Keyword}] {narrowed[0].Title}";
            TxtFinalPreview.Text = narrowed[0].Content;
        }
        else
        {
            TxtMatchStatus.Text = narrowed.Count > 1
                ? $"🔍 스무고개 후보 {narrowed.Count}건 남음 · 누적 {_entries.Count}개 조합 미리보기"
                : $"✨ 누적 {_entries.Count}개 답변 조합 미리보기";
            TxtFinalPreview.Text = ConsultationMemoService.ComposeFinalText(_entries, MemoJoinMode.Paragraph);
        }

        UpdateLayout();
        if (!_hasCustomPosition)
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left + 12, workArea.Right - ActualWidth - 20);
            Top = Math.Max(workArea.Top + 12, workArea.Bottom - ActualHeight - 72);
        }

        ApplyOpacity();
        if (!IsVisible)
            Show();
    }

    public void TriggerFinalInsert(IntPtr targetHwnd, int backspaceCount = 0)
    {
        if (!_configManager.Config.EnableConsultationMemo || _entries.Count == 0) return;

        string finalText = BuildFinalText();
        if (string.IsNullOrWhiteSpace(finalText)) return;

        IntPtr dst = Win32Api.IsValidTargetWindow(targetHwnd) ? targetHwnd : _targetHwnd;
        ClearMemos();
        FinalInsertRequested?.Invoke(finalText, dst, backspaceCount);
    }

    private void BtnInsertFinal_Click(object sender, RoutedEventArgs e) => TriggerFinalInsert(_targetHwnd, 0);

    private void BtnOpenDetail_Click(object sender, RoutedEventArgs e) => OpenDetailRequested?.Invoke(_targetHwnd);

    private void BtnClearMemos_Click(object sender, RoutedEventArgs e) => ClearMemos();

    private void BtnHideHud_Click(object sender, RoutedEventArgs e) => Hide();

    private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _hasCustomPosition = true;
            try { DragMove(); } catch { }
        }
    }
}
