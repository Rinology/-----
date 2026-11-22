namespace ChatHelper.Models;

public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime ExpireDate { get; set; }
    public string Status { get; set; } = "active";
    public string Plan { get; set; } = "Basic";
    public string Memo { get; set; } = string.Empty;
    /// <summary>접근 허용 업무 프로필 목록 (비어 있거나 ALL/* 이면 전체 허용, 예: "물류", "CS상담", "물류,CS상담")</summary>
    public string AllowedProfiles { get; set; } = string.Empty;

    public bool IsExpired => DateTime.Now.Date > ExpireDate.Date;
    public int DaysRemaining => Math.Max(0, (ExpireDate.Date - DateTime.Now.Date).Days);

    public bool CanAccessProfile(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(AllowedProfiles)) return true;
        string raw = AllowedProfiles.Trim();
        if (raw.Equals("ALL", StringComparison.OrdinalIgnoreCase) || raw == "*" || raw == "전체")
            return true;
        if (string.IsNullOrWhiteSpace(profileName)) return false;

        var tokens = raw.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            if (string.Equals(t.Trim(), profileName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
