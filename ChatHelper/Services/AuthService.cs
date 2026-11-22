using System.IO;
using System.Net.Http;
using System.Text;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class AuthService
{
    private readonly ConfigManager _configManager;
    private readonly HttpClient _httpClient = HttpService.ExternalClient;
    private readonly object _userLock = new();

    public const string DefaultSheetCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vTuIQPMJyX7V7ET37FjOe-mNcWtURoLt8wblf3tUVVZ0il4lzG2DbHw1GyTqOTkZLQDAgEKY39eHUuO/pub?gid=1989475712&single=true&output=csv";

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

        try
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            ParseUserCsv(content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthService] 유저 로드 실패: {ex.Message}");
        }
    }

    public void ParseUserCsv(string csvContent)
    {
        var records = CsvHelperService.ParseRecords(csvContent);
        if (records.Count <= 1) return;

        var header = records[0];
        int uidIdx = -1, pwIdx = -1, expIdx = -1, statusIdx = -1, planIdx = -1, memoIdx = -1, profilesIdx = -1;

        for (int i = 0; i < header.Count; i++)
        {
            var h = header[i].Trim().ToLower();
            if (h.Contains("user_id") || h.Contains("userid") || h.Contains("아이디")) uidIdx = i;
            else if (h.Contains("pw") || h.Contains("password") || h.Contains("pass") || h.Contains("비밀번호") || h.Contains("비번")) pwIdx = i;
            else if (h.Contains("expire") || h.Contains("만료")) expIdx = i;
            else if (h.Contains("status") || h.Contains("상태")) statusIdx = i;
            else if (h.Contains("plan") || h.Contains("등급") || h.Contains("플랜")) planIdx = i;
            else if (h.Contains("memo") || h.Contains("note") || h.Contains("메모") || h.Contains("비고")) memoIdx = i;
            else if (h.Contains("profile") || h.Contains("프로필") || h.Contains("권한") || h.Contains("부서")) profilesIdx = i;
        }

        // 헤더 매칭 실패 시 기본 열 인덱스 (user_id, pw, expire_date, status, plan, memo, allowed_profiles)
        if (uidIdx < 0) uidIdx = 0;
        if (pwIdx < 0 && header.Count > 1) pwIdx = 1;
        if (expIdx < 0 && header.Count > 2) expIdx = 2;
        if (statusIdx < 0 && header.Count > 3) statusIdx = 3;
        if (planIdx < 0 && header.Count > 4) planIdx = 4;
        if (memoIdx < 0 && header.Count > 5) memoIdx = 5;
        if (profilesIdx < 0 && header.Count > 6) profilesIdx = 6;

        var list = new List<UserInfo>();
        for (int i = 1; i < records.Count; i++)
        {
            var cols = records[i];
            if (cols.Count == 0) continue;

            string uid = uidIdx < cols.Count ? cols[uidIdx].Trim() : "";
            if (string.IsNullOrWhiteSpace(uid)) continue;

            string pw = pwIdx >= 0 && pwIdx < cols.Count ? cols[pwIdx].Trim() : "";
            string expStr = expIdx >= 0 && expIdx < cols.Count ? cols[expIdx].Trim() : "";
            string status = statusIdx >= 0 && statusIdx < cols.Count ? cols[statusIdx].Trim().ToLower() : "active";
            string plan = planIdx >= 0 && planIdx < cols.Count ? cols[planIdx].Trim() : "Basic";
            string memo = memoIdx >= 0 && memoIdx < cols.Count ? cols[memoIdx].Trim() : "";
            string allowedProfiles = profilesIdx >= 0 && profilesIdx < cols.Count ? cols[profilesIdx].Trim() : "";

            DateTime exp = DateTime.MaxValue;
            if (!string.IsNullOrEmpty(expStr) && DateTime.TryParse(expStr, out var parsedExp))
            {
                exp = parsedExp;
            }

            list.Add(new UserInfo
            {
                UserId = uid,
                Password = pw,
                ExpireDate = exp,
                Status = string.IsNullOrWhiteSpace(status) ? "active" : status,
                Plan = string.IsNullOrWhiteSpace(plan) ? "Basic" : plan,
                Memo = memo,
                AllowedProfiles = allowedProfiles
            });
        }

        lock (_userLock)
        {
            Users = list;
        }
    }

    public async Task<(bool Success, string Message)> SyncUserListFromRemoteAsync()
    {
        // 1. GAS 토큰 인증 비공개 시트 조회 최우선 (웹 게시/링크 공유 불필요)
        var gasUrl = DataSourceResolver.BuildGasUsersReadUrl();
        if (!string.IsNullOrWhiteSpace(gasUrl))
        {
            try
            {
                var gasCsv = await _httpClient.GetStringAsync(gasUrl);
                if (!string.IsNullOrWhiteSpace(gasCsv) &&
                    !gasCsv.StartsWith("Server error:", StringComparison.OrdinalIgnoreCase) &&
                    !gasCsv.StartsWith("ChatHelper GAS", StringComparison.OrdinalIgnoreCase))
                {
                    ParseUserCsv(gasCsv);
                    if (Users.Count > 0)
                    {
                        SaveLocalUserCache(gasCsv);
                        return (true, $"사용자 목록 {Users.Count}명 동기화 완료!");
                    }
                }
            }
            catch { /* GAS v5 미배포 시 기존 URL 폴백 */ }
        }

        // 2. .env 파일의 USER_SHEET_URL 폴백
        var url = EnvService.Get("USER_SHEET_URL");

        // 2. config.json 설정 확인
        if (string.IsNullOrWhiteSpace(url))
        {
            url = _configManager.Config.UserCsvUrl?.Trim();
        }

        // 3. 기본 구글 스프레드시트 URL 폴백
        if (string.IsNullOrWhiteSpace(url))
        {
            url = DefaultSheetCsvUrl;
        }

        try
        {
            var response = await _httpClient.GetStringAsync(url);
            if (string.IsNullOrWhiteSpace(response))
            {
                return (false, "구글 스프레드시트 응답이 비어있습니다.");
            }

            ParseUserCsv(response);

            // 로컬 파일 캐시 갱신
            var path = _configManager.Config.LocalUserCsvPath;
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            }
            await File.WriteAllTextAsync(path, response, Encoding.UTF8);

            return (true, $"구글 스프레드시트에서 유저 라이선스 {Users.Count}건 동기화 성공!");
        }
        catch (Exception ex)
        {
            return (false, $"유저 목록 동기화 실패 (오프라인 캐시 사용): {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message, UserInfo? User)> ValidateUserAsync(string userId, string password = "", bool syncFirst = true)
    {
        if (syncFirst)
        {
            await SyncUserListFromRemoteAsync();
        }

        return await Task.Run(() => ValidateUser(userId, password));
    }

    public (bool Success, string Message, UserInfo? User) ValidateUser(string userId, string password = "")
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "아이디를 입력해 주세요.", null);

        lock (_userLock)
        {
            var user = Users.FirstOrDefault(u => u.UserId.Equals(userId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user == null)
                return (false, "등록되지 않은 아이디입니다. 관리자에게 문의하세요.", null);

            // 비밀번호 검증 (서버/시트에 비밀번호가 등록되어 있는 경우 확인)
            if (!string.IsNullOrEmpty(user.Password))
            {
                if (string.IsNullOrEmpty(password))
                    return (false, "비밀번호를 입력해 주세요.", user);

                if (!string.Equals(user.Password.Trim(), password.Trim(), StringComparison.Ordinal))
                    return (false, "비밀번호가 일치하지 않습니다. 다시 확인해 주세요.", user);
            }

            if (user.Status != "active")
                return (false, "비활성화 또는 차단된 계정입니다. (사유: 관리자 차단)", user);

            if (user.IsExpired)
                return (false, $"이용 기간이 만료되었습니다. (만료일: {user.ExpireDate:yyyy-MM-dd})\n연장 문의가 필요합니다.", user);

            CurrentUser = user;
            DataSourceResolver.SetAllowedProfiles(user.AllowedProfiles);
            _configManager.Config.CurrentUserId = user.UserId;
            _configManager.Save();

            // 백그라운드로 마지막 로그인 일시(last_login) GAS 기록 (로그인 지연 0ms)
            _ = ReportLastLoginAsync(user.UserId);

            return (true, $"인증 성공! (만료일: {user.ExpireDate:yyyy-MM-dd}, {user.DaysRemaining}일 남음)", user);
        }
    }

    private void SaveLocalUserCache(string csv)
    {
        try
        {
            var path = _configManager.Config.LocalUserCsvPath;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            File.WriteAllText(path, csv, Encoding.UTF8);
        }
        catch { }
    }

    public event Action<string>? LastLoginReported;

    private async Task ReportLastLoginAsync(string userId)
    {
        try
        {
            string webhook = DataSourceResolver.WebhookUrl;
            if (string.IsNullOrWhiteSpace(webhook) || string.IsNullOrWhiteSpace(userId)) return;

            string sep = webhook.Contains('?') ? "&" : "?";
            string url = $"{webhook}{sep}action=update_last_login&token={Uri.EscapeDataString(DataSourceResolver.ApiToken)}&userId={Uri.EscapeDataString(userId.Trim())}";
            string resp = (await _httpClient.GetStringAsync(url)).Trim();
            if (resp.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                LastLoginReported?.Invoke($"✅ 로그인 일시 기록 완료 ({userId})");
            }
            else
            {
                AppLogger.Warn("AuthService", $"last_login 응답 오류: {resp}");
                LastLoginReported?.Invoke($"⚠️ 로그인 일시 기록 실패: {resp}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AuthService", "last_login 기록 예외", ex);
            LastLoginReported?.Invoke($"⚠️ 로그인 일시 기록 오류: {ex.Message}");
        }
    }
}
