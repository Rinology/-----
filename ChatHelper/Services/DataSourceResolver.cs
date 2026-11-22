using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatHelper.Models;

namespace ChatHelper.Services;

/// <summary>
/// 현재 활성 프로필 기준으로 읽기/쓰기 대상 시트를 결정한다.
/// 프로필 출처(우선순위):
/// 1) GAS(doGet?action=profiles) 에서 내려받는 중앙 프로필 목록 (방법 B)
/// 2) .env 에 등록된 PROFILE_<키>_NAME (있을 경우)
/// 3) 사용자가 제어판 고급 설정에서 직접 추가한 프로필 (config.json)
/// </summary>
public static class DataSourceResolver
{
    public const string DefaultProfileName = "기본(.env)";
    public const string DefaultTemplateTab = "채팅템플릿";
    public const string DefaultGlossaryTab = "용어집";
    public const string DefaultAiRulesTab = "톤앤매너";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static ConfigManager? Config { get; set; }
    public static List<DataSourceProfile> RemoteProfiles { get; private set; } = new();
    private static string _allowedProfilesRaw = string.Empty;

    /// <summary>로그인한 계정의 허용 프로필 문자열 설정 (비어 있거나 ALL/* 이면 전체 허용)</summary>
    public static void SetAllowedProfiles(string? allowedProfiles)
    {
        _allowedProfilesRaw = allowedProfiles?.Trim() ?? string.Empty;
    }

    public static bool IsProfileAllowed(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(_allowedProfilesRaw)) return true;
        if (_allowedProfilesRaw.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
            _allowedProfilesRaw == "*" || _allowedProfilesRaw == "전체")
            return true;
        if (string.IsNullOrWhiteSpace(profileName)) return false;

        var tokens = _allowedProfilesRaw.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => string.Equals(t.Trim(), profileName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>활성 프로필이 바뀌었거나 수정되어 재동기화가 필요할 때 발생.</summary>
    public static event Action? ProfileChanged;

    public static void RaiseChanged() => ProfileChanged?.Invoke();

    /// <summary>GAS ?action=profiles 에서 중앙 프로필 목록을 받아온다. 실패 시 기존 목록 유지.</summary>
    public static async Task<bool> RefreshRemoteProfilesAsync(CancellationToken ct = default)
    {
        string webhook = WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhook)) return false;

        try
        {
            string sep = webhook.Contains('?') ? "&" : "?";
            string json = await HttpService.ExternalClient.GetStringAsync(
                $"{webhook}{sep}action=profiles&token={Uri.EscapeDataString(ApiToken)}", ct);
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("[")) return false;

            var list = JsonSerializer.Deserialize<List<DataSourceProfile>>(json, JsonOpts);
            if (list == null) return false;

            RemoteProfiles = list
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(ExtractId(p.SpreadsheetUrl)))
                .ToList();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSourceResolver", "GAS 프로필 목록 조회 실패 (기존 설정 사용)", ex);
            return false;
        }
    }

    public static List<DataSourceProfile> GetEnvPresets()
    {
        var keys = EnvService.GetAllKeys();
        var ids = keys
            .Select(k => Regex.Match(k, @"^PROFILE_([A-Za-z0-9_]+)_NAME$", RegexOptions.IgnoreCase))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);

        var list = new List<DataSourceProfile>();
        foreach (var id in ids)
        {
            string name = EnvService.Get($"PROFILE_{id}_NAME").Trim();
            string url = EnvService.Get($"PROFILE_{id}_SHEET_URL").Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ExtractId(url))) continue;

            list.Add(new DataSourceProfile
            {
                Name = name,
                SpreadsheetUrl = url,
                TemplateTab = EnvService.Get($"PROFILE_{id}_TEMPLATE_TAB").Trim(),
                GlossaryTab = EnvService.Get($"PROFILE_{id}_GLOSSARY_TAB").Trim(),
                AiRulesTab = EnvService.Get($"PROFILE_{id}_RULES_TAB").Trim()
            });
        }
        return list;
    }

    /// <summary>GAS 중앙 목록 + .env 프리셋 + 사용자 프로필 중 현재 계정에 허용된 프로필만 반환.</summary>
    public static List<(DataSourceProfile Profile, string Source)> GetAllProfiles()
    {
        var result = new List<(DataSourceProfile, string)>();
        void AddUnique(IEnumerable<DataSourceProfile> items, string src)
        {
            foreach (var p in items)
            {
                if (!IsProfileAllowed(p.Name)) continue;
                if (!result.Any(x => string.Equals(x.Item1.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                    result.Add((p, src));
            }
        }

        AddUnique(RemoteProfiles, "GAS");
        AddUnique(GetEnvPresets(), "ENV");
        AddUnique(Config?.Config.DataSourceProfiles ?? new List<DataSourceProfile>(), "USER");
        return result;
    }

    public static DataSourceProfile? Active
    {
        get
        {
            var cfg = Config?.Config;
            var all = GetAllProfiles();
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ActiveDataSourceProfile))
            {
                var found = all.FirstOrDefault(x =>
                    string.Equals(x.Profile.Name, cfg.ActiveDataSourceProfile, StringComparison.OrdinalIgnoreCase)).Profile;
                if (found != null && !string.IsNullOrWhiteSpace(ExtractId(found.SpreadsheetUrl))) return found;
            }

            // 선택된 프로필이 없거나 권한이 없으면 허용된 첫 번째 프로필을 자동 선택
            return all.Count > 0 ? all[0].Profile : null;
        }
    }

    public static string ActiveName => Active?.Name ?? DefaultProfileName;

    // ---------- 읽기 URL ----------
    public static string TemplateUrl => ResolveReadUrl(Active, p => TabOf(p.TemplateTab, DefaultTemplateTab), "TEMPLATE_SHEET_URL");
    public static string GlossaryUrl => ResolveReadUrl(Active, p => TabOf(p.GlossaryTab, DefaultGlossaryTab), "GLOSSARY_SHEET_URL");
    public static string AiRulesUrl => ResolveReadUrl(Active, p => TabOf(p.AiRulesTab, DefaultAiRulesTab), "AI_RULES_SHEET_URL");

    private static string ResolveReadUrl(DataSourceProfile? p, Func<DataSourceProfile, string> tabSelector, string envFallbackKey)
    {
        if (p == null) return EnvService.Get(envFallbackKey);
        string tab = tabSelector(p);

        // GAS 중앙 프로필이거나 Webhook 이 있으면 GAS doGet(read) 로 비공개 시트도 안전하게 읽음
        bool isRemote = RemoteProfiles.Any(r => string.Equals(r.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (isRemote && !string.IsNullOrWhiteSpace(WebhookUrl))
            return BuildGasReadUrl(WebhookUrl, p.SpreadsheetUrl, tab);

        return BuildReadUrl(p.SpreadsheetUrl, tab);
    }

    // ---------- 쓰기 대상 (GAS 로 전달) ----------
    public static string WebhookUrl
    {
        get
        {
            var env = EnvService.Get("SHEET_WRITE_WEBHOOK_URL");
            if (!string.IsNullOrWhiteSpace(env) && !env.Contains("YOUR_DEPLOYMENT_ID", StringComparison.OrdinalIgnoreCase))
                return env.Trim();
            var cfg = Config?.Config.SheetUploadUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(cfg)) return cfg;
            return Constants.AppConstants.DefaultGasWebhookUrl;
        }
    }

    public static string ApiToken
    {
        get
        {
            var env = EnvService.Get("GAS_API_TOKEN");
            return !string.IsNullOrWhiteSpace(env) ? env.Trim() : Constants.AppConstants.DefaultGasApiToken;
        }
    }

    public static string SpreadsheetUrl => Active?.SpreadsheetUrl.Trim() ?? "";
    public static string TemplateSheetName => Active is { } p ? TabOf(p.TemplateTab, DefaultTemplateTab) : "";
    public static string GlossarySheetName => Active is { } p ? TabOf(p.GlossaryTab, DefaultGlossaryTab) : DefaultGlossaryTab;

    /// <summary>프로필별 로컬 캐시 파일 경로를 반환한다. (기본 프로필은 기존 경로 유지)</summary>
    public static string GetScopedCachePath(string baseFilePath)
    {
        string profileName = Config?.Config.ActiveDataSourceProfile?.Trim() ?? "";
        if (string.IsNullOrEmpty(profileName)) return baseFilePath;

        string safe = Regex.Replace(profileName, @"[\\/:*?""<>|\s]+", "_").Trim('_');
        if (string.IsNullOrEmpty(safe)) return baseFilePath;

        string dir = Path.GetDirectoryName(baseFilePath) ?? "";
        string stem = Path.GetFileNameWithoutExtension(baseFilePath);
        string ext = Path.GetExtension(baseFilePath);
        return Path.Combine(dir, $"{stem}_{safe}{ext}");
    }

    private static string TabOf(string? tab, string fallback) => string.IsNullOrWhiteSpace(tab) ? fallback : tab.Trim();

    public static string ExtractId(string? url)
    {
        var m = Regex.Match(url ?? "", @"/spreadsheets/d/([A-Za-z0-9_-]+)");
        return m.Success && m.Groups[1].Value != "e" ? m.Groups[1].Value : "";
    }

    public static string BuildGasReadUrl(string webhookUrl, string spreadsheetUrl, string tabName)
    {
        string sep = webhookUrl.Contains('?') ? "&" : "?";
        return $"{webhookUrl}{sep}action=read&token={Uri.EscapeDataString(ApiToken)}&spreadsheetUrl={Uri.EscapeDataString(spreadsheetUrl)}&sheet={Uri.EscapeDataString(tabName)}";
    }

    public static string BuildGasUsersReadUrl()
    {
        string webhook = WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhook)) return "";
        string sep = webhook.Contains('?') ? "&" : "?";
        return $"{webhook}{sep}action=users&token={Uri.EscapeDataString(ApiToken)}";
    }

    public static string BuildReadUrl(string spreadsheetUrl, string tabName) =>
        $"https://docs.google.com/spreadsheets/d/{ExtractId(spreadsheetUrl)}/gviz/tq?tqx=out:csv&headers=1&sheet={Uri.EscapeDataString(tabName)}";
}
