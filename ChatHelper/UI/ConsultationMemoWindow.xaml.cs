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

public partial class ConsultationMemoWindow : Window
{
    private readonly TemplateManager _templateManager;
    private readonly ConfigManager _configManager;
    private IntPtr _targetHwnd = IntPtr.Zero;
    private readonly List<string> _keywordChips = new();
    private readonly List<MemoEntry> _memoEntries = new();
    private readonly Dictionary<string, string> _variableValues = new(StringComparer.OrdinalIgnoreCase);
    private List<MemoEntry> _externalStickyMemos = new();
    private bool _suppressPreviewRebuild = false;
    private bool _navigatedCandidateList = false;
    private bool _hasCustomPosition = false;

    public void SetExternalStickyMemos(IReadOnlyList<MemoEntry>? stickyMemos)
    {
        _externalStickyMemos = stickyMemos != null ? new List<MemoEntry>(stickyMemos) : new List<MemoEntry>();
        RebuildFinalPreview();
    }

    public void MarkCustomPosition() => _hasCustomPosition = true;

    public void UpdateDockState(bool docked)
    {
        if (BtnDockSticky != null)
        {
            BtnDockSticky.Content = docked ? "🔓 임시메모 분리" : "🔗 임시메모 붙이기";
            BtnDockSticky.ToolTip = docked ? "챗헬프 임시메모 창을 분리합니다" : "챗헬프 임시메모 창을 옆에 자석처럼 붙입니다";
        }
    }

    public event Action<string, IntPtr>? FinalTextReady;
    public event Action<int>? MemoCountChanged;
    public event Action? DockStickyRequested;
    public event Action<IReadOnlyList<string>>? StickyMemosConsumed;

    public int MemoCount => _memoEntries.Count;
    public IReadOnlyList<MemoEntry> Entries => _memoEntries;

    public void ApplyOpacity()
    {
        double winAlpha = Math.Clamp(_configManager.Config.MemoOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);
        Win32Api.SetWindowAlpha(this, winAlpha);
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

        _memoEntries.Add(new MemoEntry
        {
            Keyword = kw,
            Category = candidate?.Category ?? "메모",
            MemoNote = candidate?.Title ?? kw,
            Content = content
        });

        RenderMemoEntries();
    }

    public void ClearMemos()
    {
        _memoEntries.Clear();
        _variableValues.Clear();
        _keywordChips.Clear();
        TxtKeywordInput.Clear();
        RenderKeywordChips();
        RenderMemoEntries();
    }

    public void TriggerFinalInsert(IntPtr targetHwnd, int backspaceCount = 0)
    {
        if (!_configManager.Config.EnableConsultationMemo || _memoEntries.Count == 0) return;

        string composed = ConsultationMemoService.ComposeFinalText(_memoEntries, GetSelectedJoinMode());
        string finalText = !string.IsNullOrWhiteSpace(TxtFinalPreview.Text) && IsVisible
            ? TxtFinalPreview.Text.Trim()
            : ApplyVariableValues(composed).Trim();
        if (string.IsNullOrWhiteSpace(finalText)) return;

        IntPtr dst = Win32Api.IsValidTargetWindow(targetHwnd) ? targetHwnd : _targetHwnd;
        ConsumeStickyMemosAndAdvance(composed);

        if (ChkClearAfterInsert.IsChecked == true)
            ClearMemos();
        else
            RebuildFinalPreview();

        if (IsVisible)
            FlashBorderHighlight();

        if (backspaceCount > 0)
        {
            Task.Run(() => TextInjectionService.ReplaceText(backspaceCount, finalText, dst, useShiftHome: false));
        }
        else
        {
            FinalTextReady?.Invoke(finalText, dst);
        }
    }

    private void ConsumeStickyMemosAndAdvance(string composedText)
    {
        var usedValues = new List<string>();
        var vars = ConsultationMemoService.ExtractPlaceholderNames(composedText);
        foreach (var v in vars)
        {
            if (_variableValues.TryGetValue(v, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                usedValues.Add(val.Trim());
            }
        }

        // 수동 입력값 초기화하여 다음 임시메모가 자동으로 채워지도록 준비
        _variableValues.Clear();

        if (ChkConsumeStickyMemos?.IsChecked != false && usedValues.Count > 0)
        {
            StickyMemosConsumed?.Invoke(usedValues);
        }
    }

    public void SyncEntriesFromHud(IEnumerable<MemoEntry> entries)
    {
        _memoEntries.Clear();
        if (entries != null)
        {
            foreach (var e in entries)
            {
                _memoEntries.Add(new MemoEntry
                {
                    Keyword = e.Keyword,
                    Category = e.Category,
                    MemoNote = e.MemoNote,
                    Content = e.Content
                });
            }
        }
        RenderMemoEntries();
    }

    public ConsultationMemoWindow(TemplateManager templateManager, ConfigManager configManager)
    {
        InitializeComponent();
        _templateManager = templateManager;
        _configManager = configManager;

        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }

        Loaded += (_, _) => Win32Api.EnforceTopmost(this, toolWindow: true, noActivate: false);
        Deactivated += (_, _) => Win32Api.EnforceTopmost(this, toolWindow: true, noActivate: false);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void ShowAtCenter(IntPtr targetHwnd)
    {
        if (Win32Api.IsValidTargetWindow(targetHwnd))
        {
            _targetHwnd = targetHwnd;
        }
        else if (_targetHwnd == IntPtr.Zero && Win32Api.TryGetValidInputPosition(out _, out _, out var resolvedHwnd, false))
        {
            if (Win32Api.IsValidTargetWindow(resolvedHwnd))
                _targetHwnd = resolvedHwnd;
        }

        // B안: 이미 열려 있는 상태에서 재호출 시 위치 이동·포커스 강탈 없이 제자리 유지 + 테두리 강조만 표시
        if (IsVisible)
        {
            RefreshCandidates();
            RenderMemoEntries();
            Win32Api.EnforceTopmost(this, toolWindow: true, noActivate: true);
            FlashBorderHighlight();
            return;
        }

        if (!_hasCustomPosition)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2.0;
            Top = workArea.Top + (workArea.Height - Height) / 2.0;
        }

        RefreshCandidates();
        RenderMemoEntries();

        double winAlpha = Math.Clamp(_configManager.Config.MemoOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);
        Win32Api.SetWindowAlpha(this, winAlpha);

        ShowActivated = true;
        Topmost = true;
        Show();
        WindowState = WindowState.Normal;
        Win32Api.EnforceTopmost(this, toolWindow: true, noActivate: false);
        Win32Api.SetWindowAlpha(this, winAlpha);

        var helper = new WindowInteropHelper(this);
        Win32Api.ForceForegroundWindow(helper.Handle);
        Activate();
        TxtKeywordInput.Focus();
        Keyboard.Focus(TxtKeywordInput);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (IsVisible)
            {
                Win32Api.ForceForegroundWindow(helper.Handle);
                Activate();
                TxtKeywordInput.Focus();
                Keyboard.Focus(TxtKeywordInput);
            }
        });
    }

    private void FlashBorderHighlight()
    {
        if (MainWindowBorder == null) return;
        var origBrush = MainWindowBorder.BorderBrush;
        var origThick = MainWindowBorder.BorderThickness;
        MainWindowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
        MainWindowBorder.BorderThickness = new Thickness(2.5);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            MainWindowBorder.BorderBrush = origBrush;
            MainWindowBorder.BorderThickness = origThick;
        };
        timer.Start();
    }

    private void RefreshCandidates()
    {
        _navigatedCandidateList = false;
        string input = TxtKeywordInput.Text?.Trim() ?? string.Empty;
        var filtered = ConsultationMemoService.FilterTemplates(_templateManager.Templates, _keywordChips, input);
        LstCandidates.ItemsSource = filtered;
        if (filtered.Count > 0)
            LstCandidates.SelectedIndex = 0;

        if (_keywordChips.Count == 0 && string.IsNullOrEmpty(input))
            TxtCandidateHint.Text = $"🔍 키워드 · 제목 · 본문 문장으로 검색해 후보를 좁히세요 (전체 {filtered.Count}건)";
        else
            TxtCandidateHint.Text = $"🎯 검색 조건 [{string.Join(" + ", _keywordChips.Concat(string.IsNullOrEmpty(input) ? Array.Empty<string>() : new[] { input }))}] → {filtered.Count}건 일치";
    }

    private void RenderKeywordChips()
    {
        KeywordChipsPanel.Children.Clear();
        KeywordChipsPanel.Visibility = _keywordChips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < _keywordChips.Count; i++)
        {
            int idx = i;
            string chipText = _keywordChips[i];

            var chipBorder = new Border
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
                Text = $"🏷 {chipText}",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeBtn = new Button
            {
                Content = "✕",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(3, 0, 3, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            removeBtn.Click += (s, e) =>
            {
                if (idx >= 0 && idx < _keywordChips.Count)
                {
                    _keywordChips.RemoveAt(idx);
                    RenderKeywordChips();
                    RefreshCandidates();
                }
                TxtKeywordInput.Focus();
            };
            stack.Children.Add(removeBtn);

            chipBorder.Child = stack;
            KeywordChipsPanel.Children.Add(chipBorder);
        }
    }

    private void RenderMemoEntries()
    {
        MemoCountChanged?.Invoke(_memoEntries.Count);
        MemoEntriesPanel.Children.Clear();
        TxtMemoCount.Text = $"📌 달아둔 상담 메모 ({_memoEntries.Count}개)";

        if (_memoEntries.Count == 0)
        {
            MemoEntriesPanel.Children.Add(new TextBlock
            {
                Text = "아직 담아둔 답변이 없습니다.\n좌측 목록에서 [+ 담기]를 누르거나\n검색창 문구를 그대로 추가해 보세요.",
                Foreground = (Brush)(TryFindResource("ThemeSubText") ?? Brushes.Gray),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(12, 48, 12, 12),
                TextWrapping = TextWrapping.Wrap
            });
            RebuildFinalPreview();
            return;
        }

        for (int i = 0; i < _memoEntries.Count; i++)
        {
            int idx = i;
            var entry = _memoEntries[i];

            var card = new Border
            {
                Background = (Brush)(TryFindResource("ThemeBg") ?? Brushes.White),
                BorderBrush = (Brush)(TryFindResource("ThemePanelBorder") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stack = new StackPanel();

            // 상단 행: 번호 + 키워드 배지 + 한줄 메모 + 이동/삭제 버튼
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var numBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            numBadge.Child = new TextBlock
            {
                Text = (idx + 1).ToString(),
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(numBadge, 0);
            topGrid.Children.Add(numBadge);

            var kwBadge = new Border
            {
                Background = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"))),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            kwBadge.Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Keyword) ? "메모" : entry.Keyword,
                Foreground = (Brush)(TryFindResource("ThemeAccentText") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"))),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(kwBadge, 1);
            topGrid.Children.Add(kwBadge);

            var txtMemoNote = new TextBox
            {
                Text = entry.MemoNote,
                FontSize = 11.5,
                Foreground = (Brush)(TryFindResource("ThemeText") ?? Brushes.Black),
                Background = (Brush)(TryFindResource("ThemeSurfaceBg") ?? Brushes.White),
                BorderBrush = (Brush)(TryFindResource("ThemePanelBorder") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "상담 질문/상황 메모 (수정 가능)"
            };
            txtMemoNote.TextChanged += (s, e) =>
            {
                entry.MemoNote = txtMemoNote.Text;
                if (string.IsNullOrWhiteSpace(entry.Content))
                    RebuildFinalPreview();
            };
            Grid.SetColumn(txtMemoNote, 2);
            topGrid.Children.Add(txtMemoNote);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
            var btnUp = new Button { Content = "▲", Padding = new Thickness(4, 1, 4, 1), FontSize = 9.5, Margin = new Thickness(0, 0, 2, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, IsEnabled = idx > 0 };
            btnUp.Click += (s, e) =>
            {
                (_memoEntries[idx - 1], _memoEntries[idx]) = (_memoEntries[idx], _memoEntries[idx - 1]);
                RenderMemoEntries();
            };
            var btnDown = new Button { Content = "▼", Padding = new Thickness(4, 1, 4, 1), FontSize = 9.5, Margin = new Thickness(0, 0, 2, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, IsEnabled = idx < _memoEntries.Count - 1 };
            btnDown.Click += (s, e) =>
            {
                (_memoEntries[idx + 1], _memoEntries[idx]) = (_memoEntries[idx], _memoEntries[idx + 1]);
                RenderMemoEntries();
            };
            var btnDel = new Button { Content = "✕", Padding = new Thickness(5, 1, 5, 1), FontSize = 10.5, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnDel.Click += (s, e) =>
            {
                _memoEntries.RemoveAt(idx);
                RenderMemoEntries();
            };
            btnPanel.Children.Add(btnUp);
            btnPanel.Children.Add(btnDown);
            btnPanel.Children.Add(btnDel);
            Grid.SetColumn(btnPanel, 3);
            topGrid.Children.Add(btnPanel);

            stack.Children.Add(topGrid);

            // 본문 편집 박스
            var txtContent = new TextBox
            {
                Text = entry.Content,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 76,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 11.5,
                Foreground = (Brush)(TryFindResource("ThemeText") ?? Brushes.Black),
                Background = (Brush)(TryFindResource("ThemeSurfaceBg") ?? Brushes.White),
                BorderBrush = (Brush)(TryFindResource("ThemePanelBorder") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4)
            };
            txtContent.TextChanged += (s, e) =>
            {
                entry.Content = txtContent.Text;
                RebuildFinalPreview();
            };
            stack.Children.Add(txtContent);

            card.Child = stack;
            MemoEntriesPanel.Children.Add(card);
        }

        RebuildFinalPreview();
    }

    private MemoJoinMode GetSelectedJoinMode()
    {
        if (CmbJoinMode.SelectedItem is ComboBoxItem item && item.Tag is string tag)
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

    private void RebuildFinalPreview()
    {
        if (_suppressPreviewRebuild) return;
        string composed = ConsultationMemoService.ComposeFinalText(_memoEntries, GetSelectedJoinMode());
        RebuildVariableInputs(composed);
        TxtFinalPreview.Text = ApplyVariableValues(composed);
    }

    private string ApplyVariableValues(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        foreach (var kv in _variableValues)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                text = text.Replace($"{{{kv.Key}}}", kv.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        if (_externalStickyMemos.Count > 0 && ConsultationMemoService.ContainsPlaceholders(text))
        {
            text = ConsultationMemoService.ResolveTemplateWithMemosOrClipboard(text, _externalStickyMemos, null);
        }
        return text;
    }

    private void RebuildVariableInputs(string composedText)
    {
        if (VariableInputsBar == null || VariableInputsPanel == null) return;

        var vars = ConsultationMemoService.ExtractPlaceholderNames(composedText);
        VariableInputsPanel.Children.Clear();
        if (vars.Count == 0)
        {
            VariableInputsBar.Visibility = Visibility.Collapsed;
            return;
        }

        VariableInputsBar.Visibility = Visibility.Visible;
        var autoAssignedStickyIndices = new HashSet<int>();
        foreach (var varName in vars)
        {
            string key = varName;
            if (!_variableValues.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing))
            {
                string? autoVal = FindMatchingExternalStickyValue(key, autoAssignedStickyIndices);
                if (!string.IsNullOrWhiteSpace(autoVal))
                {
                    existing = autoVal;
                    _variableValues[key] = autoVal;
                }
            }

            var boxStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4), VerticalAlignment = VerticalAlignment.Center };
            boxStack.Children.Add(new TextBlock
            {
                Text = $"{ConsultationMemoService.GetFriendlyVariableLabel(key)}:",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });

            var tb = new TextBox
            {
                Width = 140,
                Height = 26,
                FontSize = 11.5,
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = existing ?? string.Empty,
                Tag = key
            };
            tb.TextChanged += (s, e) =>
            {
                _variableValues[key] = tb.Text;
                TxtFinalPreview.Text = ApplyVariableValues(ConsultationMemoService.ComposeFinalText(_memoEntries, GetSelectedJoinMode()));
            };
            boxStack.Children.Add(tb);

            var clipBtn = new Button
            {
                Content = "📋",
                Width = 26,
                Height = 26,
                Margin = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = $"클립보드(텍스트/이미지 OCR) 내용을 {key} 칸에 채우기"
            };
            clipBtn.Click += async (s, e) =>
            {
                var res = await ClipboardSmartService.GetCustomerMessageAsync();
                string clip = ConsultationMemoService.SanitizeClipboardForVariable(key, res.ExtractedText);
                if (!string.IsNullOrWhiteSpace(clip))
                {
                    tb.Text = clip;
                }
            };
            boxStack.Children.Add(clipBtn);

            // 날짜/일정 변수인 경우 오늘·내일·금일 오후·내일 오전 원클릭 프리셋 제공
            bool isDateOrTimeVar = key.Contains("날짜", StringComparison.OrdinalIgnoreCase)
                || key.Contains("일정", StringComparison.OrdinalIgnoreCase)
                || key.Contains("예약", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("일", StringComparison.OrdinalIgnoreCase)
                || key.Contains("date", StringComparison.OrdinalIgnoreCase);
            if (isDateOrTimeVar)
            {
                var ko = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");
                var presets = new (string Label, string Value)[]
                {
                    ("📅 오늘", DateTime.Today.ToString("M월 d일 (ddd)", ko)),
                    ("📅 내일", DateTime.Today.AddDays(1).ToString("M월 d일 (ddd)", ko)),
                    ("🕒 금일 오후", $"{DateTime.Today.ToString("M월 d일 (ddd)", ko)} 오후"),
                    ("🕒 내일 오전", $"{DateTime.Today.AddDays(1).ToString("M월 d일 (ddd)", ko)} 오전")
                };
                foreach (var (label, val) in presets)
                {
                    var pBtn = new Button
                    {
                        Content = label,
                        Height = 24,
                        Margin = new Thickness(4, 0, 0, 0),
                        Padding = new Thickness(7, 0, 7, 0),
                        FontSize = 10.5,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD")),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        Focusable = false,
                        ToolTip = $"클릭하여 '{val}' 입력"
                    };
                    pBtn.Click += (s, e) => tb.Text = val;
                    boxStack.Children.Add(pBtn);
                }
            }

            // 비개발자 친화형 원클릭 임시메모 선택 알약
            for (int mIdx = 0; mIdx < Math.Min(_externalStickyMemos.Count, 4); mIdx++)
            {
                string memoVal = _externalStickyMemos[mIdx].Content?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(memoVal) || memoVal.Contains('\n')) continue;
                string shortVal = memoVal.Length > 12 ? memoVal[..12] + "…" : memoVal;
                var memoPill = new Button
                {
                    Content = shortVal,
                    Height = 24,
                    Margin = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(7, 0, 7, 0),
                    FontSize = 10.5,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    ToolTip = $"클릭하여 '{memoVal}' 값을 {key} 칸에 바로 넣기"
                };
                memoPill.Click += (s, e) => tb.Text = memoVal;
                boxStack.Children.Add(memoPill);
            }

            VariableInputsPanel.Children.Add(boxStack);
        }
    }

    private string? FindMatchingExternalStickyValue(string varName, HashSet<int> usedIndices)
    {
        if (_externalStickyMemos.Count == 0) return null;

        string targetKind = (varName.Contains("송장") || varName.Contains("운송장") || varName.Contains("번호")) ? "송장"
            : (varName.Contains("날짜") || varName.Contains("일정") || varName.Contains("시간") || varName.EndsWith("일")) ? "날짜"
            : (varName.Contains("금액") || varName.Contains("비용") || varName.Contains("가격")) ? "금액"
            : string.Empty;

        if (!string.IsNullOrEmpty(targetKind))
        {
            for (int i = 0; i < _externalStickyMemos.Count; i++)
            {
                if (usedIndices.Contains(i)) continue;
                var m = _externalStickyMemos[i];
                string val = m.Content?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(val)) continue;
                if (string.Equals(ConsultationMemoService.ResolveMemoKind(m), targetKind, StringComparison.Ordinal))
                {
                    usedIndices.Add(i);
                    return val;
                }
            }
        }

        for (int i = 0; i < _externalStickyMemos.Count; i++)
        {
            if (usedIndices.Contains(i)) continue;
            string val = _externalStickyMemos[i].Content?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(val))
            {
                usedIndices.Add(i);
                return val;
            }
        }

        return null;
    }

    private void AddCandidateToMemo(TemplateItem item)
    {
        if (item == null) return;
        string memoLabel = _keywordChips.Count > 0
            ? $"{string.Join("+", _keywordChips)} · {item.Title}"
            : item.Title;

        _memoEntries.Add(new MemoEntry
        {
            Keyword = string.IsNullOrWhiteSpace(item.Keyword) ? item.Title : item.Keyword,
            Category = item.Category,
            MemoNote = memoLabel,
            Content = item.Content
        });
        RenderMemoEntries();
    }

    private void AddCurrentKeywordChip()
    {
        string text = TxtKeywordInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!_keywordChips.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            _keywordChips.Add(text);
        }
        TxtKeywordInput.Clear();
        RenderKeywordChips();
        RefreshCandidates();
        TxtKeywordInput.Focus();
    }

    private void TxtKeywordInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        BtnClearInput.Visibility = string.IsNullOrEmpty(TxtKeywordInput.Text) ? Visibility.Collapsed : Visibility.Visible;
        RefreshCandidates();
    }

    private void BtnClearInput_Click(object sender, RoutedEventArgs e)
    {
        TxtKeywordInput.Clear();
        TxtKeywordInput.Focus();
    }

    private void BtnAddKeywordChip_Click(object sender, RoutedEventArgs e) => AddCurrentKeywordChip();

    private void BtnResetChips_Click(object sender, RoutedEventArgs e)
    {
        _keywordChips.Clear();
        TxtKeywordInput.Clear();
        RenderKeywordChips();
        RefreshCandidates();
        TxtKeywordInput.Focus();
    }

    private void BtnAddCandidateToMemo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TemplateItem item)
        {
            AddCandidateToMemo(item);
        }
    }

    private void LstCandidates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstCandidates.SelectedItem is TemplateItem item)
        {
            AddCandidateToMemo(item);
        }
    }

    private void BtnAddCustomMemo_Click(object sender, RoutedEventArgs e)
    {
        string input = TxtKeywordInput.Text?.Trim() ?? string.Empty;
        string chips = string.Join(", ", _keywordChips);
        string note = !string.IsNullOrWhiteSpace(input) ? input : (string.IsNullOrWhiteSpace(chips) ? "상담 메모" : chips);

        _memoEntries.Add(new MemoEntry
        {
            Keyword = string.IsNullOrWhiteSpace(chips) ? "자유메모" : chips,
            Category = "메모",
            MemoNote = note,
            Content = input
        });
        TxtKeywordInput.Clear();
        RenderMemoEntries();
        TxtKeywordInput.Focus();
    }

    private void BtnClearMemos_Click(object sender, RoutedEventArgs e)
    {
        _memoEntries.Clear();
        RenderMemoEntries();
        TxtKeywordInput.Focus();
    }

    private void CmbJoinMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RebuildFinalPreview();
    }

    private void BtnRestorePreview_Click(object sender, RoutedEventArgs e)
    {
        RebuildFinalPreview();
    }

    private void BtnClearPreview_Click(object sender, RoutedEventArgs e)
    {
        TxtFinalPreview.Clear();
        TxtFinalPreview.Focus();
    }

    private void BtnCopyFinal_Click(object sender, RoutedEventArgs e)
    {
        string finalText = TxtFinalPreview.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(finalText)) return;
        try { Clipboard.SetText(finalText); } catch { }
    }

    private void BtnInsertFinal_Click(object sender, RoutedEventArgs e)
    {
        ExecuteFinalInsert();
    }

    private void ExecuteFinalInsert()
    {
        string finalText = TxtFinalPreview.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(finalText)) return;

        IntPtr targetHwnd = _targetHwnd;
        string composed = ConsultationMemoService.ComposeFinalText(_memoEntries, GetSelectedJoinMode());
        ConsumeStickyMemosAndAdvance(composed);

        if (ChkClearAfterInsert.IsChecked == true)
        {
            _memoEntries.Clear();
            _keywordChips.Clear();
            TxtKeywordInput.Clear();
            RenderKeywordChips();
            RenderMemoEntries();
        }
        else
        {
            RebuildFinalPreview();
        }

        FlashBorderHighlight();
        FinalTextReady?.Invoke(finalText, targetHwnd);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var actualKey = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : (e.Key == Key.System ? e.SystemKey : e.Key);

        if (actualKey == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Down || actualKey == Key.Up)
        {
            if (LstCandidates.Items.Count > 0 && !TxtFinalPreview.IsFocused)
            {
                int idx = LstCandidates.SelectedIndex;
                if (actualKey == Key.Down)
                    idx = idx < LstCandidates.Items.Count - 1 ? idx + 1 : 0;
                else
                    idx = idx > 0 ? idx - 1 : LstCandidates.Items.Count - 1;

                LstCandidates.SelectedIndex = idx;
                LstCandidates.ScrollIntoView(LstCandidates.SelectedItem);
                _navigatedCandidateList = true;
                e.Handled = true;
                return;
            }
        }

        if (actualKey == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ExecuteFinalInsert();
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Enter && (_navigatedCandidateList || LstCandidates.IsKeyboardFocusWithin) && LstCandidates.SelectedItem is TemplateItem selectedItem)
        {
            AddCandidateToMemo(selectedItem);
            _navigatedCandidateList = false;
            TxtKeywordInput.Focus();
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Enter && TxtKeywordInput.IsFocused)
        {
            AddCurrentKeywordChip();
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Back && TxtKeywordInput.IsFocused && string.IsNullOrEmpty(TxtKeywordInput.Text) && _keywordChips.Count > 0)
        {
            _keywordChips.RemoveAt(_keywordChips.Count - 1);
            RenderKeywordChips();
            RefreshCandidates();
            e.Handled = true;
        }
    }

    private void BtnDockSticky_Click(object sender, RoutedEventArgs e) => DockStickyRequested?.Invoke();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
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

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
