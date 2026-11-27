using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace ChatHelper.Services;

public static class TextInjectionService
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static string LastInjectedText { get; private set; } = string.Empty;
    public static uint LastInjectedClipboardSeq { get; private set; } = 0;

    public static void SendBackspaces(int count)
    {
        if (count <= 0) return;

        // 조합 중인 한글 IME(예: "전원이 안켜져요"의 마지막 "요")를 확정시켜 1글자=1백스페이스로 정확히 지워지도록 함
        var commitIme = new[]
        {
            CreateKeyInput(Win32Api.VK_RIGHT, keyUp: false, extended: true),
            CreateKeyInput(Win32Api.VK_RIGHT, keyUp: true, extended: true)
        };
        Win32Api.SendInput((uint)commitIme.Length, commitIme, Marshal.SizeOf<Win32Api.INPUT>());
        Thread.Sleep(15);

        for (int i = 0; i < count; i++)
        {
            var bs = new[]
            {
                CreateKeyInput(Win32Api.VK_BACK, keyUp: false, extended: false),
                CreateKeyInput(Win32Api.VK_BACK, keyUp: true, extended: false)
            };
            Win32Api.SendInput((uint)bs.Length, bs, Marshal.SizeOf<Win32Api.INPUT>());
            Thread.Sleep(8);
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

            LastInjectedText = text;

            // 1. 클립보드에 텍스트 복사 (OpenClipboard 점유 충돌 대비 최대 5회 재시도)
            SetClipboardTextWithRetry(text);
            LastInjectedClipboardSeq = Win32Api.GetClipboardSequenceNumber();

            // 2. 대상 창으로 확실한 강제 포커스 복원 (공통 ForceForegroundWindow)
            if (targetHwnd != IntPtr.Zero)
            {
                Win32Api.ForceForegroundWindow(targetHwnd);

                // Chrome 및 웹 브라우저가 포커스 및 웹 캐럿을 완전히 회복할 시간 보장
                Thread.Sleep(110);
            }

            // 실제로 Alt 키가 눌려 있을 때만 안전하게 해제 (평소 \+Tab 등에서 불필요한 WM_SYSKEYUP으로 Win11 메모장 리본 팁이 뜨는 현상 방지)
            if ((GetAsyncKeyState(Win32Api.VK_MENU) & 0x8000) != 0)
            {
                var releaseAlt = new[]
                {
                    CreateKeyInput(Win32Api.VK_LMENU, keyUp: true, extended: false),
                    CreateKeyInput(Win32Api.VK_MENU, keyUp: true, extended: false)
                };
                Win32Api.SendInput((uint)releaseAlt.Length, releaseAlt, Marshal.SizeOf<Win32Api.INPUT>());
            }

            // 3. SendInput으로 Ctrl + V 원자적 전송 (키 풀림/순서 꼬임 원천 차단)
            SendKeyCombo(Win32Api.VK_CONTROL, Win32Api.VK_V);
            Thread.Sleep(30);
        }
        catch (Exception ex)
        {
            AppLogger.Error("TextInjectionService", "텍스트 주입 에러", ex);
        }
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        const int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
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
                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    AppLogger.Warn("TextInjectionService", $"클립보드 설정 {maxRetries}회 실패", ex);
                }
                else
                {
                    Thread.Sleep(20);
                }
            }
        }
    }

    private static Win32Api.INPUT CreateKeyInput(int vk, bool keyUp, bool extended)
    {
        ushort scan = (ushort)Win32Api.MapVirtualKey((uint)vk, Win32Api.MAPVK_VK_TO_VSC);
        uint flags = 0;
        if (extended) flags |= Win32Api.KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= Win32Api.KEYEVENTF_KEYUP;

        return new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.INPUT_UNION
            {
                ki = new Win32Api.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static void SendKey(int vk, bool extended = false)
    {
        var inputs = new[]
        {
            CreateKeyInput(vk, keyUp: false, extended),
            CreateKeyInput(vk, keyUp: true, extended)
        };
        Win32Api.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Api.INPUT>());
    }

    private static void SendKeyCombo(int modifierVk, int keyVk, bool keyExtended = false)
    {
        var inputs = new[]
        {
            CreateKeyInput(modifierVk, keyUp: false, extended: false),
            CreateKeyInput(keyVk, keyUp: false, keyExtended),
            CreateKeyInput(keyVk, keyUp: true, keyExtended),
            CreateKeyInput(modifierVk, keyUp: true, extended: false)
        };
        Win32Api.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Api.INPUT>());
    }

    public static void ReplaceText(int deleteCount, string newText, IntPtr targetHwnd, bool useShiftHome = false)
    {
        Win32Api.ForceForegroundWindow(targetHwnd);
        Thread.Sleep(35);

        if (deleteCount > 0 || useShiftHome)
        {
            // Chrome/웹 한글 IME 조합 중인 마지막 글자 확정
            SendKey(Win32Api.VK_RIGHT, extended: true);
            Thread.Sleep(45);

            if (useShiftHome)
            {
                // Shift + Extended Home 원자적 전송 후 Backspace
                SendKeyCombo(Win32Api.VK_SHIFT, Win32Api.VK_HOME, keyExtended: true);
                Thread.Sleep(25);
                SendKey(Win32Api.VK_BACK, extended: false);
                Thread.Sleep(45);
            }
            else
            {
                SendBackspaces(deleteCount);
                Thread.Sleep(45);
            }
        }

        FastPasteText(newText, targetHwnd);
    }

    /// <summary>
    /// 사용자가 마우스/키보드로 드래그해 둔 텍스트를 Ctrl+C로 안전하게 읽어옵니다.
    /// 선택 영역이 없으면 빈 문자열을 반환하고 기존 클립보드를 복원합니다.
    /// </summary>
    public static string CaptureSelectedText(IntPtr targetHwnd)
    {
        string prevClip = GetClipboardTextSafe();
        try
        {
            ClearClipboardSafe();
            if (targetHwnd != IntPtr.Zero)
            {
                Win32Api.ForceForegroundWindow(targetHwnd);
                Thread.Sleep(55);
            }

            // Alt+T / Alt+R 직후 Alt 키 눌림 상태 해제
            var releaseAlt = new[]
            {
                CreateKeyInput(Win32Api.VK_LMENU, keyUp: true, extended: false),
                CreateKeyInput(Win32Api.VK_MENU, keyUp: true, extended: false)
            };
            Win32Api.SendInput((uint)releaseAlt.Length, releaseAlt, Marshal.SizeOf<Win32Api.INPUT>());
            Thread.Sleep(25);

            uint seqBefore = Win32Api.GetClipboardSequenceNumber();
            SendKeyCombo(Win32Api.VK_CONTROL, Win32Api.VK_C);
            Thread.Sleep(110);
            uint seqAfter = Win32Api.GetClipboardSequenceNumber();

            string copied = seqAfter != seqBefore ? GetClipboardTextSafe() : string.Empty;
            if (string.IsNullOrWhiteSpace(copied))
            {
                if (!string.IsNullOrEmpty(prevClip))
                    SetClipboardTextWithRetry(prevClip);
                return string.Empty;
            }
            return copied.Trim();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TextInjectionService", "선택 텍스트 캡처 실패", ex);
            return string.Empty;
        }
    }

    public static string GetClipboardTextSafe()
    {
        try
        {
            string result = string.Empty;
            if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
            {
                if (Clipboard.ContainsText()) result = Clipboard.GetText();
            }
            else if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText()) result = Clipboard.GetText();
                });
            }
            return result;
        }
        catch { return string.Empty; }
    }

    private static void ClearClipboardSafe()
    {
        try
        {
            if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
                Clipboard.Clear();
            else if (Application.Current != null)
                Application.Current.Dispatcher.Invoke(() => Clipboard.Clear());
        }
        catch { }
    }
}
