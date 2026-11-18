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
        try
        {
            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            System.IO.File.WriteAllText(logPath, e.Exception.ToString());
        }
        catch { }

        MessageBox.Show($"오류가 발생했습니다:\n{e.Exception.Message}\n\n상세: {e.Exception.InnerException?.Message}", "ChatHelper 알림", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
