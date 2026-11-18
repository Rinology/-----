using System.Windows;

namespace ChatHelper.Services;

public static class TextInjectionService
{
    public static void SendBackspaces(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Win32Api.keybd_event(Win32Api.VK_BACK, 0, 0, UIntPtr.Zero);
            Thread.Sleep(3);
            Win32Api.keybd_event(Win32Api.VK_BACK, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(3);
        }
    }

    public static void FastPasteText(string text, IntPtr targetHwnd)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;

            // 본문 줄바꿈 처리: \n 이스케이프 문자 치환 및 CRLF(\r\n) 표준 줄바꿈으로 정규화
            text = text.Replace("\\n", "\n")
                       .Replace("\r\n", "\n")
                       .Replace("\r", "\n")
                       .Replace("\n", "\r\n");

            // 클립보드에 텍스트 복사 (STA Thread 보장)
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
                Thread.Sleep(10);
            }

            // Ctrl + V 키 이벤트 전송
            Win32Api.keybd_event(Win32Api.VK_CONTROL, 0, 0, UIntPtr.Zero);
            Win32Api.keybd_event(Win32Api.VK_V, 0, 0, UIntPtr.Zero);
            Thread.Sleep(5);
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
            Thread.Sleep(10);
        }

        FastPasteText(newText, targetHwnd);
    }
}
