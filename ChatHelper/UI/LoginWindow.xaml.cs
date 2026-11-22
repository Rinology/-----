using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class LoginWindow : Window
{
    private readonly AuthService _authService;
    private readonly ConfigManager _configManager;

    public bool IsAuthenticated { get; private set; } = false;

    public LoginWindow(AuthService authService, ConfigManager configManager)
    {
        _authService = authService;
        _configManager = configManager;

        InitializeComponent();

        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
        }
        catch { }

        // 이전 아이디만 복원 (비밀번호는 저장하지 않음)
        ChkKeepLoggedIn.IsChecked = _configManager.Config.KeepLoggedIn;
        TxtUserId.Text = _configManager.Config.KeepLoggedIn ? (_configManager.Config.CurrentUserId ?? "") : "";

        Loaded += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(TxtUserId.Text))
            {
                TxtUserId.Focus();
            }
            else
            {
                TxtPassword.Focus();
            }
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnTogglePw_Click(object sender, RoutedEventArgs e)
    {
        if (TxtPasswordVisible.Visibility == Visibility.Visible)
        {
            // 숨기기 모드로 전환
            TxtPassword.Password = TxtPasswordVisible.Text;
            TxtPasswordVisible.Visibility = Visibility.Collapsed;
            TxtPassword.Visibility = Visibility.Visible;
            BtnTogglePw.Content = "보기";
            TxtPassword.Focus();
        }
        else
        {
            // 보이기 모드로 전환
            TxtPasswordVisible.Text = TxtPassword.Password;
            TxtPassword.Visibility = Visibility.Collapsed;
            TxtPasswordVisible.Visibility = Visibility.Visible;
            BtnTogglePw.Content = "숨김";
            TxtPasswordVisible.Focus();
            TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
        }
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        string userId = TxtUserId.Text.Trim();
        string password = TxtPasswordVisible.Visibility == Visibility.Visible
            ? TxtPasswordVisible.Text
            : TxtPassword.Password;

        if (string.IsNullOrWhiteSpace(userId))
        {
            LblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D00000"));
            LblStatus.Text = "아이디를 입력해 주세요.";
            TxtUserId.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            LblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D00000"));
            LblStatus.Text = "비밀번호를 입력해 주세요.";
            if (TxtPasswordVisible.Visibility == Visibility.Visible)
                TxtPasswordVisible.Focus();
            else
                TxtPassword.Focus();
            return;
        }

        // UI 블로킹 방지 및 로딩 상태 표시
        BtnLogin.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        TxtUserId.IsEnabled = false;
        TxtPassword.IsEnabled = false;
        TxtPasswordVisible.IsEnabled = false;
        BtnTogglePw.IsEnabled = false;
        ProgressBarLoading.Visibility = Visibility.Visible;
        LblStatus.Foreground = (Brush)(TryFindResource("ThemeAccent") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")));
        LblStatus.Text = "유저 정보를 확인하고 있습니다...";

        try
        {
            // 구글 시트 원격 동기화 포함 비동기 검증
            var (success, msg, user) = await _authService.ValidateUserAsync(userId, password, syncFirst: true);

            if (success)
            {
                IsAuthenticated = true;
                _authService.ClearUserDirectory(); // 로그인 완료 즉시 타 직원 계정 목록 메모리 제거
                bool rememberId = ChkKeepLoggedIn.IsChecked == true;
                _configManager.Config.KeepLoggedIn = rememberId;
                _configManager.Config.CurrentUserId = rememberId ? userId : "";
                _configManager.Save();

                // 팝업 없이 즉시 프로그램 구동 진입
                DialogResult = true;
                Close();
            }
            else
            {
                LblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D00000"));
                LblStatus.Text = msg;
            }
        }
        catch (Exception ex)
        {
            LblStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D00000"));
            LblStatus.Text = $"로그인 요청 실패: {ex.Message}";
        }
        finally
        {
            ProgressBarLoading.Visibility = Visibility.Collapsed;
            BtnLogin.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            TxtUserId.IsEnabled = true;
            TxtPassword.IsEnabled = true;
            TxtPasswordVisible.IsEnabled = true;
            BtnTogglePw.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
