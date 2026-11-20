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

        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }

        Deactivated += QuickSearchWindow_Deactivated;
    }

    private void QuickSearchWindow_Deactivated(object? sender, EventArgs e)
    {
        // 사용자가 Alt+Q를 누르고 손을 뗄 때 발생하는 일시적 포커스 플리커(Focus Flicker)로 창이 닫히는 현상 방지 (450ms 유예)
        if ((DateTime.UtcNow - _openedAt).TotalMilliseconds < 450)
        {
            return;
        }

        // 모달창이나 메시지박스가 열려있는 중이 아니라면 포커스 상실 시 닫기
        if (!_isActionModalOpen && IsVisible)
        {
            Hide();
        }
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

        // 외부 창으로부터 포커스 강제 탈취
        var helper = new WindowInteropHelper(this);
        Win32Api.SetForegroundWindow(helper.Handle);
        Activate();
        TxtSearch.Focus();
        Keyboard.Focus(TxtSearch);
    }

    private void BuildCategoryChips()
    {
        CategoryPanel.Children.Clear();
        var cats = _templateManager.GetCategories();

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
        var cats = _templateManager.GetCategories();
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
        var items = _templateManager.Search(query, _selectedCategory);

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
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            // Tab 키: 다음 카테고리 칩으로 이동! / Shift+Tab: 이전 카테고리로 이동!
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            CycleCategory(!isShift);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            // 아래 방향키: 템플릿 목록 다음 항목 선택 (순환)
            if (LstTemplates.Items.Count > 0)
            {
                if (LstTemplates.SelectedIndex < LstTemplates.Items.Count - 1)
                {
                    LstTemplates.SelectedIndex++;
                }
                else
                {
                    LstTemplates.SelectedIndex = 0;
                }
                LstTemplates.ScrollIntoView(LstTemplates.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            // 위 방향키: 템플릿 목록 이전 항목 선택 (순환)
            if (LstTemplates.Items.Count > 0)
            {
                if (LstTemplates.SelectedIndex > 0)
                {
                    LstTemplates.SelectedIndex--;
                }
                else
                {
                    LstTemplates.SelectedIndex = LstTemplates.Items.Count - 1;
                }
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
            Hide();
            ItemSelected?.Invoke(item, _targetHwnd);
        }
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

    private void BtnAddAndUpload_Click(object sender, RoutedEventArgs e)
    {
        _isActionModalOpen = true;
        try
        {
            // 사용자가 입력한 검색어가 있다면 초기값으로 활용
            string currentQuery = TxtSearch.Text.Trim();

            var addWin = new Window
            {
                Title = "새 템플릿 등록 및 구글 시트 업로드",
                Width = 520,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("ThemeBg"),
                BorderBrush = (Brush)FindResource("ThemeAccent"),
                BorderThickness = new Thickness(2),
                WindowStyle = WindowStyle.ToolWindow
            };

            var sp = new StackPanel { Margin = new Thickness(16) };

            sp.Children.Add(new TextBlock { Text = "단축어 (keyword - 자동완성용):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtKw = new TextBox { Height = 32, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };
            sp.Children.Add(txtKw);

            sp.Children.Add(new TextBlock { Text = "본문 키워드 (contentKeyword - AI/의미 검색용):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtContentKw = new TextBox { Height = 32, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };
            sp.Children.Add(txtContentKw);

            sp.Children.Add(new TextBlock { Text = "제목:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtTitle = new TextBox { Height = 32, Text = currentQuery, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };
            sp.Children.Add(txtTitle);

            sp.Children.Add(new TextBlock { Text = "카테고리:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtCat = new TextBox { Height = 32, Text = _selectedCategory == "전체" ? "일반" : _selectedCategory, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };
            sp.Children.Add(txtCat);

            sp.Children.Add(new TextBlock { Text = "치환될 본문 내용:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtContent = new TextBox { Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Style = (Style)FindResource("ThemeTextBoxStyle") };
            sp.Children.Add(txtContent);

            var btnUpload = new Button
            {
                Content = "💾 저장 및 스프레드시트에 즉시 업로드",
                Height = 36,
                Style = (Style)FindResource("ThemeButtonStyle"),
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
}
