namespace ChatHelper.Models;

public class AppConfig
{
    // 버전 정보
    public string Version { get; set; } = "v2.0.0 (Millennium Edition)";
    public string BuildDate { get; set; } = "2026-11-18";

    // 로그인 및 세션 관리
    public string CurrentUserId { get; set; } = "taerin";
    public bool KeepLoggedIn { get; set; } = false; // 로그인 상태 유지 여부
    public string UserCsvUrl { get; set; } = "";
    public string LocalUserCsvPath { get; set; } = "users.csv";

    // UI 테마: Windows 98 ME 클래식 고정
    public string Theme { get; set; } = "Win98";

    // 가로형 멀티 추천 바 최대 표시 개수 (3 ~ 10개, 기본 5개)
    public int MaxCandidatesCount { get; set; } = 5;

    // 투명도 설정 (바탕화면 은은하게 비침 조절)
    public double StatusWindowOpacity { get; set; } = 0.90; // 활성 상태 프로그램창(우측하단) 투명도 (0.4 ~ 1.0)
    public double WindowOpacity { get; set; } = 0.95; // 알약 및 검색창 투명도 (0.6 ~ 1.0)

    // 알약 크기 배율 (1: 1.0배 보통, 2: 2.0배 큼(기본 권장), 3: 3.0배 아주 큼)
    public int PillScale { get; set; } = 2;

    // 알약 위치 및 간격 설정 (채팅 방해 방지)
    public string PillPlacement { get; set; } = "CursorNearby"; // CursorNearby, ScreenBottomRight, ScreenTopCenter
    public int PillOffsetY { get; set; } = 35; // 커서 기준 Y 오프셋 (기본 +35px)

    // 작업표시줄 및 화면 상태표시바 표시 설정 (true: 상태표시창&작업표시줄 표시, false: 트레이 아이콘으로만 숨김)
    public bool ShowStatusWindow { get; set; } = true;

    // 자동완성 활성화 여부
    public bool IsEnabled { get; set; } = true;

    // 단축키 설정
    public string TriggerKey { get; set; } = "\\";
    public string CompleteKeyMode { get; set; } = "SlashTab"; // SlashTab(\+Tab), SlashOnly(\), ShiftSpace
    public bool EnableNumberSelection { get; set; } = true;
    public string GlobalHotkey { get; set; } = "Alt+Q";

    // 템플릿 데이터 소스 및 스프레드시트 업로드
    public string DisplayMode { get; set; } = "InlinePill";
    public string CsvFilePath { get; set; } = "chat_templates.csv";
    public string RemoteCsvUrl { get; set; } = "";
    public string SheetUploadUrl { get; set; } = ""; // 스프레드시트 새 행 등록(웹훅/Apps Script) URL
    public bool EnableLocalAi { get; set; } = false; // 로컬 AI 자연어 추천 기능 사용 여부
}
