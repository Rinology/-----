using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatHelper.Models;

namespace ChatHelper.Services;

/// <summary>
/// 확정된 답변/템플릿과 추출된 키워드를 Google Apps Script Web App(doPost)으로 전송하는 서비스.
/// </summary>
public class KeywordRegistrationService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigManager? _configManager;

    public KeywordRegistrationService(ConfigManager? configManager = null, HttpClient? httpClient = null)
    {
        _configManager = configManager;
        _httpClient = httpClient ?? HttpService.ExternalClient;
    }

    public string ResolveWebhookUrl()
    {
        // 활성 프로필 → .env 순
        var resolved = DataSourceResolver.WebhookUrl;
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        var cfgUrl = _configManager?.Config.SheetUploadUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(cfgUrl))
        {
            return cfgUrl;
        }

        return string.Empty;
    }

    public async Task<(bool Success, string Message)> RegisterAsync(TemplateItem item, CancellationToken cancellationToken = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        string webhookUrl = ResolveWebhookUrl();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return (false, ".env의 SHEET_WRITE_WEBHOOK_URL(또는 제어판 업로드 URL)이 설정되어 있지 않습니다.");
        }

        // GAS doPost 필수 5개 필드(keyword, contentKeyword, title, content, category)가 비어있지 않도록 보정
        string keyword = !string.IsNullOrWhiteSpace(item.Keyword)
            ? item.Keyword.Trim()
            : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title.Trim() : "자동등록");

        string title = !string.IsNullOrWhiteSpace(item.Title)
            ? item.Title.Trim()
            : keyword;

        string contentKeyword = !string.IsNullOrWhiteSpace(item.ContentKeyword)
            ? item.ContentKeyword.Trim()
            : keyword;

        string content = item.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return (false, "저장할 답변 내용(content)이 비어 있습니다.");
        }

        string category = !string.IsNullOrWhiteSpace(item.Category)
            ? item.Category.Trim()
            : "일반";

        var payload = new
        {
            keyword,
            contentKeyword,
            title,
            content,
            category,
            target = "template",
            sheetName = DataSourceResolver.TemplateSheetName,
            spreadsheetUrl = DataSourceResolver.SpreadsheetUrl
        };

        try
        {
            string json = JsonSerializer.Serialize(payload);
            using var reqContent = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(webhookUrl, reqContent, cancellationToken);
            string respText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                string trimmed = respText.Trim();
                if (trimmed.Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "구글 스프레드시트에 성공적으로 등록되었습니다!");
                }
                return (false, $"GAS 스크립트 응답 오류: {trimmed}");
            }

            return (false, $"업로드 응답 오류 (HTTP {(int)response.StatusCode}): {respText}");
        }
        catch (Exception ex)
        {
            return (false, $"업로드 요청 실패: {ex.Message}");
        }
    }
}
