using System.Windows;
using System.Windows.Input;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class LoginWindow : Window
{
    private readonly AuthService _authService;
    private readonly ConfigManager _configManager;

    public bool IsAuthenticated { get; private set; } = false;

    public LoginWindow(AuthService authService, ConfigManager configManager)
    {
        InitializeComponent();
        _authService = authService;
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

        TxtUserId.Text = _configManager.Config.CurrentUserId ?? "";
        ChkKeepLoggedIn.IsChecked = _configManager.Config.KeepLoggedIn;

        TxtUserId.Focus();
        TxtUserId.SelectAll();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        string userId = TxtUserId.Text.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            LblStatus.Text = "아이디를 입력해 주세요.";
            return;
        }

        // UI 블로킹 방지: 컨트롤 비활성화 및 진행 표시
        BtnLogin.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        TxtUserId.IsEnabled = false;
        LblStatus.Text = "사용자 인증 확인 중...";

        try
        {
            var (success, msg, user) = await _authService.ValidateUserAsync(userId);

            if (success)
            {
                IsAuthenticated = true;
                _configManager.Config.CurrentUserId = userId;
                _configManager.Config.KeepLoggedIn = ChkKeepLoggedIn.IsChecked == true;
                _configManager.Save();

                MessageBox.Show(msg, "로그온 성공", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                LblStatus.Text = msg;
                MessageBox.Show(msg, "로그온 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            BtnLogin.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            TxtUserId.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
