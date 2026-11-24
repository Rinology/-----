using System;
using System.Collections.Generic;

namespace ChatHelper.Services;

/// <summary>
/// "Alt+Q", "Alt+S", "Ctrl+Shift+Space", "F9" 같은 사용자 설정 전역 단축키 문자열을
/// Win32 RegisterHotKey 및 저수준 키훅(WH_KEYBOARD_LL) 판별용 플래그/VK/스캔코드로 변환합니다.
/// </summary>
public static class HotkeyParser
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = Win32Api.VK_SPACE,
        ["TAB"] = Win32Api.VK_TAB,
        ["ENTER"] = Win32Api.VK_RETURN,
        ["RETURN"] = Win32Api.VK_RETURN,
        ["ESC"] = Win32Api.VK_ESCAPE,
        ["ESCAPE"] = Win32Api.VK_ESCAPE,
        ["BACK"] = Win32Api.VK_BACK,
        ["BACKSPACE"] = Win32Api.VK_BACK,
        ["DELETE"] = Win32Api.VK_DELETE,
        ["DEL"] = Win32Api.VK_DELETE,
        ["HOME"] = Win32Api.VK_HOME,
        ["END"] = Win32Api.VK_END,
        ["PAGEUP"] = Win32Api.VK_PRIOR,
        ["PGUP"] = Win32Api.VK_PRIOR,
        ["PAGEDOWN"] = Win32Api.VK_NEXT,
        ["PGDN"] = Win32Api.VK_NEXT,
        ["INSERT"] = 0x2D,
        ["INS"] = 0x2D,
    };

    public static bool TryParse(string? hotkeyText, out uint modFlags, out int vkCode, out int scanCode)
    {
        modFlags = 0;
        vkCode = 0;
        scanCode = 0;

        if (string.IsNullOrWhiteSpace(hotkeyText))
            return false;

        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string raw in parts)
        {
            string token = raw.ToUpperInvariant();
            switch (token)
            {
                case "ALT":
                    modFlags |= MOD_ALT;
                    continue;
                case "CTRL":
                case "CONTROL":
                    modFlags |= MOD_CONTROL;
                    continue;
                case "SHIFT":
                    modFlags |= MOD_SHIFT;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modFlags |= MOD_WIN;
                    continue;
            }

            if (token.Length == 1 && token[0] >= 'A' && token[0] <= 'Z')
            {
                vkCode = token[0];
            }
            else if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
            {
                vkCode = token[0];
            }
            else if (token.StartsWith('F') && int.TryParse(token[1..], out int fn) && fn >= 1 && fn <= 24)
            {
                vkCode = 0x70 + (fn - 1);
            }
            else if (NamedKeys.TryGetValue(token, out int namedVk))
            {
                vkCode = namedVk;
            }
            else
            {
                return false;
            }
        }

        if (vkCode == 0)
            return false;

        if (vkCode >= Win32Api.VK_A && vkCode <= Win32Api.VK_Z)
        {
            scanCode = (int)Win32Api.MapVirtualKey((uint)vkCode, 0); // MAPVK_VK_TO_VSC
        }

        return true;
    }

    public static (uint ModFlags, int VkCode, int ScanCode, string NormalizedLabel) ResolveOrDefault(string? hotkeyText)
    {
        if (TryParse(hotkeyText, out uint mod, out int vk, out int scan))
        {
            return (mod, vk, scan, hotkeyText!.Trim());
        }
        return (MOD_ALT, 'S', 0x1F, "Alt+S");
    }
}
