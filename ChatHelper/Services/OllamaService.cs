using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChatHelper.Models;

namespace ChatHelper.Services;

/// <summary>
/// 100% 로컬 백그라운드에서 실행되는 초경량 Ollama AI (Qwen 2.5 1.5B) 서비스
/// 작업표시줄/트레이 아이콘 없이 순수 백그라운드 데몬으로 무창 실행됩니다.
/// </summary>
public class OllamaService : IDisposable
{
    private static readonly Lazy<OllamaService> _instance = new(() => new OllamaService());
    public static OllamaService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private Process? _ollamaProcess;
    private readonly string _baseUrl;
    public const string DefaultModel = "qwen2.5:1.5b";

    private string _systemPrompt = "너는 친절하고 전문적인 쇼핑몰 CS 고객센터 상담원이야. 항상 정중하고 부드러운 한국어 존댓말로 답변해. 사족이나 따옴표 없이 고객에게 보낼 본문만 1~3문장으로 간결하게 작성해.";

    private OllamaService()
    {
        var configuredEndpoint = EnvService.Get("LOCAL_LLM_ENDPOINT", "http://localhost:11434");
        _baseUrl = NormalizeBaseUrl(configuredEndpoint);
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        LoadSystemRules();
    }

    private static string NormalizeBaseUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "http://localhost:11434";

        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/api/generate".Length];
        else if (trimmed.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/api/chat".Length];

        return trimmed;
    }

    /// <summary>
    /// Prompts/ai_system_rules.json 에서 프롬프트 규칙 로드
    /// </summary>
    public void LoadSystemRules()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string promptPath = Path.Combine(baseDir, "Prompts", "ai_system_rules.json");
            
            // 개발 환경 상대 경로 fallback
            if (!File.Exists(promptPath))
            {
                promptPath = Path.Combine(baseDir, "..", "..", "..", "Prompts", "ai_system_rules.json");
            }

            if (File.Exists(promptPath))
            {
                string json = File.ReadAllText(promptPath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var sb = new StringBuilder();
                if (root.TryGetProperty("system_role", out var roleEl))
                {
                    sb.AppendLine(roleEl.GetString());
                }

                if (root.TryGetProperty("core_rules", out var rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("\n[핵심 규칙]");
                    foreach (var rule in rulesEl.EnumerateArray())
                    {
                        sb.AppendLine("- " + rule.GetString());
                    }
                }

                if (root.TryGetProperty("few_shot_examples", out var examplesEl) && examplesEl.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("\n[모범 답변 예시]");
                    foreach (var eg in examplesEl.EnumerateArray())
                    {
                        if (eg.TryGetProperty("input", out var inEl) && eg.TryGetProperty("output", out var outEl))
                        {
                            sb.AppendLine($"문의: {inEl.GetString()} => 답장: {outEl.GetString()}");
                        }
                    }
                }

                if (sb.Length > 0)
                {
                    _systemPrompt = sb.ToString().Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OllamaService] Rules load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ollama 서비스가 살아있는지 검사하고, 없으면 무창(트레이 아이콘 없는 백그라운드)으로 자동 기동
    /// </summary>
    public async Task<bool> EnsureRunningAsync()
    {
        if (await IsServerRespondingAsync())
        {
            return true;
        }

        try
        {
            // 백그라운드 ollama serve 무창 실행
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exePath = Path.Combine(localAppData, @"Programs\Ollama\ollama.exe");

            if (!File.Exists(exePath))
            {
                exePath = "ollama.exe";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "serve",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _ollamaProcess = Process.Start(startInfo);

            // 기동 대기 (최대 5초)
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                if (await IsServerRespondingAsync())
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OllamaService] Failed to auto-start ollama daemon: {ex.Message}");
        }

        return await IsServerRespondingAsync();
    }

    public async Task<bool> IsServerRespondingAsync()
    {
        try
        {
            var res = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 고객 문의 메시지나 캡처 OCR 텍스트를 기반으로 정중한 친절 CS 답변 생성
    /// </summary>
    public async Task<string> GenerateCsAnswerAsync(string customerMessage, CancellationToken cancellationToken = default)
    {
        await EnsureRunningAsync();

        string userPrompt = $"[상대방 메시지 내용]\n{customerMessage.Trim()}\n\n위 메시지에 대해 즉시 보낼 수 있는 1~2문장의 간결하고 명확한 답장을 작성해줘:";

        var requestBody = new
        {
            model = DefaultModel,
            messages = new[]
            {
                new { role = "system", content = _systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            options = new
            {
                temperature = 0.2,
                top_p = 0.85,
                num_predict = 120
            },
            keep_alive = "5m" // 5분간 미사용 시 RAM 자동 반환
        };

        string jsonPayload = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        
        if (doc.RootElement.TryGetProperty("message", out var msgEl) &&
            msgEl.TryGetProperty("content", out var contentEl))
        {
            string answer = contentEl.GetString()?.Trim() ?? string.Empty;
            // 앞뒤 따옴표 및 "답장:" / "답변:" 접두어 제거 정리
            if (answer.StartsWith("\"") && answer.EndsWith("\"") && answer.Length > 2)
            {
                answer = answer[1..^1].Trim();
            }
            answer = Regex.Replace(answer, @"^(답장|답변|추천\s*답변|응답)\s*:\s*", "", RegexOptions.IgnoreCase).Trim();
            return answer;
        }

        return "답변을 생성하지 못했습니다.";
    }

    /// <summary>
    /// 답변 본문(content)을 분석해 시트 contentKeyword 컬럼에 저장할 핵심 키워드(인사말·태그 등)를 추출
    /// </summary>
    public async Task<string> GenerateKeywordAsync(string answerContent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(answerContent))
            return string.Empty;

        await EnsureRunningAsync();

        string prompt = $"다음 CS 답변 문장의 핵심 주제를 가장 잘 나타내는 짧은 키워드 1~2단어(예: 인사말, 반품안내, 배송지연, 사과)만 출력하세요. 설명이나 문장부호 없이 키워드만 출력:\n\n{answerContent.Trim()}";

        var requestBody = new
        {
            model = DefaultModel,
            messages = new[]
            {
                new { role = "system", content = "너는 CS 답변에서 핵심 검색 키워드 1~2단어만 간결하게 추출하는 도우미야." },
                new { role = "user", content = prompt }
            },
            stream = false,
            options = new
            {
                temperature = 0.0,
                num_predict = 24
            },
            keep_alive = "5m"
        };

        string jsonPayload = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("message", out var msgEl) &&
            msgEl.TryGetProperty("content", out var contentEl))
        {
            string kw = contentEl.GetString()?.Trim() ?? string.Empty;
            return kw.Trim('"', '\'', '.', ' ');
        }

        return string.Empty;
    }

    /// <summary>
    /// 스프레드시트 템플릿 목록을 Ollama가 읽고, 사용자 입력(예: "배송안내", "화난 고객 달래주기")에 가장 알맞은 템플릿 인덱스들을 추천
    /// </summary>
    public async Task<List<int>> RecommendTemplateIndicesAsync(
        string userQuery,
        IReadOnlyList<TemplateItem> templates,
        int maxCount = 5,
        CancellationToken cancellationToken = default)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(userQuery) || templates == null || templates.Count == 0)
            return result;

        await EnsureRunningAsync();

        var catalogSb = new StringBuilder();
        int limit = Math.Min(templates.Count, 80);
        for (int i = 0; i < limit; i++)
        {
            var t = templates[i];
            string snippet = (t.Content ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (snippet.Length > 45) snippet = snippet[..45] + "..";
            catalogSb.AppendLine($"[{i}] {t.Title} | 키워드:{t.Keyword},{t.ContentKeyword} | 분류:{t.Category} | {snippet}");
        }

        string prompt =
            $"사용자 요청: \"{userQuery.Trim()}\"\n\n" +
            $"아래 스프레드시트 템플릿 목록에서 사용자 요청에 가장 알맞은 항목 번호를 최대 {maxCount}개 고르세요.\n" +
            "반드시 JSON 형식으로만 답하세요. 예: {\"indices\":[3, 12]}\n\n" +
            catalogSb.ToString();

        var requestBody = new
        {
            model = DefaultModel,
            messages = new[]
            {
                new { role = "system", content = "너는 고객센터 템플릿 분류기야. 목록에서 가장 관련성이 높은 번호만 JSON {\"indices\":[...]} 형태로 출력해." },
                new { role = "user", content = prompt }
            },
            stream = false,
            options = new
            {
                temperature = 0.0,
                num_predict = 60
            },
            keep_alive = "5m"
        };

        string jsonPayload = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl))
        {
            return result;
        }

        string raw = contentEl.GetString() ?? string.Empty;
        var matches = Regex.Matches(raw, @"\d+");
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Value, out int idx) && idx >= 0 && idx < limit && !result.Contains(idx))
            {
                result.Add(idx);
                if (result.Count >= maxCount) break;
            }
        }

        return result;
    }

    /// <summary>
    /// 시트 업로드 전, Ollama가 keyword(짧은 영문 단축키), contentKeyword, title, content, category를 깔끔하게 정리
    /// </summary>
    public async Task<TemplateItem> StructureTemplateForSheetAsync(
        string userQuery,
        string answerContent,
        IEnumerable<string> existingKeywords,
        CancellationToken cancellationToken = default)
    {
        string cleanContent = answerContent?.Trim() ?? string.Empty;
        var usedSet = new HashSet<string>(
            existingKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()));

        // 기본 폴백 생성 함수
        TemplateItem BuildFallback()
        {
            string fallbackTitle = (userQuery ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (fallbackTitle.Length > 15 || string.IsNullOrWhiteSpace(fallbackTitle))
            {
                string firstSentence = cleanContent.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "CS응대";
                fallbackTitle = firstSentence.Length > 15 ? firstSentence[..15] : firstSentence;
            }

            string shortKw = GenerateUniqueShortKeyword(usedSet);
            return new TemplateItem
            {
                Keyword = shortKw,
                ContentKeyword = fallbackTitle,
                Title = fallbackTitle,
                Content = cleanContent,
                Category = "CS응대"
            };
        }

        try
        {
            if (!await IsServerRespondingAsync())
                return BuildFallback();

            string prompt =
                "아래 고객 문의/의도와 답변 본문을 구글 스프레드시트에 깔끔하게 저장할 수 있도록 JSON으로 정리해 주세요.\n" +
                "규칙:\n" +
                "1. keyword: 2~4글자 영문 소문자+숫자 단축키 (예: dlv, ret, cs1). 절대 한국어나 긴 문장을 넣지 마세요.\n" +
                "2. contentKeyword: 검색용 핵심 한국어 키워드 1~3개 (쉼표로 구분, 예: 알림톡, 상세안내).\n" +
                "3. title: 5~15자 내외의 간결한 한국어 제목.\n" +
                "4. category: 배송, 교환반품, CS응대, 결제, 계정, 일반 중 가장 알맞은 1개.\n" +
                "5. content: 고객에게 보낼 정중하고 깔끔한 본문.\n\n" +
                $"[문의/의도]\n{userQuery?.Trim()}\n\n[답변 본문]\n{cleanContent}\n\n" +
                "반드시 JSON 객체만 출력하세요.";

            var requestBody = new
            {
                model = DefaultModel,
                messages = new[]
                {
                    new { role = "system", content = "너는 CS 템플릿 데이터를 JSON으로 구조화하는 도우미야. 반드시 유효한 JSON 객체만 출력해." },
                    new { role = "user", content = prompt }
                },
                stream = false,
                options = new
                {
                    temperature = 0.1,
                    num_predict = 260
                },
                keep_alive = "5m"
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", httpContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var outerDoc = JsonDocument.Parse(responseJson);
            string raw = outerDoc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
                return BuildFallback();

            string jsonBody = raw[start..(end + 1)];
            using var innerDoc = JsonDocument.Parse(jsonBody);
            var root = innerDoc.RootElement;

            string kw = root.TryGetProperty("keyword", out var kwEl) ? (kwEl.GetString()?.Trim().ToLowerInvariant() ?? "") : "";
            kw = Regex.Replace(kw, @"[^a-z0-9]", "");
            if (kw.Length < 2 || kw.Length > 6 || usedSet.Contains(kw))
            {
                kw = GenerateUniqueShortKeyword(usedSet);
            }

            string ckw = root.TryGetProperty("contentKeyword", out var ckwEl) ? (ckwEl.GetString()?.Trim() ?? "") : "";
            string title = root.TryGetProperty("title", out var tEl) ? (tEl.GetString()?.Trim() ?? "") : "";
            string cat = root.TryGetProperty("category", out var catEl) ? (catEl.GetString()?.Trim() ?? "") : "";
            string body = root.TryGetProperty("content", out var bEl) ? (bEl.GetString()?.Trim() ?? "") : "";

            var fb = BuildFallback();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 25) title = fb.Title;
            if (string.IsNullOrWhiteSpace(ckw) || ckw.Length > 35) ckw = title;
            if (string.IsNullOrWhiteSpace(cat)) cat = "CS응대";
            if (string.IsNullOrWhiteSpace(body)) body = cleanContent;

            return new TemplateItem
            {
                Keyword = kw,
                ContentKeyword = ckw,
                Title = title,
                Content = body,
                Category = cat
            };
        }
        catch
        {
            return BuildFallback();
        }
    }

    private static string GenerateUniqueShortKeyword(HashSet<string> usedSet)
    {
        for (int i = 1; i <= 999; i++)
        {
            string candidate = $"ai{i}";
            if (!usedSet.Contains(candidate))
                return candidate;
        }
        return $"ai{DateTime.Now:mmss}";
    }

    public void Dispose()
    {
        try
        {
            if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            {
                _ollamaProcess.Kill(true);
                _ollamaProcess.Dispose();
            }
        }
        catch { }
        _httpClient.Dispose();
    }
}
