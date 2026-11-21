using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ChatHelper.Constants;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class QuickSearchWindow : Window
{
    private readonly TemplateManager _templateManager;
    private readonly ConfigManager _configManager;
    private IntPtr _targetHwnd = IntPtr.Zero;
    private string _selectedCategory = "전체";
    private bool _isActionModalOpen = false;
    private DateTime _openedAt = DateTime.MinValue;

    public event Action<TemplateItem, IntPtr>? ItemSelected;

    public QuickSearchWindow(TemplateManager templateManager, ConfigManager configManager)
    {
        InitializeComponent();
        _templateManager = templateManager;
        _configManager = configManager;
        BuildQuickSymbolBar();

        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void ShowAtCursor(IntPtr targetHwnd)
    {
        _openedAt = DateTime.UtcNow;
        _targetHwnd = targetHwnd;

        // 1. 텍스트 입력 캐럿(Caret) 위치 확인 (알약 뜨는 위치 근처)
        double targetX, targetY;
        if (Win32Api.TryGetValidInputPosition(out double caretX, out double caretY, out _))
        {
            targetX = caretX;
            targetY = caretY;
        }
        else
        {
            // 캐럿 인식 실패 시 마우스 커서 위치 활용
            Win32Api.GetCursorPos(out var pt);
            targetX = pt.X;
            targetY = pt.Y;
        }

        double screenW = SystemParameters.PrimaryScreenWidth;
        double screenH = SystemParameters.PrimaryScreenHeight;

        // X 위치: 커서 인근에서 시작하되, 화면 너비를 벗어나지 않도록 보정
        double desiredX = targetX - 40;
        Left = Math.Max(10, Math.Min(desiredX, screenW - Width - 10));

        // Y 위치: 커서 아래 +30px 위치하되, 너무 하단에 치우치면 커서 상단으로 보정
        double desiredY = targetY + 30;
        if (desiredY + Height > screenH - 50)
        {
            // 하단 공간 부족 -> 커서 위쪽으로 보정
            desiredY = targetY - Height - 15;
        }
        
        // 너무 상단에 치우치면 상단 최소 여백 10px로 보정
        if (desiredY < 10)
        {
            desiredY = 10;
        }

        Top = desiredY;

        _selectedCategory = "전체";
        TxtSearch.Clear();
        BtnClearSearch.Visibility = Visibility.Collapsed;

        BuildCategoryChips();
        UpdateList();

        Opacity = Math.Clamp(_configManager.Config.WindowOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);

        Show();
        WindowState = WindowState.Normal;

        // 외부 창(Chrome/카톡/메모장)으로부터 AttachThreadInput 기반 강제 포커스 전환 -> 즉시 Tab/입력 가능
        var helper = new WindowInteropHelper(this);
        Win32Api.ForceForegroundWindow(helper.Handle);
        Activate();
        TxtSearch.Focus();
        Keyboard.Focus(TxtSearch);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (IsVisible)
            {
                Win32Api.ForceForegroundWindow(helper.Handle);
                Activate();
                TxtSearch.Focus();
                Keyboard.Focus(TxtSearch);
            }
        });
    }

    private void BuildQuickSymbolBar()
    {
        // 특수기호는 Alt+X 전용 창(SymbolPickerWindow)으로 분리됨
    }

    private const string RecentCategoryName = "⭐ 최근 사용";

    private List<string> GetDisplayCategories()
    {
        var cats = _templateManager.GetCategories();
        var recent = _configManager.Config.RecentKeywords;
        if (recent != null && recent.Count > 0)
        {
            int insertIdx = cats.Count > 0 && cats[0] == "전체" ? 1 : 0;
            cats.Insert(insertIdx, RecentCategoryName);
        }
        return cats;
    }

    private void BuildCategoryChips()
    {
        CategoryPanel.Children.Clear();
        var cats = GetDisplayCategories();

        foreach (var cat in cats)
        {
            bool isSelected = string.Equals(cat, _selectedCategory, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Content = isSelected ? $"✔ {cat}" : cat,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 11.5,
                FontFamily = new FontFamily("Tahoma, Gulim, Segoe UI"),
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                Cursor = Cursors.Hand,
                Background = isSelected 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000080")) // Win98 네이비
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")), // Win98 실버 그레이
                Foreground = isSelected ? Brushes.White : Brushes.Black,
                BorderBrush = isSelected 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000")) 
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")),
                BorderThickness = new Thickness(1),
                Focusable = false
            };

            btn.Click += (s, e) =>
            {
                _selectedCategory = cat;
                BuildCategoryChips();
                UpdateList();
                TxtSearch.Focus();
            };

            CategoryPanel.Children.Add(btn);

            if (isSelected)
            {
                btn.Loaded += (s, e) => btn.BringIntoView();
                if (btn.IsLoaded) btn.BringIntoView();
            }
        }
    }

    public void CycleCategory(bool forward)
    {
        var cats = GetDisplayCategories();
        if (cats.Count == 0) return;

        int idx = cats.FindIndex(c => string.Equals(c, _selectedCategory, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;

        if (forward)
        {
            idx = (idx + 1) % cats.Count;
        }
        else
        {
            idx = (idx - 1 + cats.Count) % cats.Count;
        }

        _selectedCategory = cats[idx];
        BuildCategoryChips();
        UpdateList();
        TxtSearch.Focus();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        BtnClearSearch.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdateList();
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Clear();
        TxtSearch.Focus();
    }

    private void UpdateList()
    {
        string query = TxtSearch.Text.Trim();
        var recent = _configManager.Config.RecentKeywords ?? new List<string>();
        List<TemplateItem> items;

        if (_selectedCategory == RecentCategoryName)
        {
            var all = _templateManager.Search(query, "전체");
            items = recent
                .Select(kw => all.FirstOrDefault(t => string.Equals(t.Keyword, kw, StringComparison.OrdinalIgnoreCase)))
                .Where(t => t != null)
                .Cast<TemplateItem>()
                .ToList();
        }
        else
        {
            items = _templateManager.Search(query, _selectedCategory);
            // 검색어가 없을 때는 최근 사용한 템플릿을 상단에 우선 배치
            if (string.IsNullOrWhiteSpace(query) && recent.Count > 0)
            {
                items = items
                    .OrderBy(t =>
                    {
                        int idx = recent.FindIndex(kw => string.Equals(kw, t.Keyword, StringComparison.OrdinalIgnoreCase));
                        return idx >= 0 ? idx : int.MaxValue;
                    })
                    .ToList();
            }
        }

        // [스마트 폴백] 선택된 특정 카테고리에 결과가 없는데, 전체에서 검색하면 결과가 있는 경우
        if (items.Count == 0 && _selectedCategory != "전체" && !string.IsNullOrWhiteSpace(query))
        {
            var fallbackItems = _templateManager.Search(query, "전체");
            if (fallbackItems.Count > 0)
            {
                TxtResultHint.Text = $"💡 '{_selectedCategory}' 카테고리에는 일치하는 항목이 없어 '전체' 검색 결과({fallbackItems.Count}건)를 표시합니다.";
                TxtResultHint.Foreground = (Brush)new BrushConverter().ConvertFromString("#2563EB")!;
                LstTemplates.ItemsSource = fallbackItems;
                LstTemplates.SelectedIndex = 0;
                return;
            }
        }

        LstTemplates.ItemsSource = items;

        if (items.Count > 0)
        {
            TxtResultHint.Text = string.IsNullOrWhiteSpace(query)
                ? $"'{_selectedCategory}' 카테고리 템플릿 총 {items.Count}개"
                : $"'{_selectedCategory}' 검색 결과 {items.Count}건 일치";
            TxtResultHint.Foreground = (Brush)FindResource("ThemeSubText");
            LstTemplates.SelectedIndex = 0;
        }
        else
        {
            TxtResultHint.Text = string.IsNullOrWhiteSpace(query)
                ? $"'{_selectedCategory}' 카테고리에 등록된 템플릿이 없습니다."
                : $"검색어 '{query}'에 일치하는 템플릿이 없습니다. [➕ 새 템플릿 등록]으로 바로 추가해 보세요!";
            TxtResultHint.Foreground = (Brush)new BrushConverter().ConvertFromString("#DC2626")!;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var actualKey = (e.Key == Key.System) ? e.SystemKey : e.Key;

        if (actualKey == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (actualKey == Key.Tab)
        {
            // Tab 키: 다음 카테고리 칩으로 이동! / Shift+Tab: 이전 카테고리로 이동!
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            CycleCategory(!isShift);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (LstTemplates.Items.Count > 0)
            {
                LstTemplates.SelectedIndex = LstTemplates.SelectedIndex < LstTemplates.Items.Count - 1
                    ? LstTemplates.SelectedIndex + 1
                    : 0;
                LstTemplates.ScrollIntoView(LstTemplates.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (LstTemplates.Items.Count > 0)
            {
                LstTemplates.SelectedIndex = LstTemplates.SelectedIndex > 0
                    ? LstTemplates.SelectedIndex - 1
                    : LstTemplates.Items.Count - 1;
                LstTemplates.ScrollIntoView(LstTemplates.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void LstTemplates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (LstTemplates.SelectedItem is TemplateItem item)
        {
            RecordRecentKeyword(item.Keyword);
            Hide();
            ItemSelected?.Invoke(item, _targetHwnd);
        }
    }

    private void RecordRecentKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        var list = _configManager.Config.RecentKeywords ??= new List<string>();
        list.RemoveAll(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, keyword.Trim());
        if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
        _configManager.Save();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    public void OpenAddAndUploadDialog(Window? ownerWindow = null)
    {
        _isActionModalOpen = true;
        try
        {
            string currentQuery = TxtSearch.Text.Trim();
            string clipText = "";
            try { if (Clipboard.ContainsText()) clipText = Clipboard.GetText().Trim(); } catch { }

            var addWin = new Window
            {
                Title = $"새 답변 등록 → 구글 시트 ({DataSourceResolver.ActiveName})",
                Width = 520,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = ownerWindow ?? (IsVisible ? this : null),
                Background = (Brush)FindResource("ThemeBg"),
                BorderBrush = (Brush)FindResource("ThemeAccent"),
                BorderThickness = new Thickness(2),
                WindowStyle = WindowStyle.ToolWindow
            };

            var sp = new StackPanel { Margin = new Thickness(16) };

            sp.Children.Add(new TextBlock { Text = "단축어 (keyword - 자동완성용):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtKw = new TextBox { Height = 30, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtKw);

            sp.Children.Add(new TextBlock { Text = "본문 키워드 (contentKeyword - 의미 검색용):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtContentKw = new TextBox { Height = 30, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtContentKw);

            sp.Children.Add(new TextBlock { Text = "제목:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtTitle = new TextBox { Height = 30, Padding = new Thickness(6, 4, 6, 4), Text = currentQuery, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtTitle);

            sp.Children.Add(new TextBlock { Text = "카테고리:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtCat = new TextBox { Height = 30, Padding = new Thickness(6, 4, 6, 4), Text = _selectedCategory == "전체" || _selectedCategory == RecentCategoryName ? "일반" : _selectedCategory, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtCat);

            sp.Children.Add(new TextBlock { Text = "치환될 본문 내용 (복사해 둔 텍스트 자동 입력):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtContent = new TextBox { Height = 90, Padding = new Thickness(6, 4, 6, 4), Text = clipText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(txtContent);

            var btnUpload = new Button
            {
                Content = "💾 저장 및 스프레드시트에 즉시 업로드",
                Height = 36,
                Style = TryFindResource("ThemeButtonStyle") as Style,
                FontWeight = FontWeights.Bold
            };

            btnUpload.Click += async (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txtKw.Text) && string.IsNullOrWhiteSpace(txtTitle.Text))
                {
                    MessageBox.Show("단축어나 제목을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newItem = new TemplateItem
                {
                    Keyword = txtKw.Text.Trim(),
                    ContentKeyword = txtContentKw.Text.Trim(),
                    Title = txtTitle.Text.Trim(),
                    Category = string.IsNullOrWhiteSpace(txtCat.Text) ? "일반" : txtCat.Text.Trim(),
                    Content = txtContent.Text
                };

                _templateManager.AddTemplate(newItem);
                var (success, msg) = await _templateManager.UploadTemplateToRemoteSheetAsync(newItem);

                MessageBox.Show($"로컬 저장 완료!\n{msg}", "등록 및 업로드 결과", MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Warning);

                addWin.Close();
                BuildCategoryChips();
                UpdateList();
            };

            sp.Children.Add(btnUpload);
            addWin.Content = sp;
            addWin.ShowDialog();
        }
        finally
        {
            _isActionModalOpen = false;
        }
    }

    private void BtnAddAndUpload_Click(object sender, RoutedEventArgs e) => OpenAddAndUploadDialog(this);
}
