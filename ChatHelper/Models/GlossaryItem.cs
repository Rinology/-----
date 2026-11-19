namespace ChatHelper.Models;

/// <summary>
/// 사내 용어집(2번 시트) 한 행에 대응하는 모델.
/// </summary>
public class GlossaryItem
{
    public string Term { get; set; } = string.Empty;
    public string Aliases { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Guide { get; set; } = string.Empty;
    public string Category { get; set; } = "일반";
}
