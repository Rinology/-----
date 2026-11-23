using System.Windows;
using System.Windows.Input;

namespace ChatHelper.UI;

public partial class ExitConfirmWindow : Window
{
    public enum ExitChoice
    {
        Cancel,
        KeepLoginAndExit,
        LogoutAndExit
    }

    private readonly bool _logoutOnExit;
    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;

    public ExitConfirmWindow(bool logoutOnExit = false)
    {
        _logoutOnExit = logoutOnExit;
        InitializeComponent();
        if (TxtExitDesc != null)
        {
            TxtExitDesc.Text = _logoutOnExit
                ? "설정에 따라 종료 시 자동 로그아웃(저장된 아이디 초기화)됩니다."
                : "현재 로그인 아이디를 유지한 채 프로그램을 종료합니다.";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Choice = ExitChoice.Cancel;
            DialogResult = false;
            Close();
        }
    }

    private void BtnConfirmExit_Click(object sender, RoutedEventArgs e)
    {
        Choice = _logoutOnExit ? ExitChoice.LogoutAndExit : ExitChoice.KeepLoginAndExit;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
