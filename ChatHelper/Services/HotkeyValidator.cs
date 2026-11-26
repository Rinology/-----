namespace ChatHelper.Services;

public static class HotkeyValidator
{
    private static readonly Dictionary<string, string> KnownConflictHotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Alt+X", "ChatHelper 내부 '특수기호 입력창(Alt+X)' 단축키와 중복됩니다." },
        { "Alt+T", "ChatHelper 내부 '드래그 Google 번역(Alt+T)' 단축키와 중복됩니다." },
        { "Alt+R", "ChatHelper 예약 단축키(Alt+R)와 중복됩니다." },
        { "Alt+Tab", "윈도우 활성 창 전환 단축키와 충돌합니다." },
        { "Alt+F4", "윈도우 프로그램 종료 단축키와 충돌합니다." },
        { "Alt+Space", "윈도우 창 시스템 메뉴 단축키와 충돌합니다." },
        { "Alt+Enter", "속성/전체화면 전환 단축키와 충돌합니다." },
        { "Alt+F", "브라우저/메모장 파일 메뉴 단축키와 충돌할 수 있습니다." },
        { "Alt+E", "브라우저/메모장 편집 메뉴 단축키와 충돌할 수 있습니다." },
        { "Alt+D", "브라우저 주소창 포커스 단축키와 충돌합니다." },
        { "Ctrl+C", "텍스트 복사 단축키와 충돌합니다." },
        { "Ctrl+V", "텍스트 붙여넣기 단축키와 충돌합니다." },
        { "Ctrl+X", "텍스트 잘라내기 단축키와 충돌합니다." },
        { "Ctrl+Z", "실행 취소(Undo) 단축키와 충돌합니다." },
        { "Ctrl+Y", "다시 실행(Redo) 단축키와 충돌합니다." },
        { "Ctrl+A", "전체 선택 단축키와 충돌합니다." },
        { "Ctrl+S", "문서 저장 단축키와 충돌합니다." },
        { "Ctrl+F", "찾기(검색) 단축키와 충돌합니다." },
        { "Ctrl+W", "탭/창 닫기 단축키와 충돌합니다." },
        { "Ctrl+T", "브라우저 새 탭 열기 단축키와 충돌합니다." },
        { "Ctrl+N", "새 창 열기 단축키와 충돌합니다." },
        { "Ctrl+P", "인쇄 단축키와 충돌합니다." },
        { "Shift+Space", "ChatHelper 자동완성 기본 트리거(Shift+Space)와 충돌합니다." },
        { "Ctrl+Alt+Delete", "윈도우 보안 및 작업관리자 시스템 단축키와 충돌합니다." },
        { "Ctrl+Shift+Esc", "작업 관리자 호출 단축키와 충돌합니다." },
        { "Win+L", "윈도우 화면 잠금 단축키와 충돌합니다." },
        { "Win+D", "바탕화면 보기 단축키와 충돌합니다." },
        { "Win+E", "파일 탐색기 열기 단축키와 충돌합니다." },
        { "Win+R", "실행 창 열기 단축키와 충돌합니다." }
    };

    public static (bool IsConflict, string WarningMessage) ValidateHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return (true, "⚠️ 단축키를 입력해 주세요. (예: Alt+Q)");

        var parts = hotkey.Trim()
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return (true, "⚠️ 조합키(Alt, Ctrl, Shift 중 1개 이상)와 일반 키를 '+'로 연결해 주세요. (예: Alt+Q)");
        }

        uint modifiers = 0;
        string? mainKey = null;

        foreach (var p in parts)
        {
            if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32Api.MOD_ALT;
            else if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32Api.MOD_CONTROL;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32Api.MOD_SHIFT;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32Api.MOD_WIN;
            else if (mainKey == null)
                mainKey = p.ToUpperInvariant();
            else
                return (true, $"⚠️ 일반 키가 2개 이상 입력되었습니다 ('{mainKey}', '{p}'). 예: Alt+Q");
        }

        if (modifiers == 0)
        {
            return (true, "⚠️ Alt, Ctrl, Shift 중 최소 1개의 조합키가 포함되어야 합니다. (예: Alt+Q)");
        }

        if (string.IsNullOrEmpty(mainKey) || !TryResolveVirtualKey(mainKey, out uint vk))
        {
            return (true, $"⚠️ 지원되지 않거나 잘못된 키 이름입니다 ('{mainKey ?? "없음"}'). 영문(A~Z), 숫자(0~9), F1~F12를 사용해 주세요.");
        }

        string canonical = BuildCanonicalName(modifiers, mainKey);

        if (KnownConflictHotkeys.TryGetValue(canonical, out var reason))
        {
            return (true, $"⚠️ 주의: '{canonical}' — {reason}");
        }

        // Alt+1~9 (GeForce Experience / 외부 오버레이 흔한 충돌 경고)
        if (modifiers == Win32Api.MOD_ALT && mainKey.Length == 1 && mainKey[0] >= '1' && mainKey[0] <= '9')
        {
            return (true, $"⚠️ 주의: '{canonical}'는 GeForce 오버레이나 브라우저 탭 전환 단축키와 충돌할 수 있습니다.");
        }

        // 실제 Windows OS에서 다른 프로그램이 이미 RegisterHotKey로 선점 중인지 실시간 검사
        if (!canonical.Equals(Constants.AppConstants.DefaultGlobalHotkey, StringComparison.OrdinalIgnoreCase))
        {
            const int probeHotkeyId = 0x7FFF;
            bool canRegister = Win32Api.RegisterHotKey(IntPtr.Zero, probeHotkeyId, modifiers | Win32Api.MOD_NOREPEAT, vk);
            if (canRegister)
            {
                Win32Api.UnregisterHotKey(IntPtr.Zero, probeHotkeyId);
            }
            else
            {
                return (true, $"⚠️ 주의: '{canonical}'는 현재 실행 중인 다른 프로그램 또는 Windows에서 이미 사용 중입니다.");
            }
        }

        return (false, $"✅ 사용 가능한 안전한 단축키입니다 ({canonical}).");
    }

    private static string BuildCanonicalName(uint modifiers, string mainKey)
    {
        var list = new List<string>();
        if ((modifiers & Win32Api.MOD_CONTROL) != 0) list.Add("Ctrl");
        if ((modifiers & Win32Api.MOD_ALT) != 0) list.Add("Alt");
        if ((modifiers & Win32Api.MOD_SHIFT) != 0) list.Add("Shift");
        if ((modifiers & Win32Api.MOD_WIN) != 0) list.Add("Win");
        list.Add(mainKey);
        return string.Join("+", list);
    }

    private static bool TryResolveVirtualKey(string keyName, out uint vk)
    {
        vk = 0;
        if (keyName.Length == 1)
        {
            char c = keyName[0];
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                vk = c;
                return true;
            }
        }

        if (keyName.StartsWith('F') && int.TryParse(keyName[1..], out int fNum) && fNum >= 1 && fNum <= 12)
        {
            vk = (uint)(0x70 + (fNum - 1));
            return true;
        }

        if (keyName.Equals("SPACE", StringComparison.OrdinalIgnoreCase))
        {
            vk = (uint)Win32Api.VK_SPACE;
            return true;
        }

        if (keyName.Equals("TAB", StringComparison.OrdinalIgnoreCase))
        {
            vk = (uint)Win32Api.VK_TAB;
            return true;
        }

        return false;
    }
}
