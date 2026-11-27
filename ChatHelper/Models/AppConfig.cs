using System.Text.Json.Serialization;

namespace ChatHelper.Models;

public class AppConfig
{
    // 버전 정보
    public string Version { get; set; } = "v3.3.0";
    public string BuildDate { get; set; } = "2026-11-27";

    // 로그인 및 세션 관리 (보안상 평문 아이디는 디스크에 저장하지 않고 DPAPI 암호화 토큰만 선택 저장)
    [JsonIgnore]
    public string CurrentUserId { get; set; } = "";
    public string EncryptedUserId { get; set; } = "";
    public bool KeepLoggedIn { get; set; } = false; // 아이디 기억 여부
    public bool LogoutOnExit { get; set; } = false; // 종료 시 자동 로그아웃(아이디 기억 해제) 여부
    public string UserCsvUrl { get; set; } = "";
    public string LocalUserCsvPath { get; set; } = "users.csv";

    // UI 테마: FluentLight (Windows 11 Fluent 라이트 화이트 베이스) + 포인트 컬러
    public string Theme { get; set; } = "FluentLight";
    public string AccentColor { get; set; } = "Blue"; // Blue(기본), Green, Lavender, Peach, Pink

    // 가로형 멀티 추천 바 최대 표시 개수 (3 ~ 10개, 기본 5개)
    public int MaxCandidatesCount { get; set; } = 5;

    // 투명도 설정 (바탕화면 은은하게 비침 조절)
    public double StatusWindowOpacity { get; set; } = 0.90; // 활성 상태 프로그램창(우측하단) 투명도 (0.4 ~ 1.0)
    public double WindowOpacity { get; set; } = 0.95; // 알약 및 검색창 투명도 (0.6 ~ 1.0)
    public double MemoOpacity { get; set; } = 0.95; // 상담 메모 HUD 투명도 (0.6 ~ 1.0)

    // 알약 크기 배율 (1: 1.0x 보통, 2: 2.0x 큼(기본 권장), 3: 3.0x 아주 큼, 4: 4.0x 매우 큼, 5: 5.0x 최대)
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
    public string CompleteKeyMode { get; set; } = "SlashTab"; // SlashTab(\+Tab, 기본값), ShiftSpace(Shift+Space + Ctrl+숫자), SlashOnly(\)
    public bool EnableNumberSelection { get; set; } = true;
    public string GlobalHotkey { get; set; } = "Alt+S";
    public string MemoHotkey { get; set; } = "Alt+N";

    // 템플릿 데이터 소스 및 로컬 AI
    public string DisplayMode { get; set; } = "InlinePill";
    public string CsvFilePath { get; set; } = "chat_templates.csv";
    public string RemoteCsvUrl { get; set; } = "";
    public string SheetUploadUrl { get; set; } = ""; // 스프레드시트 새 행 등록(웹훅/Apps Script) URL

    // 구글 스프레드시트 주기적 자동 동기화 (분 단위, 기본 15분, 0이면 비활성화)
    public int AutoSyncIntervalMinutes { get; set; } = 15;

    // 추천 알약 자동 숨김 시간 (초 단위, 3 ~ 20초, 기본 12초)
    public int AutoHideSeconds { get; set; } = 12;

    // 시트 등록 시 상세 입력창을 띄우지 않고 즉시 저장 여부 (false: 상세 입력창, true: 즉시 저장)
    public bool QuickSaveWithoutDialog { get; set; } = false;

    // 최근 사용한 템플릿 키워드 (Alt+Q 상단 우선 표시, 최대 10개)
    public List<string> RecentKeywords { get; set; } = new();

    // 사용자 정의 특수문자 목록 (Alt+X 특수문자 전용 창에서 추가/삭제 관리)
    public List<string> CustomSymbols { get; set; } = new()
    {
        "→", "➔", "▶", "■", "□", "●", "★", "☆", "※", "✔", "✅", "📌", "📦", "📞", "😊", "🙏"
    };

    // 데이터 소스 프로필 (상황/사용자별 키워드·톤앤매너·용어집 스프레드시트 전환). 비어 있으면 .env 기본값 사용
    public List<DataSourceProfile> DataSourceProfiles { get; set; } = new();
    public string ActiveDataSourceProfile { get; set; } = "";

    // 로컬 AI 사칙연산 계산 및 양방향 번역(한↔영) · 상담 스무고개 메모 기능 ON/OFF
    public bool EnableMathCalc { get; set; } = true;
    public bool EnableKoToEnTranslation { get; set; } = false;
    public string TranslationDirection { get; set; } = "Auto"; // Auto(자동감지), KoToEn(한→영), EnToKo(영→한)
    public bool EnableConsultationMemo { get; set; } = true; // 상담 스무고개 메모 HUD ON/OFF (테스트용 기본 ON)
}
