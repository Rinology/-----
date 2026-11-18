using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public event Action<TemplateItem, IntPtr>? ItemSelected;

    public QuickSearchWindow(TemplateManager templateManager, ConfigManager configManager)
    {
        InitializeComponent();
        _templateManager = templateManager;
        _configManager = configManager;

        Deactivated += QuickSearchWindow_Deactivated;
    }

    private void QuickSearchWindow_Deactivated(object? sender, EventArgs e)
    {
        // 모달창이나 메시지박스가 열려있는 중이 아니라면 포커스 상실 시 닫기
        if (!_isActionModalOpen && IsVisible)
        {
            Hide();
        }
    }

    public void ShowAtCursor(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;

        // 마우스 커서 위치 계산하여 화면 중앙 또는 커서 인근에 표시
        Win32Api.GetCursorPos(out var pt);
        double screenW = SystemParameters.PrimaryScreenWidth;
        double screenH = SystemParameters.PrimaryScreenHeight;

        double targetX = pt.X - (Width / 2);
        double targetY = pt.Y + 25;

        Left = Math.Max(20, Math.Min(targetX, screenW - Width - 20));
        Top = Math.Max(20, Math.Min(targetY, screenH - Height - 20));

        _selectedCategory = "전체";
        TxtSearch.Clear();
        BtnClearSearch.Visibility = Visibility.Collapsed;

        BuildCategoryChips();
        UpdateList();

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
            bool isSelected = (cat == _selectedCategory);
            var btn = new Button
            {
                Content = cat,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 4, 12, 4),
                FontSize = 12,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                Cursor = Cursors.Hand,
                Background = isSelected 
                    ? (Brush)new BrushConverter().ConvertFromString("#2563EB")! 
                    : (Brush)new BrushConverter().ConvertFromString("#3F3F46")!,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
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
        }
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
        LstTemplates.ItemsSource = items;

        if (items.Count > 0)
        {
            LstTemplates.SelectedIndex = 0;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (LstTemplates.Items.Count > 0 && LstTemplates.SelectedIndex < LstTemplates.Items.Count - 1)
            {
                LstTemplates.SelectedIndex++;
                LstTemplates.ScrollIntoView(LstTemplates.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (LstTemplates.SelectedIndex > 0)
            {
                LstTemplates.SelectedIndex--;
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
                Height = 440,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("ThemeBg"),
                BorderBrush = (Brush)FindResource("ThemeAccent"),
                BorderThickness = new Thickness(2),
                WindowStyle = WindowStyle.ToolWindow
            };

            var sp = new StackPanel { Margin = new Thickness(16) };

            sp.Children.Add(new TextBlock { Text = "단축어 (영문/자모 매핑):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtKw = new TextBox { Height = 32, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };

            sp.Children.Add(new TextBlock { Text = "제목:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtTitle = new TextBox { Height = 32, Text = currentQuery, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };

            sp.Children.Add(new TextBlock { Text = "카테고리:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtCat = new TextBox { Height = 32, Text = _selectedCategory == "전체" ? "일반" : _selectedCategory, Margin = new Thickness(0, 0, 0, 10), Style = (Style)FindResource("ThemeTextBoxStyle") };

            sp.Children.Add(new TextBlock { Text = "치환될 본문 내용:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            var txtContent = new TextBox { Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Style = (Style)FindResource("ThemeTextBoxStyle") };

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
