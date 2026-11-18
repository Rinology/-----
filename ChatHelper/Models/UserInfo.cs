namespace ChatHelper.Models;

public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public DateTime ExpireDate { get; set; }
    public string Status { get; set; } = "active";
    public string Plan { get; set; } = "Basic";
    public string Memo { get; set; } = string.Empty;

    public bool IsExpired => DateTime.Now.Date > ExpireDate.Date;
    public int DaysRemaining => Math.Max(0, (ExpireDate.Date - DateTime.Now.Date).Days);
}
