namespace ChatHelper.Models;

public class AppConfig
{
    // 버전 정보
    public string Version { get; set; } = "v2.0.0 (Millennium Edition)";
    public string BuildDate { get; set; } = "2026-11-18";

    // 로그인 정보
    public string CurrentUserId { get; set; } = "taerin";
    public string UserCsvUrl { get; set; } = "";
    public string LocalUserCsvPath { get; set; } = "users.csv";

    // UI 테마: "Win98" (기본: 윈도우 98 레트로), "ModernDark" (다크), "ModernLight" (라이트)
    public string Theme { get; set; } = "Win98";

    // 작업표시줄 및 화면 상태표시바 표시 설정 (true: 상태표시창&작업표시줄 표시, false: 트레이 아이콘으로만 숨김)
    public bool ShowStatusWindow { get; set; } = true;

    // 자동완성 활성화 여부
    public bool IsEnabled { get; set; } = true;

    // 단축키 설정
    public string TriggerKey { get; set; } = "\\";
    public string CompleteKeyMode { get; set; } = "SlashTab"; // SlashTab(\+Tab), SlashOnly(\), ShiftSpace
    public bool EnableNumberSelection { get; set; } = true;
    public string GlobalHotkey { get; set; } = "Alt+Q";

    // 템플릿 데이터 소스
    public string DisplayMode { get; set; } = "InlinePill";
    public string CsvFilePath { get; set; } = "chat_templates.csv";
    public string RemoteCsvUrl { get; set; } = "";
}
