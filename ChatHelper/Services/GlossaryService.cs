using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatHelper.Constants;
using ChatHelper.Models;

namespace ChatHelper.Services;

/// <summary>
/// 구글 스프레드시트 2번 시트(용어집) 동기화, RAG 검색, 추가/삭제 업로드를 담당하는 서비스.
/// </summary>
public class GlossaryService
{
    public static string DefaultGlossarySheetUrl =>
        AppConstants.BuildPublishedCsvUrl(AppConstants.DefaultPublishedSheetId, AppConstants.DefaultGlossaryGid);

    private readonly ConfigManager _configManager;
    private readonly HttpClient _httpClient = HttpService.ExternalClient;
    private readonly string? _customCachePath;

    public List<GlossaryItem> Terms { get; private set; } = new();

    public GlossaryService(ConfigManager configManager, string? localCachePath = null)
    {
        _configManager = configManager;
        _customCachePath = localCachePath;
        LoadFromLocalCache();
    }

    private string ResolveCachePath()
    {
        if (!string.IsNullOrWhiteSpace(_customCachePath)) return _customCachePath;
        string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "glossary_cache.csv");
        return DataSourceResolver.GetScopedCachePath(basePath);
    }

    public int LoadFromLocalCache()
    {
        string cachePath = ResolveCachePath();
        if (!File.Exists(cachePath))
            return 0;

        try
        {
            string csv = File.ReadAllText(cachePath, Encoding.UTF8);
            return ParseGlossaryCsv(csv);
        }
        catch
        {
            return 0;
        }
    }

    public int ParseGlossaryCsv(string csvText)
    {
        var list = new List<GlossaryItem>();
        try
        {
            var records = CsvHelperService.ParseRecords(csvText);
            if (records.Count <= 1)
            {
                Terms = list;
                return 0;
            }

            var header = records[0];
            int termIdx = -1, aliasIdx = -1, descIdx = -1, guideIdx = -1, catIdx = -1;

            for (int i = 0; i < header.Count; i++)
            {
                var h = header[i].Trim().ToLowerInvariant();
                if (h == "term" || h.Contains("용어") || h.Contains("단어")) termIdx = i;
                else if (h.Contains("alias") || h.Contains("동의어") || h.Contains("유사어")) aliasIdx = i;
                else if (h.Contains("desc") || h.Contains("설명") || h.Contains("내용")) descIdx = i;
                else if (h.Contains("guide") || h.Contains("주의") || h.Contains("가이드")) guideIdx = i;
                else if (h.Contains("cat") || h.Contains("분류") || h.Contains("카테고리")) catIdx = i;
            }

            if (termIdx < 0) termIdx = 0;
            if (descIdx < 0 && header.Count > 2) descIdx = 2;

            for (int i = 1; i < records.Count; i++)
            {
                var cols = records[i];
                if (cols.Count == 0) continue;

                string term = termIdx >= 0 && termIdx < cols.Count ? cols[termIdx].Trim() : "";
                string desc = descIdx >= 0 && descIdx < cols.Count ? cols[descIdx].Trim() : "";
                if (string.IsNullOrWhiteSpace(term) && string.IsNullOrWhiteSpace(desc))
                    continue;

                list.Add(new GlossaryItem
                {
                    Term = term,
                    Aliases = aliasIdx >= 0 && aliasIdx < cols.Count ? cols[aliasIdx].Trim() : "",
                    Description = desc,
                    Guide = guideIdx >= 0 && guideIdx < cols.Count ? cols[guideIdx].Trim() : "",
                    Category = catIdx >= 0 && catIdx < cols.Count && !string.IsNullOrWhiteSpace(cols[catIdx]) ? cols[catIdx].Trim() : "일반"
                });
            }

            Terms = list;
        }
        catch { }

        return Terms.Count;
    }

    public async Task<(bool Success, string Message)> SyncFromRemoteAsync(CancellationToken cancellationToken = default)
    {
        var url = DataSourceResolver.GlossaryUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = DefaultGlossarySheetUrl;

        try
        {
            var csv = await _httpClient.GetStringAsync(url, cancellationToken);
            if (string.IsNullOrWhiteSpace(csv))
                return (false, "용어집 시트 응답이 비어 있습니다.");

            int count = ParseGlossaryCsv(csv);
            return (true, $"용어집 {count}개 동기화 완료");
        }
        catch (Exception ex)
        {
            return (false, $"용어집 동기화 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 사용자 입력 문장에서 관련 사내 용어를 찾아 반환 (term, aliases, description 매칭)
    /// </summary>
    public List<GlossaryItem> FindRelevantTerms(string query, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || Terms.Count == 0)
            return new List<GlossaryItem>();

        string q = query.Trim().ToLowerInvariant();
        string qNoSpace = q.Replace(" ", "");
        var words = q.Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);

        var scored = new List<(GlossaryItem Item, int Score)>();

        foreach (var item in Terms)
        {
            int score = 0;
            string term = item.Term.ToLowerInvariant();
            string termNoSpace = term.Replace(" ", "");

            if (!string.IsNullOrEmpty(term))
            {
                if (q.Contains(term) || qNoSpace.Contains(termNoSpace) || term.Contains(q))
                    score += 100;
            }

            if (!string.IsNullOrEmpty(item.Aliases))
            {
                var aliases = item.Aliases.ToLowerInvariant()
                    .Split(new[] { ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim());
                foreach (var alias in aliases)
                {
                    if (string.IsNullOrEmpty(alias)) continue;
                    string aliasNoSpace = alias.Replace(" ", "");
                    if (q.Contains(alias) || qNoSpace.Contains(aliasNoSpace) || alias.Contains(q))
                    {
                        score += 80;
                    }
                }
            }

            string desc = item.Description.ToLowerInvariant();
            foreach (var w in words)
            {
                if (w.Length < 2) continue;
                if (term.Contains(w)) score += 30;
                if (desc.Contains(w)) score += 10;
            }

            if (score > 0)
            {
                scored.Add((item, score));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Item)
            .ToList();
    }

    public async Task<(bool Success, string Message)> UploadTermAsync(GlossaryItem item, CancellationToken cancellationToken = default)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Term) || string.IsNullOrWhiteSpace(item.Description))
            return (false, "용어(term)와 공식 설명(description)은 필수입니다.");

        var webhookUrl = new KeywordRegistrationService(_configManager, _httpClient).ResolveWebhookUrl();
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return (false, ".env의 SHEET_WRITE_WEBHOOK_URL이 설정되어 있지 않습니다.");

        Terms.Add(item);
        SaveLocalCache();

        var payload = new
        {
            token = DataSourceResolver.ApiToken,
            sheetName = DataSourceResolver.GlossarySheetName,
            target = "glossary",
            spreadsheetUrl = DataSourceResolver.SpreadsheetUrl,
            action = "append",
            term = item.Term.Trim(),
            aliases = item.Aliases?.Trim() ?? "",
            description = item.Description.Trim(),
            guide = item.Guide?.Trim() ?? "",
            category = string.IsNullOrWhiteSpace(item.Category) ? "일반" : item.Category.Trim()
        };

        return await PostToGasAsync(webhookUrl, payload, "용어집에 성공적으로 추가되었습니다!", cancellationToken);
    }

    public async Task<(bool Success, string Message)> DeleteTermAsync(string term, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return (false, "삭제할 용어(term)를 입력해 주세요.");

        var webhookUrl = new KeywordRegistrationService(_configManager, _httpClient).ResolveWebhookUrl();
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return (false, ".env의 SHEET_WRITE_WEBHOOK_URL이 설정되어 있지 않습니다.");

        Terms.RemoveAll(t => string.Equals(t.Term.Trim(), term.Trim(), StringComparison.OrdinalIgnoreCase));
        SaveLocalCache();

        var payload = new
        {
            token = DataSourceResolver.ApiToken,
            sheetName = DataSourceResolver.GlossarySheetName,
            target = "glossary",
            spreadsheetUrl = DataSourceResolver.SpreadsheetUrl,
            action = "delete",
            term = term.Trim()
        };

        return await PostToGasAsync(webhookUrl, payload, $"'{term}' 용어가 구글 시트에서 삭제되었습니다!", cancellationToken);
    }

    private async Task<(bool Success, string Message)> PostToGasAsync(string url, object payload, string okMsg, CancellationToken cancellationToken)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.PostAsync(url, content, cancellationToken);
            string body = (await resp.Content.ReadAsStringAsync(cancellationToken)).Trim();

            if (resp.IsSuccessStatusCode && body.Equals("OK", StringComparison.OrdinalIgnoreCase))
                return (true, okMsg);

            return (false, $"GAS 응답 오류 (HTTP {(int)resp.StatusCode}): {body}");
        }
        catch (Exception ex)
        {
            return (false, $"요청 실패: {ex.Message}");
        }
    }

    private void SaveLocalCache()
    {
        // 보안 정책에 따라 로컬 CSV 캐시 파일을 디스크에 저장하지 않음
    }

    private static string Esc(string? s) => CsvHelperService.EscapeField(s);
}
