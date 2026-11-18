namespace ChatHelper.Models;

public class AppConfig
{
    // 트리거 접두사 키 (기본: "\", 사용자가 원하는 특수문자나 키로 변경 가능)
    public string TriggerKey { get; set; } = "\\";

    // 일반 Tab 키를 방해하지 않도록 트리거 키(\)가 눌렸을 때만 완성할지 여부
    public bool RequireTriggerForTab { get; set; } = true;

    // 트리거 키(\) 상태에서 1~9 숫자로 빠른 선택 허용
    public bool EnableNumberSelection { get; set; } = true;

    // 표시 모드: InlinePill (옵션1: 인라인 툴팁), HorizontalBar (옵션2: 가로 추천바), ManualOnly (수동)
    public string DisplayMode { get; set; } = "InlinePill";

    // 수동 오버레이 호출 핫키 (기본: Alt + Q)
    public string GlobalHotkey { get; set; } = "Alt+Q";

    // 로컬 템플릿 CSV 경로
    public string CsvFilePath { get; set; } = "chat_templates.csv";

    // 원격 구글 스프레드시트 웹게시 CSV URL
    public string RemoteCsvUrl { get; set; } = "";

    // (선택 기능) 채팅 기록 수집 활성화 여부
    public bool EnableChatLogging { get; set; } = false;
}
