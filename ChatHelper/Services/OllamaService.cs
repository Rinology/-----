using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    private const string BaseUrl = "http://localhost:11434";
    public const string DefaultModel = "qwen2.5:1.5b";

    private string _systemPrompt = "너는 친절하고 전문적인 쇼핑몰 CS 고객센터 상담원이야. 항상 정중하고 부드러운 한국어 존댓말로 답변해. 사족이나 따옴표 없이 고객에게 보낼 본문만 1~3문장으로 간결하게 작성해.";

    private OllamaService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        LoadSystemRules();
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
            var res = await _httpClient.GetAsync($"{BaseUrl}/api/tags");
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

        var response = await _httpClient.PostAsync($"{BaseUrl}/api/chat", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        
        if (doc.RootElement.TryGetProperty("message", out var msgEl) &&
            msgEl.TryGetProperty("content", out var contentEl))
        {
            string answer = contentEl.GetString()?.Trim() ?? string.Empty;
            // 앞뒤 따옴표 제거 정리
            if (answer.StartsWith("\"") && answer.EndsWith("\"") && answer.Length > 2)
            {
                answer = answer[1..^1].Trim();
            }
            return answer;
        }

        return "답변을 생성하지 못했습니다.";
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
