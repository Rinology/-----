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

        TxtUserId.Text = _configManager.Config.CurrentUserId ?? "";
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

    private void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        string userId = TxtUserId.Text.Trim();
        var (success, msg, user) = _authService.ValidateUser(userId);

        if (success)
        {
            IsAuthenticated = true;
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

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
