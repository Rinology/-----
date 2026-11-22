namespace ChatHelper.Models;

/// <summary>
/// 상황별 데이터 소스 프로필. 스프레드시트 1개(탭 3개)를 가리키며 읽기/쓰기 모두 이 시트를 사용한다.
/// 예) 프로필 "CS상담" / 프로필 "물류"
/// 기본값(프로필 미선택)은 .env 에 적힌 시트 3개이다.
/// </summary>
public class DataSourceProfile
{
    public string Name { get; set; } = "";

    // 이 프로필의 스프레드시트 주소 (브라우저 주소창의 편집 URL)
    public string SpreadsheetUrl { get; set; } = "";

    // 탭 이름 (비우면 기본 이름 사용)
    public string TemplateTab { get; set; } = "";  // 키워드(채팅템플릿)  기본: 채팅템플릿
    public string GlossaryTab { get; set; } = "";  // 용어집              기본: 용어집
    public string AiRulesTab { get; set; } = "";   // 톤앤매너            기본: 톤앤매너
    public string OwnerId { get; set; } = "";      // GAS 소유자 아이디 (ALL 또는 본인 ID)
    public bool IsPersonal { get; set; } = false;  // 개인 시트 여부
    public bool IsLocalOnly { get; set; } = false; // 구글 시트 없이 이 PC에만 저장하는 로컬 전용 목록
}
