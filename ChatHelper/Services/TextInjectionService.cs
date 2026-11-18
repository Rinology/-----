using System.Windows;

namespace ChatHelper.Services;

public static class TextInjectionService
{
    public static void SendBackspaces(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Win32Api.keybd_event(Win32Api.VK_BACK, 0, 0, UIntPtr.Zero);
            Win32Api.keybd_event(Win32Api.VK_BACK, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    public static void FastPasteText(string text, IntPtr targetHwnd)
    {
        try
        {
            // 클립보드 설정 (WPF Dispatcher 활용 또는 STA Thread)
            if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
            {
                Clipboard.SetDataObject(text, false);
            }
            else if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetDataObject(text, false));
            }
            else
            {
                var thread = new Thread(() => Clipboard.SetDataObject(text, false));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }

            // 대상 창으로 포커스 복원
            if (targetHwnd != IntPtr.Zero && Win32Api.GetForegroundWindow() != targetHwnd)
            {
                Win32Api.SetForegroundWindow(targetHwnd);
                Thread.Sleep(5);
            }

            // Ctrl + V 전송
            Win32Api.keybd_event(Win32Api.VK_CONTROL, 0, 0, UIntPtr.Zero);
            Win32Api.keybd_event(Win32Api.VK_V, 0, 0, UIntPtr.Zero);
            Win32Api.keybd_event(Win32Api.VK_V, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Win32Api.keybd_event(Win32Api.VK_CONTROL, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TextInjection] 주입 에러: {ex.Message}");
        }
    }

    public static void ReplaceText(int deleteCount, string newText, IntPtr targetHwnd)
    {
        if (targetHwnd != IntPtr.Zero && Win32Api.GetForegroundWindow() != targetHwnd)
        {
            Win32Api.SetForegroundWindow(targetHwnd);
            Thread.Sleep(5);
        }

        if (deleteCount > 0)
        {
            SendBackspaces(deleteCount);
            Thread.Sleep(5);
        }

        FastPasteText(newText, targetHwnd);
    }
}
