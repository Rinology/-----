using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

/// <summary>
/// QuickSearchWindow 전체를 띄우지 않고 <10ms 내에 즉시 열리는 초경량 시트 빠른 등록 대화상자.
/// 업로드 중 로딩 프로그레스 바를 표시하고, 드래그/클립보드 본문 범위를 명확히 보여줍니다.
/// </summary>
public static class QuickRegisterDialog
{
    public static void Show(
        TemplateManager templateManager,
        Window? owner,
        string? initialKeyword = null,
        string? initialContent = null,
        Action<TemplateItem, bool, string>? onCompleted = null)
    {
        string seedKw = (initialKeyword ?? string.Empty).Trim().TrimStart('\\', '/');
        string seedContent = !string.IsNullOrWhiteSpace(initialContent)
            ? initialContent.Trim()
            : TextInjectionService.GetClipboardTextSafe().Trim();

        if (string.IsNullOrWhiteSpace(seedKw) && !string.IsNullOrWhiteSpace(seedContent))
        {
            string singleLine = seedContent.Replace("\r", "").Replace("\n", " ").Trim();
            seedKw = singleLine.Length > 10 ? singleLine[..10].Trim() : singleLine;
        }

        var bgBrush = Application.Current?.TryFindResource("ThemeBg") as Brush
            ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
        var borderBrush = Application.Current?.TryFindResource("ThemeBorder") as Brush
            ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
        var textBrush = Application.Current?.TryFindResource("ThemeText") as Brush
            ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
        var subTextBrush = Application.Current?.TryFindResource("ThemeSubText") as Brush
            ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        var accentBrush = Application.Current?.TryFindResource("ThemeAccent") as Brush
            ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

        var dlg = new Window
        {
            Title = $"시트 빠른 등록 ({DataSourceResolver.ActiveName})",
            Width = 480,
            Height = 470,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Owner = (owner != null && owner.IsVisible) ? owner : null,
            Background = bgBrush,
            WindowStyle = WindowStyle.ToolWindow,
            Topmost = true
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(initialContent)
                ? "※ 현재 클립보드(Ctrl+C) 내용이 본문에 자동 입력됩니다. (드래그 후 Alt+R도 가능)"
                : "※ 드래그로 선택한 텍스트가 본문에 자동 입력되었습니다.",
            Foreground = accentBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        root.Children.Add(new TextBlock { Text = "단축어 (keyword - 자동완성 호출용)", FontWeight = FontWeights.Bold, Foreground = textBrush, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var txtKw = new TextBox { Height = 30, Padding = new Thickness(8, 5, 8, 5), Text = seedKw, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(txtKw);

        root.Children.Add(new TextBlock { Text = "제목 (알약 표시 이름)", FontWeight = FontWeights.Bold, Foreground = textBrush, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var txtTitle = new TextBox { Height = 30, Padding = new Thickness(8, 5, 8, 5), Text = seedKw, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(txtTitle);

        root.Children.Add(new TextBlock { Text = "카테고리", FontWeight = FontWeights.Bold, Foreground = textBrush, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var txtCat = new TextBox { Height = 30, Padding = new Thickness(8, 5, 8, 5), Text = "일반", Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(txtCat);

        root.Children.Add(new TextBlock { Text = "시트에 저장될 본문 (어디까지 저장될지 확인·수정)", FontWeight = FontWeights.Bold, Foreground = textBrush, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var txtContent = new TextBox
        {
            Height = 110,
            Padding = new Thickness(8, 6, 8, 6),
            Text = seedContent,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(txtContent);

        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 5,
            Visibility = Visibility.Collapsed,
            Foreground = accentBrush,
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(progressBar);

        var lblStatus = new TextBlock
        {
            Text = "",
            FontSize = 11.5,
            Foreground = subTextBrush,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(lblStatus);

        var btnPanel = new Grid();
        btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btnSave = new Button
        {
            Content = "💾 구글 시트에 즉시 저장",
            Height = 36,
            Background = accentBrush,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        Grid.SetColumn(btnSave, 0);

        var btnCancel = new Button
        {
            Content = "취소",
            Height = 36,
            Width = 76,
            Background = Brushes.White,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnCancel.Click += (_, _) => dlg.Close();
        Grid.SetColumn(btnCancel, 2);

        btnPanel.Children.Add(btnSave);
        btnPanel.Children.Add(btnCancel);
        root.Children.Add(btnPanel);

        btnSave.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtKw.Text) && string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show(dlg, "단축어나 제목을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtContent.Text))
            {
                MessageBox.Show(dlg, "저장할 본문 내용을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnSave.IsEnabled = false;
            btnCancel.IsEnabled = false;
            btnSave.Content = "⏳ 시트에 저장 중...";
            progressBar.Visibility = Visibility.Visible;
            lblStatus.Text = "구글 스프레드시트로 전송 중입니다. 잠시만 기다려 주세요...";
            lblStatus.Visibility = Visibility.Visible;

            var newItem = new TemplateItem
            {
                Keyword = string.IsNullOrWhiteSpace(txtKw.Text) ? txtTitle.Text.Trim() : txtKw.Text.Trim(),
                ContentKeyword = txtKw.Text.Trim(),
                Title = string.IsNullOrWhiteSpace(txtTitle.Text) ? txtKw.Text.Trim() : txtTitle.Text.Trim(),
                Category = string.IsNullOrWhiteSpace(txtCat.Text) ? "일반" : txtCat.Text.Trim(),
                Content = txtContent.Text
            };

            templateManager.AddTemplate(newItem);
            var (success, msg) = await templateManager.UploadTemplateToRemoteSheetAsync(newItem);

            progressBar.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke(newItem, success, msg);

            if (success)
            {
                MessageBox.Show(dlg, $"구글 시트에 저장 완료!\n({newItem.Keyword})", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                dlg.Close();
            }
            else
            {
                lblStatus.Text = $"⚠️ 로컬에는 저장되었으나 시트 업로드 실패: {msg}";
                lblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                btnSave.IsEnabled = true;
                btnCancel.IsEnabled = true;
                btnSave.Content = "💾 다시 시도";
            }
        };

        dlg.Content = root;
        dlg.Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtKw.Text))
                txtKw.Focus();
            else
                txtContent.Focus();
        };
        dlg.ShowDialog();
    }
}
