using System;
using System.Threading;
using System.Windows;

namespace ChatHelper.Services;

public static class TextInjectionService
{
    public static void SendBackspaces(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Win32Api.keybd_event(Win32Api.VK_BACK, 0, 0, UIntPtr.Zero);
            Thread.Sleep(5);
            Win32Api.keybd_event(Win32Api.VK_BACK, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Chrome, Google Chat, Electron, 메모장 등 모든 창에 텍스트를 안정적으로 붙여넣기(Ctrl+V) 주입합니다.
    /// AttachThreadInput 강제 포커스 복원 및 충분한 대기 시간을 보장합니다.
    /// </summary>
    public static void FastPasteText(string text, IntPtr targetHwnd)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;

            // 줄바꿈 정규화
            text = text.Replace("\\n", "\n")
                       .Replace("\r\n", "\n")
                       .Replace("\r", "\n")
                       .Replace("\n", "\r\n");

            // 1. 클립보드에 텍스트 복사 (STA 보장, 영구 보존)
            if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
            {
                Clipboard.SetDataObject(text, true);
            }
            else if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetDataObject(text, true));
            }
            else
            {
                var thread = new Thread(() => Clipboard.SetDataObject(text, true));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }

            // 2. 대상 창으로 확실한 강제 포커스 복원 (공통 ForceForegroundWindow)
            if (targetHwnd != IntPtr.Zero)
            {
                Win32Api.ForceForegroundWindow(targetHwnd);

                // Chrome 및 웹 브라우저가 포커스 및 웹 캐럿을 완전히 회복할 시간 보장
                Thread.Sleep(90);
            }

            // 3. Ctrl + V 키 이벤트 전송 (스캔코드 포함으로 카톡/구글채팅/메모장 모두 안정 동작)
            byte ctrlScan = (byte)Win32Api.MapVirtualKey((uint)Win32Api.VK_CONTROL, Win32Api.MAPVK_VK_TO_VSC);
            byte vScan = (byte)Win32Api.MapVirtualKey((uint)Win32Api.VK_V, Win32Api.MAPVK_VK_TO_VSC);

            Win32Api.keybd_event(Win32Api.VK_CONTROL, ctrlScan, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            Win32Api.keybd_event(Win32Api.VK_V, vScan, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            Win32Api.keybd_event(Win32Api.VK_V, vScan, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(15);
            Win32Api.keybd_event(Win32Api.VK_CONTROL, ctrlScan, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TextInjection] 주입 에러: {ex.Message}");
        }
    }

    private static void FocusTargetWindow(IntPtr targetHwnd) =>
        Win32Api.ForceForegroundWindow(targetHwnd);

    private static void SendKey(int vk, bool extended = false)
    {
        byte scan = (byte)Win32Api.MapVirtualKey((uint)vk, Win32Api.MAPVK_VK_TO_VSC);
        uint downFlags = extended ? Win32Api.KEYEVENTF_EXTENDEDKEY : 0;
        uint upFlags = (extended ? Win32Api.KEYEVENTF_EXTENDEDKEY : 0) | Win32Api.KEYEVENTF_KEYUP;

        Win32Api.keybd_event((byte)vk, scan, downFlags, UIntPtr.Zero);
        Thread.Sleep(15);
        Win32Api.keybd_event((byte)vk, scan, upFlags, UIntPtr.Zero);
    }

    public static void ReplaceText(int deleteCount, string newText, IntPtr targetHwnd, bool useShiftHome = false)
    {
        FocusTargetWindow(targetHwnd);
        Thread.Sleep(35);

        if (deleteCount > 0 || useShiftHome)
        {
            // Chrome/웹 한글 IME 조합 중인 마지막 글자 확정:
            // 확장 키(KEYEVENTF_EXTENDEDKEY) + 스캔코드로 VK_RIGHT 전송해야 NumLock ON 상태에서도 정상 동작
            SendKey(Win32Api.VK_RIGHT, extended: true);
            Thread.Sleep(50);

            if (useShiftHome)
            {
                // Shift + Extended Home 으로 현재 줄 전체 선택 후 Backspace (한글 IME 잔존 글자 원천 차단)
                byte shiftScan = (byte)Win32Api.MapVirtualKey((uint)Win32Api.VK_SHIFT, Win32Api.MAPVK_VK_TO_VSC);
                Win32Api.keybd_event(Win32Api.VK_SHIFT, shiftScan, 0, UIntPtr.Zero);
                Thread.Sleep(15);
                SendKey(Win32Api.VK_HOME, extended: true);
                Thread.Sleep(25);
                Win32Api.keybd_event(Win32Api.VK_SHIFT, shiftScan, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(25);

                SendKey(Win32Api.VK_BACK, extended: false);
                Thread.Sleep(50);
            }
            else
            {
                SendBackspaces(deleteCount);
                Thread.Sleep(50);
            }
        }

        FastPasteText(newText, targetHwnd);
    }
}
