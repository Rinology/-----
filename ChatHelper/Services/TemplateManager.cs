using System.IO;
using System.Net.Http;
using System.Text;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class TemplateManager
{
    private static readonly Dictionary<char, char> EngToKorMap = new()
    {
        {'q', 'ㅂ'}, {'w', 'ㅈ'}, {'e', 'ㄷ'}, {'r', 'ㄱ'}, {'t', 'ㅅ'}, {'y', 'ㅛ'}, {'u', 'ㅕ'}, {'i', 'ㅑ'}, {'o', 'ㅐ'}, {'p', 'ㅔ'},
        {'a', 'ㅁ'}, {'s', 'ㄴ'}, {'d', 'ㅇ'}, {'f', 'ㄹ'}, {'g', 'ㅎ'}, {'h', 'ㅗ'}, {'j', 'ㅓ'}, {'k', 'ㅏ'}, {'l', 'ㅣ'},
        {'z', 'ㅋ'}, {'x', 'ㅌ'}, {'c', 'ㅊ'}, {'v', 'ㅍ'}, {'b', 'ㅠ'}, {'n', 'ㅜ'}, {'m', 'ㅡ'},
        {'Q', 'ㅃ'}, {'W', 'ㅉ'}, {'E', 'ㄸ'}, {'R', 'ㄲ'}, {'T', 'ㅆ'}, {'O', 'ㅒ'}, {'P', 'ㅖ'}
    };

    private readonly ConfigManager _configManager;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public List<TemplateItem> Templates { get; private set; } = new();

    public TemplateManager(ConfigManager configManager)
    {
        _configManager = configManager;
        LoadTemplates();
    }

    public int LoadTemplates()
    {
        var csvPath = _configManager.Config.CsvFilePath;
        if (!Path.IsPathRooted(csvPath))
        {
            csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, csvPath);
        }

        if (!File.Exists(csvPath))
        {
            Templates.Clear();
            return 0;
        }

        var list = new List<TemplateItem>();
        try
        {
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
            if (lines.Length <= 1)
                return 0;

            // 헤더 파싱
            var header = lines[0].Split(',');
            int kwIdx = -1, titleIdx = -1, contentIdx = -1, catIdx = -1;

            for (int i = 0; i < header.Length; i++)
            {
                var h = header[i].Trim().ToLower();
                if (h.Contains("keyword") || h.Contains("키워드")) kwIdx = i;
                else if (h.Contains("title") || h.Contains("제목")) titleIdx = i;
                else if (h.Contains("content") || h.Contains("내용")) contentIdx = i;
                else if (h.Contains("category") || h.Contains("카테고리")) catIdx = i;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line);
                if (cols.Count == 0) continue;

                var item = new TemplateItem
                {
                    Keyword = kwIdx >= 0 && kwIdx < cols.Count ? cols[kwIdx].Trim() : "",
                    Title = titleIdx >= 0 && titleIdx < cols.Count ? cols[titleIdx].Trim() : "",
                    Content = contentIdx >= 0 && contentIdx < cols.Count ? cols[contentIdx] : "",
                    Category = catIdx >= 0 && catIdx < cols.Count ? cols[catIdx].Trim() : "일반"
                };

                if (string.IsNullOrWhiteSpace(item.Title))
                    item.Title = item.Keyword;

                if (!string.IsNullOrWhiteSpace(item.Keyword) || !string.IsNullOrWhiteSpace(item.Content))
                {
                    list.Add(item);
                }
            }

            Templates = list;
            System.Diagnostics.Debug.WriteLine($"[TemplateManager] {Templates.Count}개 템플릿 로드 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TemplateManager] 로드 에러: {ex.Message}");
        }

        return Templates.Count;
    }

    public async Task<(bool Success, string Message)> SyncFromRemoteAsync()
    {
        var url = _configManager.Config.RemoteCsvUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return (false, "설정된 구글 스프레드시트 CSV URL이 없습니다.");

        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var csvPath = _configManager.Config.CsvFilePath;
            if (!Path.IsPathRooted(csvPath))
            {
                csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, csvPath);
            }

            await File.WriteAllTextAsync(csvPath, response, Encoding.UTF8);
            var count = LoadTemplates();
            return (true, $"동기화 성공! {count}개 템플릿을 불러왔습니다.");
        }
        catch (Exception ex)
        {
            return (false, $"동기화 실패: {ex.Message}");
        }
    }

    public List<TemplateItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Templates;

        return Templates.Where(t => t.Matches(query)).ToList();
    }

    public List<TemplateItem> FindMatchingCandidates(string rawToken, out int matchedLen)
    {
        matchedLen = 0;
        if (string.IsNullOrWhiteSpace(rawToken))
            return new List<TemplateItem>();

        var trigger = _configManager.Config.TriggerKey ?? "\\";
        var token = rawToken;

        // 트리거 접두사가 있으면 제거하고 검색
        bool hasTrigger = false;
        if (!string.IsNullOrEmpty(trigger) && token.StartsWith(trigger))
        {
            token = token[trigger.Length..];
            hasTrigger = true;
        }

        if (string.IsNullOrEmpty(token))
            return new List<TemplateItem>();

        // 두벌식 변환 토큰 생성 (예: "ds" -> "ㅇㄴ")
        var korToken = ConvertEngToKor(token);

        var list = Templates.Where(t =>
            (!string.IsNullOrEmpty(t.Keyword) && (t.Keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase) || t.Keyword.StartsWith(korToken))) ||
            t.Title.StartsWith(token, StringComparison.OrdinalIgnoreCase) || t.Title.StartsWith(korToken)
        ).ToList();

        if (list.Count > 0)
        {
            matchedLen = token.Length + (hasTrigger ? trigger.Length : 0);
        }

        return list;
    }

    public static string ConvertEngToKor(string eng)
    {
        var sb = new StringBuilder();
        foreach (var c in eng)
        {
            if (EngToKorMap.TryGetValue(c, out var k))
                sb.Append(k);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    current.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
