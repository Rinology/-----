using System.IO;
using System.Net.Http;
using System.Text;
using ChatHelper.Constants;
using ChatHelper.Models;

namespace ChatHelper.Services;

public class TemplateManager
{
    public static string DefaultTemplateSheetUrl =>
        AppConstants.BuildPublishedCsvUrl(AppConstants.DefaultPublishedSheetId, AppConstants.DefaultTemplateGid);

    private readonly ConfigManager _configManager;
    private readonly HttpClient _httpClient = HttpService.ExternalClient;

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

        if (File.Exists(csvPath))
        {
            try
            {
                var csvText = File.ReadAllText(csvPath, Encoding.UTF8);
                return ParseTemplatesFromCsv(csvText);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TemplateManager", "로컬 템플릿 캐시 로드 에러", ex);
            }
        }

        return Templates.Count;
    }

    public int ParseTemplatesFromCsv(string csvText)
    {
        var list = new List<TemplateItem>();
        try
        {
            var records = ParseCsvRecords(csvText);
            if (records.Count <= 1)
            {
                Templates.Clear();
                return 0;
            }

            var header = records[0];
            int kwIdx = -1, contentKwIdx = -1, titleIdx = -1, contentIdx = -1, catIdx = -1;

            for (int i = 0; i < header.Count; i++)
            {
                var h = header[i].Trim().ToLower();
                if (h == "contentkeyword" || h.Contains("contentkeyword") || h.Contains("content_keyword") || h.Contains("본문키워드") || h.Contains("내용키워드"))
                    contentKwIdx = i;
                else if (h.Contains("keyword") || h.Contains("키워드"))
                    kwIdx = i;
                else if (h.Contains("title") || h.Contains("제목"))
                    titleIdx = i;
                else if (h.Contains("content") || h.Contains("내용"))
                    contentIdx = i;
                else if (h.Contains("category") || h.Contains("카테고리"))
                    catIdx = i;
            }

            for (int i = 1; i < records.Count; i++)
            {
                var cols = records[i];
                if (cols.Count == 0) continue;

                var rawContent = contentIdx >= 0 && contentIdx < cols.Count ? cols[contentIdx] : "";
                // 줄바꿈 이스케이프 문자 치환 및 CRLF 정규화
                rawContent = rawContent.Replace("\\n", "\n").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

                var item = new TemplateItem
                {
                    Keyword = kwIdx >= 0 && kwIdx < cols.Count ? cols[kwIdx].Trim() : "",
                    ContentKeyword = contentKwIdx >= 0 && contentKwIdx < cols.Count ? cols[contentKwIdx].Trim() : "",
                    Title = titleIdx >= 0 && titleIdx < cols.Count ? cols[titleIdx].Trim() : "",
                    Content = rawContent,
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
            System.Diagnostics.Debug.WriteLine($"[TemplateManager] {Templates.Count}개 템플릿 파싱 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TemplateManager] 파싱 에러: {ex.Message}");
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

    private void SaveTemplatesToCsv()
    {
        var csvPath = _configManager.Config.CsvFilePath;
        if (!Path.IsPathRooted(csvPath))
        {
            csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, csvPath);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Keyword,ContentKeyword,Title,Content,Category");
        foreach (var it in Templates)
        {
            sb.AppendLine($"{EscapeCsvField(it.Keyword)},{EscapeCsvField(it.ContentKeyword)},{EscapeCsvField(it.Title)},{EscapeCsvField(it.Content)},{EscapeCsvField(it.Category)}");
        }

        try
        {
            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    public async Task<(bool Success, string Message)> SyncFromRemoteAsync()
    {
        var url = EnvService.Get("TEMPLATE_SHEET_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            url = _configManager.Config.RemoteCsvUrl?.Trim();
        }
        if (string.IsNullOrWhiteSpace(url))
        {
            url = DefaultTemplateSheetUrl;
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return (false, "구글 스프레드시트 응답이 비어있습니다.");
                }

                var count = ParseTemplatesFromCsv(response);

                // 로컬 캐시 파일 보관
                try
                {
                    var csvPath = _configManager.Config.CsvFilePath;
                    if (!Path.IsPathRooted(csvPath))
                    {
                        csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, csvPath);
                    }
                    await File.WriteAllTextAsync(csvPath, response, Encoding.UTF8);
                }
                catch { }

                return (true, $"동기화 성공! {count}개 템플릿을 불러왔습니다.");
            }
            catch (Exception ex)
            {
                if (attempt == 2)
                {
                    AppLogger.Warn("TemplateManager", $"시트 동기화 실패 ({url})", ex);
                    return (false, $"동기화 실패: {ex.Message}");
                }
                await Task.Delay(800);
            }
        }

        return (false, "동기화 실패");
    }

    public List<string> GetCategories()
    {
        var cats = Templates
            .Select(t => string.IsNullOrWhiteSpace(t.Category) ? "일반" : t.Category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        cats.Insert(0, "전체");
        return cats;
    }

    public List<TemplateItem> Search(string query, string? category = null)
    {
        IEnumerable<TemplateItem> queryable = Templates;

        if (!string.IsNullOrWhiteSpace(category) && category != "전체")
        {
            queryable = queryable.Where(t => string.Equals(t.Category?.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return queryable.ToList();
        }

        var q = query.Trim().ToLower();
        bool isChoseong = HangulAssembler.IsChoseongQuery(q);
        string qNoSpace = q.Replace(" ", "");

        return queryable.Where(t =>
        {
            if (isChoseong)
            {
                string kwCho = HangulAssembler.ExtractChoseong(t.Keyword).Replace(" ", "").ToLower();
                string ckwCho = HangulAssembler.ExtractChoseong(t.ContentKeyword).Replace(" ", "").ToLower();
                string titleCho = HangulAssembler.ExtractChoseong(t.Title).Replace(" ", "").ToLower();
                if (kwCho.Contains(qNoSpace) || ckwCho.Contains(qNoSpace) || titleCho.Contains(qNoSpace))
                    return true;
            }

            return (!string.IsNullOrEmpty(t.Keyword) && t.Keyword.ToLower().Contains(q)) ||
                   (!string.IsNullOrEmpty(t.ContentKeyword) && t.ContentKeyword.ToLower().Contains(q)) ||
                   (!string.IsNullOrEmpty(t.Title) && t.Title.ToLower().Contains(q)) ||
                   (!string.IsNullOrEmpty(t.Content) && t.Content.ToLower().Contains(q)) ||
                   (!string.IsNullOrEmpty(t.Category) && t.Category.ToLower().Contains(q));
        }).ToList();
    }

    private static string EscapeCsvField(string? value) => CsvHelperService.EscapeField(value);

    public async Task<(bool Success, string Message)> UploadTemplateToRemoteSheetAsync(TemplateItem item, string? note = null)
    {
        var registrationService = new KeywordRegistrationService(_configManager, _httpClient);
        return await registrationService.RegisterAsync(item);
    }

    public List<TemplateItem> FindMatchingCandidates(string rawToken, out int matchedLen)
    {
        matchedLen = 0;
        if (string.IsNullOrWhiteSpace(rawToken))
            return new List<TemplateItem>();

        var token = rawToken.Trim().ToLower();
        int prefixLen = 0;

        // '\dz', '/dz', 또는 문장 중간에 입력된 '\dz', '/dz' 모두 정확히 추출
        int lastSlashIdx = token.LastIndexOfAny(new[] { '\\', '/' });
        var trigger = _configManager.Config.TriggerKey ?? "\\";
        if (lastSlashIdx >= 0)
        {
            token = token[(lastSlashIdx + 1)..];
            prefixLen = 1;
        }
        else if (!string.IsNullOrEmpty(trigger) && token.StartsWith(trigger))
        {
            token = token[trigger.Length..];
            prefixLen = trigger.Length;
        }

        if (string.IsNullOrEmpty(token) || token.Length < 2)
            return new List<TemplateItem>();

        var list = Templates.Where(t =>
            (!string.IsNullOrEmpty(t.Keyword) && t.Keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase)) ||
            t.Title.StartsWith(token, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        if (list.Count > 0)
        {
            // 앞에 입력한 '\' 또는 '/' 기호까지 함께 지워지도록 prefixLen 합산
            matchedLen = token.Length + prefixLen;
        }

        return list;
    }

    public static List<List<string>> ParseCsvRecords(string csvText) =>
        CsvHelperService.ParseRecords(csvText);
}
