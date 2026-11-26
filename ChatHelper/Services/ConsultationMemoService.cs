using System;
using System.Collections.Generic;
using System.Linq;
using ChatHelper.Models;

namespace ChatHelper.Services;

public enum MemoJoinMode
{
    Paragraph,  // 빈 줄로 구분 (\r\n\r\n)
    LineBreak,  // 줄바꿈 (\r\n)
    Numbered,   // 1. ...\r\n\r\n2. ...
    Bulleted    // • ...\r\n• ...
}

public class MemoEntry
{
    public string Keyword { get; set; } = string.Empty;
    public string Category { get; set; } = "메모";
    public string MemoNote { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 상담 중 스무고개 방식의 다중 키워드 좁히기 필터링과 메모 조각들의 최종 문장 조합을 담당합니다.
/// </summary>
public static class ConsultationMemoService
{
    /// <summary>
    /// 누적된 키워드 칩 목록과 현재 입력 중인 검색어를 모두 만족(AND)하는 템플릿 후보를 점수순으로 반환합니다.
    /// </summary>
    public static List<TemplateItem> FilterTemplates(
        IEnumerable<TemplateItem> templates,
        IReadOnlyList<string> keywordChips,
        string? currentInput)
    {
        var allTerms = new List<string>();
        if (keywordChips != null)
        {
            foreach (var chip in keywordChips)
            {
                if (!string.IsNullOrWhiteSpace(chip))
                    allTerms.Add(chip.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(currentInput))
        {
            foreach (var part in currentInput.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                allTerms.Add(part.Trim());
            }
        }

        if (allTerms.Count == 0)
        {
            return templates.ToList();
        }

        var scored = new List<(TemplateItem Item, int Score)>();
        foreach (var item in templates)
        {
            int totalScore = 0;
            bool allMatched = true;

            foreach (var term in allTerms)
            {
                int termScore = ScoreSingleTerm(item, term);
                if (termScore <= 0)
                {
                    allMatched = false;
                    break;
                }
                totalScore += termScore;
            }

            if (allMatched)
            {
                scored.Add((item, totalScore));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Keyword, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Item)
            .ToList();
    }

    private static int ScoreSingleTerm(TemplateItem item, string term)
    {
        string q = term.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) return 0;
        string qNoSpace = q.Replace(" ", "");

        string kw = item.Keyword?.ToLowerInvariant() ?? string.Empty;
        string ckw = item.ContentKeyword?.ToLowerInvariant() ?? string.Empty;
        string title = item.Title?.ToLowerInvariant() ?? string.Empty;
        string content = item.Content?.ToLowerInvariant() ?? string.Empty;
        string cat = item.Category?.ToLowerInvariant() ?? string.Empty;

        int score = 0;
        if (kw == q) score += 100;
        else if (kw.StartsWith(q)) score += 70;
        else if (kw.Contains(q)) score += 50;

        if (ckw.Contains(q) || ckw.Replace(" ", "").Contains(qNoSpace)) score += 45;
        if (title.Contains(q) || title.Replace(" ", "").Contains(qNoSpace)) score += 40;
        if (cat.Contains(q)) score += 20;
        if (content.Contains(q) || content.Replace(" ", "").Contains(qNoSpace)) score += 15;

        if (score == 0 && HangulAssembler.IsChoseongQuery(q))
        {
            string kwCho = HangulAssembler.ExtractChoseong(item.Keyword).Replace(" ", "").ToLowerInvariant();
            string ckwCho = HangulAssembler.ExtractChoseong(item.ContentKeyword).Replace(" ", "").ToLowerInvariant();
            string titleCho = HangulAssembler.ExtractChoseong(item.Title).Replace(" ", "").ToLowerInvariant();
            if (kwCho.Contains(qNoSpace) || ckwCho.Contains(qNoSpace) || titleCho.Contains(qNoSpace))
                score += 35;
        }

        return score;
    }

    /// <summary>
    /// 수집된 메모/답변 조각들을 지정된 결합 형식에 맞게 하나의 최종 완성 텍스트로 합칩니다.
    /// </summary>
    public static string ComposeFinalText(IEnumerable<MemoEntry> entries, MemoJoinMode joinMode)
    {
        var valid = entries?
            .Select(e => string.IsNullOrWhiteSpace(e.Content) ? e.MemoNote?.Trim() ?? string.Empty : e.Content.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList() ?? new List<string>();

        if (valid.Count == 0)
            return string.Empty;

        return joinMode switch
        {
            MemoJoinMode.LineBreak => string.Join("\r\n", valid),
            MemoJoinMode.Numbered => string.Join("\r\n\r\n", valid.Select((t, i) => $"{i + 1}. {t}")),
            MemoJoinMode.Bulleted => string.Join("\r\n", valid.Select(t => $"• {t}")),
            _ => string.Join("\r\n\r\n", valid)
        };
    }
}
