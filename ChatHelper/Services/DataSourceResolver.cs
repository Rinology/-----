using System.Text.RegularExpressions;
using ChatHelper.Models;

namespace ChatHelper.Services;

/// <summary>
/// 현재 활성 프로필 기준으로 읽기/쓰기 대상 시트를 결정한다.
/// 프로필 출처:
/// 1) 개발자가 .env 에 사전 등록한 프리셋 (PROFILE_<키>_NAME, PROFILE_<키>_SHEET_URL, 선택: _TEMPLATE_TAB / _GLOSSARY_TAB / _RULES_TAB)
/// 2) 사용자가 제어판에서 직접 추가한 프로필 (config.json)
/// 3) 미선택 시 "기본(.env)" → 기존 TEMPLATE_SHEET_URL / GLOSSARY_SHEET_URL / AI_RULES_SHEET_URL + GAS 기본 시트
/// </summary>
public static class DataSourceResolver
{
    public const string DefaultProfileName = "기본(.env)";
    public const string DefaultTemplateTab = "채팅템플릿";
    public const string DefaultGlossaryTab = "용어집";
    public const string DefaultAiRulesTab = "톤앤매너";

    public static ConfigManager? Config { get; set; }

    /// <summary>활성 프로필이 바뀌었거나 수정되어 재동기화가 필요할 때 발생.</summary>
    public static event Action? ProfileChanged;

    public static void RaiseChanged() => ProfileChanged?.Invoke();

    /// <summary>.env 에 PROFILE_<키>_NAME 과 PROFILE_<키>_SHEET_URL 로 등록된 프리셋 목록.</summary>
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

    /// <summary>.env 프리셋 + 사용자 프로필을 합쳐 반환 (.env 프리셋 우선).</summary>
    public static List<(DataSourceProfile Profile, bool IsEnvPreset)> GetAllProfiles()
    {
        var result = new List<(DataSourceProfile, bool)>();
        foreach (var p in GetEnvPresets())
            result.Add((p, true));

        var userProfiles = Config?.Config.DataSourceProfiles ?? new List<DataSourceProfile>();
        foreach (var p in userProfiles)
        {
            if (result.Any(x => string.Equals(x.Item1.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            result.Add((p, false));
        }
        return result;
    }

    public static DataSourceProfile? Active
    {
        get
        {
            var cfg = Config?.Config;
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.ActiveDataSourceProfile)) return null;
            var match = GetAllProfiles().FirstOrDefault(x =>
                string.Equals(x.Profile.Name, cfg.ActiveDataSourceProfile, StringComparison.OrdinalIgnoreCase));
            var p = match.Profile;
            return p != null && !string.IsNullOrWhiteSpace(ExtractId(p.SpreadsheetUrl)) ? p : null;
        }
    }

    public static string ActiveName => Active?.Name ?? DefaultProfileName;

    // ---------- 읽기 URL ----------
    public static string TemplateUrl => Active is { } p ? BuildReadUrl(p.SpreadsheetUrl, TabOf(p.TemplateTab, DefaultTemplateTab)) : EnvService.Get("TEMPLATE_SHEET_URL");
    public static string GlossaryUrl => Active is { } p ? BuildReadUrl(p.SpreadsheetUrl, TabOf(p.GlossaryTab, DefaultGlossaryTab)) : EnvService.Get("GLOSSARY_SHEET_URL");
    public static string AiRulesUrl => Active is { } p ? BuildReadUrl(p.SpreadsheetUrl, TabOf(p.AiRulesTab, DefaultAiRulesTab)) : EnvService.Get("AI_RULES_SHEET_URL");

    // ---------- 쓰기 대상 (GAS 로 전달) ----------
    public static string WebhookUrl
    {
        get
        {
            var env = EnvService.Get("SHEET_WRITE_WEBHOOK_URL");
            if (!string.IsNullOrWhiteSpace(env) && !env.Contains("YOUR_DEPLOYMENT_ID", StringComparison.OrdinalIgnoreCase))
                return env.Trim();
            return Config?.Config.SheetUploadUrl?.Trim() ?? "";
        }
    }

    public static string SpreadsheetUrl => Active?.SpreadsheetUrl.Trim() ?? "";
    public static string TemplateSheetName => Active is { } p ? TabOf(p.TemplateTab, DefaultTemplateTab) : "";
    public static string GlossarySheetName => Active is { } p ? TabOf(p.GlossaryTab, DefaultGlossaryTab) : DefaultGlossaryTab;

    private static string TabOf(string? tab, string fallback) => string.IsNullOrWhiteSpace(tab) ? fallback : tab.Trim();

    public static string ExtractId(string? url)
    {
        var m = Regex.Match(url ?? "", @"/spreadsheets/d/([A-Za-z0-9_-]+)");
        return m.Success && m.Groups[1].Value != "e" ? m.Groups[1].Value : "";
    }

    /// <summary>탭 이름으로 CSV를 읽는 URL (시트가 '링크가 있는 모든 사용자 - 뷰어' 이상으로 공유돼야 함).</summary>
    public static string BuildReadUrl(string spreadsheetUrl, string tabName) =>
        $"https://docs.google.com/spreadsheets/d/{ExtractId(spreadsheetUrl)}/gviz/tq?tqx=out:csv&headers=1&sheet={Uri.EscapeDataString(tabName)}";
}
