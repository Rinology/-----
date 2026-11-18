using System.Windows;
using System.Windows.Threading;

namespace ChatHelper;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"오류가 발생했습니다:\n{e.Exception.Message}", "ChatHelper 알림", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
