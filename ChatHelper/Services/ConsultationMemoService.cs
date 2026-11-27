using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// 템플릿 본문에 {메모}, {1}, {날짜} 등 자리표시자({키워드})가 있으면 자유 메모 값을 순서대로 자동 치환합니다.
    /// </summary>
    public static string ComposeFinalText(IEnumerable<MemoEntry> entries, MemoJoinMode joinMode)
    {
        var list = entries?.ToList() ?? new List<MemoEntry>();
        if (list.Count == 0)
            return string.Empty;

        // 자유 메모(Category == "메모")와 템플릿 블록 분리
        var freeMemos = new List<(int EntryIndex, string Value)>();
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Category, "메모", StringComparison.OrdinalIgnoreCase))
            {
                string val = !string.IsNullOrWhiteSpace(list[i].Content)
                    ? list[i].Content.Trim()
                    : (list[i].MemoNote?.Trim() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(val))
                    freeMemos.Add((i, val));
            }
        }

        var consumedEntryIndices = new HashSet<int>();
        var resolvedParts = new List<(int EntryIndex, string Text)>();
        int nextFreeIdx = 0;

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            string raw = string.IsNullOrWhiteSpace(e.Content) ? (e.MemoNote?.Trim() ?? string.Empty) : e.Content.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            if (!string.Equals(e.Category, "메모", StringComparison.OrdinalIgnoreCase) && freeMemos.Count > 0)
            {
                raw = ReplacePlaceholders(raw, freeMemos, consumedEntryIndices, ref nextFreeIdx);
            }

            resolvedParts.Add((i, raw));
        }

        var valid = resolvedParts
            .Where(p => !consumedEntryIndices.Contains(p.EntryIndex))
            .Select(p => p.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

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

    public static string DetectMemoKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "메모";
        string s = text.Trim();
        if (Regex.IsMatch(s, @"^\d{3,4}-\d{3,4}-\d{3,5}$") || (s.Count(char.IsDigit) >= 8 && !s.Any(c => c >= '\uAC00' && c <= '\uD7A3')))
            return "송장";
        if (Regex.IsMatch(s, @"(오늘|내일|모레|오전|오후|월|화|수|목|금|토|일|\d{1,2}월\s*\d{1,2}일|\d{1,2}/\d{1,2}|\d{1,2}:\d{2})"))
            return "날짜";
        if (Regex.IsMatch(s, @"(\d[\d,]*\s*원|무료|유료)"))
            return "금액";
        return "메모";
    }

    public static string DetectMemoLabel(string? text, int index)
    {
        return DetectMemoKind(text) switch
        {
            "송장" => "📦 송장",
            "날짜" => "📅 날짜",
            "금액" => "💰 금액",
            _ => $"📝 메모 {index}"
        };
    }

    public static string GetFriendlyVariableLabel(string varName)
    {
        if (string.IsNullOrWhiteSpace(varName)) return "📝 값";
        if (varName.Contains("송장") || varName.Contains("운송장") || varName.Contains("번호")) return $"📦 {varName}";
        if (varName.Contains("날짜") || varName.Contains("일정") || varName.Contains("시간")) return $"📅 {varName}";
        if (varName.Contains("금액") || varName.Contains("비용") || varName.Contains("가격")) return $"💰 {varName}";
        if (varName.Contains("모델") || varName.Contains("제품")) return $"🚲 {varName}";
        return $"📝 {varName}";
    }

    private static bool MatchesSemanticKind(string placeholderKey, string memoValue)
    {
        string kind = DetectMemoKind(memoValue);
        if ((placeholderKey.Contains("송장") || placeholderKey.Contains("운송장") || placeholderKey.Contains("번호")) && kind == "송장")
            return true;
        if ((placeholderKey.Contains("날짜") || placeholderKey.Contains("일정") || placeholderKey.Contains("시간")) && kind == "날짜")
            return true;
        if ((placeholderKey.Contains("금액") || placeholderKey.Contains("비용") || placeholderKey.Contains("가격")) && kind == "금액")
            return true;
        return false;
    }

    private static readonly Regex PlaceholderRegex = new(@"\{([^{}\r\n]{1,24})\}", RegexOptions.Compiled);

    public static bool ContainsPlaceholders(string? text)
        => !string.IsNullOrEmpty(text) && PlaceholderRegex.IsMatch(text);

    public static List<string> ExtractPlaceholderNames(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (Match m in PlaceholderRegex.Matches(text))
        {
            string key = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(key) && !list.Contains(key, StringComparer.OrdinalIgnoreCase))
                list.Add(key);
        }
        return list;
    }

    /// <summary>
    /// 템플릿 본문 내 {송장번호}, {1}, {메모} 등을 메모 HUD의 자유 메모 또는 짧은 한 줄 클립보드 값으로 자동 치환합니다.
    /// </summary>
    public static string ResolveTemplateWithMemosOrClipboard(
        string templateText,
        IEnumerable<MemoEntry>? entries,
        string? clipboardFallback,
        string? lastInjectedText = null)
    {
        if (string.IsNullOrEmpty(templateText) || !PlaceholderRegex.IsMatch(templateText))
            return templateText;

        var freeMemos = new List<(int EntryIndex, string Value)>();
        if (entries != null)
        {
            int idx = 0;
            foreach (var e in entries)
            {
                if (string.Equals(e.Category, "메모", StringComparison.OrdinalIgnoreCase))
                {
                    string val = !string.IsNullOrWhiteSpace(e.Content)
                        ? e.Content.Trim()
                        : (e.MemoNote?.Trim() ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(val))
                        freeMemos.Add((idx, val));
                }
                idx++;
            }
        }

        string resolved = templateText;
        if (freeMemos.Count > 0)
        {
            var consumed = new HashSet<int>();
            int nextIdx = 0;
            resolved = ReplacePlaceholders(resolved, freeMemos, consumed, ref nextIdx);
        }

        if (!string.IsNullOrWhiteSpace(clipboardFallback) && PlaceholderRegex.IsMatch(resolved))
        {
            string clip = clipboardFallback.Trim();
            bool isSelfInjected = !string.IsNullOrEmpty(lastInjectedText) &&
                string.Equals(clip.Replace("\r\n", "\n"), lastInjectedText.Trim().Replace("\r\n", "\n"), StringComparison.Ordinal);
            bool isSingleLineShort = !clip.Contains('\n') && !clip.Contains('\r') && clip.Length <= 60;
            if (!isSelfInjected && isSingleLineShort)
            {
                resolved = PlaceholderRegex.Replace(resolved, clip);
            }
        }

        return resolved;
    }

    /// <summary>
    /// 본문 내 {1}, {메모}, {날짜} 등의 자리표시자를 자유 메모 값으로 자동 치환합니다.
    /// </summary>
    public static string ReplacePlaceholders(
        string templateText,
        IReadOnlyList<(int EntryIndex, string Value)> freeMemos,
        HashSet<int> consumedEntryIndices,
        ref int nextFreeIdx)
    {
        if (string.IsNullOrEmpty(templateText) || freeMemos.Count == 0)
            return templateText;

        int localNext = nextFreeIdx;
        string result = PlaceholderRegex.Replace(templateText, match =>
        {
            string key = match.Groups[1].Value.Trim();

            // 1. 숫자 인덱스 {1}, {2}...
            if (int.TryParse(key, out int oneBased) && oneBased >= 1 && oneBased <= freeMemos.Count)
            {
                var target = freeMemos[oneBased - 1];
                consumedEntryIndices.Add(target.EntryIndex);
                return target.Value;
            }

            // 2. 의미 기반 자동 매칭 ({송장번호} <- 📦 송장, {날짜} <- 📅 날짜, {금액} <- 💰 금액)
            var semanticMatch = freeMemos.FirstOrDefault(m =>
                !consumedEntryIndices.Contains(m.EntryIndex) && MatchesSemanticKind(key, m.Value));
            if (!string.IsNullOrEmpty(semanticMatch.Value))
            {
                consumedEntryIndices.Add(semanticMatch.EntryIndex);
                return semanticMatch.Value;
            }

            // 3. 일반 자리표시자 {메모}, {모델} 등 -> 미소비 자유 메모 순서대로 배정
            while (localNext < freeMemos.Count && consumedEntryIndices.Contains(freeMemos[localNext].EntryIndex))
                localNext++;

            if (localNext < freeMemos.Count)
            {
                var target = freeMemos[localNext++];
                consumedEntryIndices.Add(target.EntryIndex);
                return target.Value;
            }

            return match.Value;
        });

        nextFreeIdx = localNext;
        return result;
    }
}
