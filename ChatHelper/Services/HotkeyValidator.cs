namespace ChatHelper.Services;

public static class HotkeyValidator
{
    private static readonly Dictionary<string, string> WindowsSystemHotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Alt+Tab", "윈도우 활성 창 전환 단축키와 충돌합니다." },
        { "Alt+F4", "윈도우 프로그램 강제 종료 단축키와 충돌합니다." },
        { "Ctrl+C", "텍스트 복사 단축키와 충돌합니다." },
        { "Ctrl+V", "텍스트 붙여넣기 단축키와 충돌합니다." },
        { "Ctrl+X", "텍스트 잘라내기 단축키와 충돌합니다." },
        { "Ctrl+Z", "실행 취소(Undo) 단축키와 충돌합니다." },
        { "Ctrl+A", "전체 선택 단축키와 충돌합니다." },
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
            return (false, "");

        var normalized = hotkey.Trim().Replace(" ", "");

        if (WindowsSystemHotkeys.TryGetValue(normalized, out var reason))
        {
            return (true, $"⚠️ 주의: '{normalized}'는 {reason}");
        }

        return (false, "✅ 안전한 단축키입니다.");
    }
}
