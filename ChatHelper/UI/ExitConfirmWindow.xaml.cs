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

    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;

    public ExitConfirmWindow()
    {
        InitializeComponent();
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

    private void BtnKeepExit_Click(object sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.KeepLoginAndExit;
        DialogResult = true;
        Close();
    }

    private void BtnLogoutExit_Click(object sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.LogoutAndExit;
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
