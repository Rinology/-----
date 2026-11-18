using System.IO;
using System.Net.Http;
using System.Text;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class AuthService
{
    private readonly ConfigManager _configManager;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    public UserInfo? CurrentUser { get; private set; }
    public List<UserInfo> Users { get; private set; } = new();

    public AuthService(ConfigManager configManager)
    {
        _configManager = configManager;
        LoadUsers();
    }

    public void LoadUsers()
    {
        var path = _configManager.Config.LocalUserCsvPath;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        if (!File.Exists(path))
        {
            Users.Clear();
            return;
        }

        var list = new List<UserInfo>();
        try
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length <= 1) return;

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split(',');
                if (cols.Length < 2) continue;

                var uid = cols[0].Trim();
                if (DateTime.TryParse(cols[1].Trim(), out var exp))
                {
                    var user = new UserInfo
                    {
                        UserId = uid,
                        ExpireDate = exp,
                        Status = cols.Length > 2 ? cols[2].Trim().ToLower() : "active",
                        Plan = cols.Length > 3 ? cols[3].Trim() : "Basic",
                        Memo = cols.Length > 4 ? cols[4].Trim() : ""
                    };
                    list.Add(user);
                }
            }

            Users = list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthService] 유저 로드 실패: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SyncUserListFromRemoteAsync()
    {
        var url = _configManager.Config.UserCsvUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return (false, "설정된 구글 스프레드시트 유저 목록 URL이 없습니다.");

        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var path = _configManager.Config.LocalUserCsvPath;
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            }

            await File.WriteAllTextAsync(path, response, Encoding.UTF8);
            LoadUsers();
            return (true, $"유저 라이선스 {Users.Count}건 동기화 성공!");
        }
        catch (Exception ex)
        {
            return (false, $"유저 목록 동기화 실패: {ex.Message}");
        }
    }

    public (bool Success, string Message, UserInfo? User) ValidateUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "아이디를 입력해 주세요.", null);

        var user = Users.FirstOrDefault(u => u.UserId.Equals(userId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user == null)
            return (false, "등록되지 않은 아이디입니다. 관리자에게 문의하세요.", null);

        if (user.Status != "active")
            return (false, "비활성화 또는 차단된 계정입니다. (사유: 관리자 차단)", user);

        if (user.IsExpired)
            return (false, $"이용 기간이 만료되었습니다. (만료일: {user.ExpireDate:yyyy-MM-dd})\n연장 문의가 필요합니다.", user);

        CurrentUser = user;
        _configManager.Config.CurrentUserId = user.UserId;
        _configManager.Save();

        return (true, $"인증 성공! (만료일: {user.ExpireDate:yyyy-MM-dd}, {user.DaysRemaining}일 남음)", user);
    }
}
