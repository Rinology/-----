using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ChatHelper.Services;

namespace ChatHelper;

public partial class App : System.Windows.Application
{
    public App()
    {
        // 어떤 PC(고주사율/듀얼모니터/하이브리드 GPU/원격)에서도 D3D9·DWM 마우스 스터터가 발생하지 않도록 소프트웨어 렌더링 강제
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // AllowsTransparency 제거 후 Windows 11 네이티브 둥근 모서리/그림자 적용
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is Window win)
            {
                var hwnd = new WindowInteropHelper(win).Handle;
                Win32Api.EnableNativeRoundedCorners(hwnd);
            }
        }));

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
