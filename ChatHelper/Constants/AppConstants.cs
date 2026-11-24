namespace ChatHelper.Constants;

public static class AppConstants
{
    // 애플리케이션 메타데이터
    public const string AppName = "ChatHelper";
    public const string AppVersion = "v2.4.0";
    public const string DefaultTheme = "FluentLight";

    // 파일 및 리소스 경로
    public const string DefaultConfigFileName = "config.json";
    public const string DefaultCsvFileName = "chat_templates.csv";
    public const string DefaultUserCsvFileName = "users.csv";
    public const string AppIconPath = "Assets/app.ico";

    // 수치 제한 및 기본값 (Magic Numbers 제거)
    public const int MinCandidatesCount = 3;
    public const int MaxCandidatesCount = 10;
    public const int DefaultCandidatesCount = 5;

    public const double MinStatusWindowOpacity = 0.35;
    public const double MaxStatusWindowOpacity = 1.0;
    public const double DefaultStatusWindowOpacity = 0.85;

    public const double MinWindowOpacity = 0.50;
    public const double MaxWindowOpacity = 1.0;
    public const double DefaultWindowOpacity = 0.95;

    public const int PillScaleNormal = 1;
    public const int PillScaleLarge = 2;
    public const int PillScaleExtraLarge = 3;

    public const int PillOffsetTight = 15;
    public const int PillOffsetNormal = 35;
    public const int PillOffsetFar = 65;

    public const int AutoHideTimeoutMs = 12000;
    public const int MinAutoHideSeconds = 3;
    public const int MaxAutoHideSeconds = 20;
    public const int DefaultAutoHideSeconds = 12;
    public const int NetworkTimeoutSeconds = 8;

    // UI 메시지 및 타이틀
    public const string TitleQuickSearch = "템플릿 빠른 검색 및 추천";
    public const string TitleControlPanel = "ChatHelper 종합 제어판 & 환경 설정";
    public const string TitleLogin = "ChatHelper 사용자 로그온";
    public const string TitleStatusRunning = "ChatHelper - 실행 중";
    public const string TitleStatusPaused = "ChatHelper - 일시정지됨";

    public const string StatusRunningText = "자동완성 작동 중 (ON)";
    public const string StatusPausedText = "자동완성 일시정지 (OFF)";

    public const string QuickSearchHotKeyHint = "[단축키: Alt+S  |  Tab: 목록/카테고리 이동]";
    public const string QuickSearchBottomHelp = "↵ Enter: 입력  |  ↑/↓: 이동  |  Tab: 목록/카테고리 전환  |  Esc: 닫기";

    public const string NoticeSlashOnly = "※ '\\' 단독 즉시 실행 모드에서는 1번 항목이 즉시 치환되므로 추천 알약 형태가 [단일 알약 툴팁]으로 자동 고정됩니다.";
    public const string DefaultTriggerKey = "\\";
    public const string DefaultGlobalHotkey = "Alt+S";

    // 구글 스프레드시트 기본 식별자 및 URL 빌더 (AES 암호화 보관)
    public static string DefaultPublishedSheetId => Services.SecretVault.FallbackPublishedSheetId;
    public const string DefaultTemplateGid = "1212205674";
    public const string DefaultGlossaryGid = "661663576";
    public const string DefaultAiRulesGid = "0";

    // GAS 통합 미들웨어 웹앱 기본 URL & 사내 전용 API 토큰 (AES 암호화 보관)
    public static string DefaultGasWebhookUrl => Services.SecretVault.GasWebhookUrl;
    public static string DefaultGasApiToken => Services.SecretVault.GasApiToken;

    public static string BuildPublishedCsvUrl(string sheetId, string gid) =>
        $"https://docs.google.com/spreadsheets/d/e/{sheetId}/pub?gid={gid}&single=true&output=csv";
}
