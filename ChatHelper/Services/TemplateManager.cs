using System.IO;
using System.Net.Http;
using System.Text;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class TemplateManager
{
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

    public void AddTemplate(TemplateItem item)
    {
        Templates.Add(item);
        SaveTemplatesToCsv();
    }

    public void UpdateTemplate(int index, TemplateItem item)
    {
        if (index >= 0 && index < Templates.Count)
        {
            Templates[index] = item;
            SaveTemplatesToCsv();
        }
    }

    public void DeleteTemplate(int index)
    {
        if (index >= 0 && index < Templates.Count)
        {
            Templates.RemoveAt(index);
            SaveTemplatesToCsv();
        }
    }

    public void SaveTemplatesToCsv()
    {
        var csvPath = _configManager.Config.CsvFilePath;
        if (!Path.IsPathRooted(csvPath))
        {
            csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, csvPath);
        }

        var sb = new StringBuilder();
        sb.AppendLine("keyword,title,content,category");

        foreach (var it in Templates)
        {
            string escContent = it.Content.Replace("\"", "\"\"");
            sb.AppendLine($"{it.Keyword},{it.Title},\"{escContent}\",{it.Category}");
        }

        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
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

        var token = rawToken.Trim().ToLower();

        var trigger = _configManager.Config.TriggerKey ?? "\\";
        if (!string.IsNullOrEmpty(trigger) && token.StartsWith(trigger))
        {
            token = token[trigger.Length..];
        }

        if (string.IsNullOrEmpty(token))
            return new List<TemplateItem>();

        var list = Templates.Where(t =>
            (!string.IsNullOrEmpty(t.Keyword) && t.Keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase)) ||
            t.Title.StartsWith(token, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        if (list.Count > 0)
        {
            matchedLen = token.Length;
        }

        return list;
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
