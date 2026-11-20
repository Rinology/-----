using System;
using System.Net.Http;
using ChatHelper.Constants;

namespace ChatHelper.Services;

/// <summary>
/// 소켓 고갈 방지를 위해 외부 API(Google Sheets/GAS)와 로컬 LLM(Ollama)용 HttpClient를 분리해 싱글톤으로 제공.
/// </summary>
public static class HttpService
{
    public static HttpClient ExternalClient { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.NetworkTimeoutSeconds + 7) // 15초
    };

    public static HttpClient LocalLlmClient { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
}
