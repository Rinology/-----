using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChatHelper.Models;

namespace ChatHelper.Services;

/// <summary>
/// 사용자 입력 의도 -> 스프레드시트 키워드/내용 검색(알약 추천) + 로컬 AI 답변/키워드 추출 -> 시트 저장을 조율하는 엔진.
/// </summary>
public class SuggestionEngine
{
    private readonly TemplateManager _templateManager;
    private readonly KeywordRegistrationService _registrationService;
    private readonly GlossaryService? _glossaryService;
    private TemplateItem? _pendingSave;

    public SuggestionEngine(
        TemplateManager templateManager,
        KeywordRegistrationService? registrationService = null,
        GlossaryService? glossaryService = null)
    {
        _templateManager = templateManager;
        _registrationService = registrationService ?? new KeywordRegistrationService();
        _glossaryService = glossaryService;
    }

    public TemplateItem? PendingSave => _pendingSave;

    /// <summary>
    /// 사용자 입력(예: "화난 고객 달래주기", "반품안내")을 받아 관련 템플릿 알약들과 추천 답변을 반환합니다.
    /// </summary>
    public Task<(string Answer, List<TemplateItem> Pills, string InferredContentKeyword)> GenerateAsync(
        string userInput,
        bool useAiFallback = false,
        bool extractKeywordWithAi = false,
        CancellationToken cancellationToken = default)
    {
        string query = userInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            _pendingSave = null;
            return Task.FromResult((string.Empty, new List<TemplateItem>(), string.Empty));
        }

        var pills = SearchRelatedPills(query, maxResults: 5);
        var topMatch = pills.FirstOrDefault();
        string answer = topMatch?.Content ?? string.Empty;
        string inferredContentKw = !string.IsNullOrWhiteSpace(topMatch?.ContentKeyword)
            ? topMatch.ContentKeyword
            : (topMatch?.Title ?? query);

        _pendingSave = string.IsNullOrWhiteSpace(answer)
            ? null
            : new TemplateItem
            {
                Keyword = topMatch?.Keyword ?? string.Empty,
                ContentKeyword = inferredContentKw,
                Title = topMatch?.Title ?? query,
                Content = answer,
                Category = topMatch?.Category ?? "일반"
            };

        return Task.FromResult((answer, pills, inferredContentKw));
    }

    public List<TemplateItem> SearchRelatedPills(string query, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<TemplateItem>();

        var terms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var scored = _templateManager.Templates
            .Select(t => new { Item = t, Score = ScoreTemplate(t, query, terms) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => LevenshteinDistance(x.Item.Keyword, query))
            .Take(maxResults)
            .Select(x => x.Item)
            .ToList();

        return scored;
    }

    /// <summary>
    /// 확정된 답변을 로컬 템플릿과 Google Sheet(GAS 웹앱)에 저장합니다.
    /// </summary>
    public async Task<(bool Success, string Message)> CommitAsync(TemplateItem? itemOverride = null, CancellationToken cancellationToken = default)
    {
        var item = itemOverride ?? _pendingSave;
        if (item == null || string.IsNullOrWhiteSpace(item.Content))
        {
            return (false, "저장할 답변 내용이 없습니다.");
        }

        _templateManager.AddTemplate(item);
        return await _registrationService.RegisterAsync(item, cancellationToken);
    }

    private static int ScoreTemplate(TemplateItem item, string fullQuery, string[] terms)
    {
        int score = 0;
        string kw = item.Keyword?.ToLower() ?? string.Empty;
        string ckw = item.ContentKeyword?.ToLower() ?? string.Empty;
        string title = item.Title?.ToLower() ?? string.Empty;
        string content = item.Content?.ToLower() ?? string.Empty;
        string cat = item.Category?.ToLower() ?? string.Empty;
        string q = fullQuery.Trim().ToLower();

        // 공백 제거 정규화 (예: "배송안내" <-> "배송 일정 안내")
        string qNoSpace = q.Replace(" ", "");
        string titleNoSpace = title.Replace(" ", "");
        string ckwNoSpace = ckw.Replace(" ", "");
        string contentNoSpace = content.Replace(" ", "");

        if (kw == q) score += 100;
        else if (kw.StartsWith(q)) score += 70;
        else if (kw.Contains(q)) score += 50;

        if (!string.IsNullOrEmpty(ckw))
        {
            if (ckw == q || ckwNoSpace == qNoSpace) score += 85;
            else if (ckw.Contains(q) || ckwNoSpace.Contains(qNoSpace)) score += 45;
        }

        if (title.Contains(q) || titleNoSpace.Contains(qNoSpace)) score += 45;
        if (content.Contains(q) || contentNoSpace.Contains(qNoSpace)) score += 25;
        if (cat.Contains(q)) score += 15;

        foreach (var term in terms)
        {
            if (kw.Contains(term)) score += 20;
            if (ckw.Contains(term)) score += 18;
            if (title.Contains(term)) score += 15;
            if (content.Contains(term)) score += 10;
            if (cat.Contains(term)) score += 5;

            // 띄어쓰기 없이 붙어쓴 한국어(예: "배송안내") 대비 2글자 부분 토큰 매칭
            if (term.Length >= 4 && score == 0)
            {
                for (int i = 0; i <= term.Length - 2; i += 2)
                {
                    string chunk = term.Substring(i, 2);
                    if (title.Contains(chunk)) score += 12;
                    if (ckw.Contains(chunk)) score += 10;
                    if (cat.Contains(chunk)) score += 8;
                    if (content.Contains(chunk)) score += 4;
                }
            }
        }

        return score;
    }

    public static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }
}
