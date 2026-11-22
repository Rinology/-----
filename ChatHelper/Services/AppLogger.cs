using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ChatHelper.Services;

/// <summary>
/// 빈 catch 블록을 대체하는 경량 스레드 안전 로거.
/// UTF-8 BOM을 포함해 메모장에서도 한글이 깨지지 않으며, 긴 HTML 응답은 자동 요약합니다.
/// </summary>
public static class AppLogger
{
    private static readonly object _logLock = new();
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatHelper.log");
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private const long MaxLogBytes = 512 * 1024; // 512KB 초과 시 자동 롤오버

    public static void Info(string area, string message) => Write("INFO", area, message, null);

    public static void Warn(string area, string message, Exception? ex = null) => Write("WARN", area, message, ex);

    public static void Error(string area, string message, Exception? ex = null) => Write("ERROR", area, message, ex);

    public static string SummarizeResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string trimmed = text.Trim();
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.Contains("권한이 없습니다", StringComparison.Ordinal))
                return "[HTML 오류: 문서 액세스 권한 없음]";
            return "[HTML 오류 응답 생략]";
        }
        return trimmed.Length > 300 ? trimmed[..300] + "..." : trimmed;
    }

    public static string SanitizeSecrets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = System.Text.RegularExpressions.Regex.Replace(
            text, @"https?://[^\s\)""'<>]+", "[URL 숨김]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"(token|pw|password)=[^&\s]+", "$1=[숨김]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    private static void Write(string level, string area, string message, Exception? ex)
    {
        string safeMsg = SanitizeSecrets(SummarizeResponse(message));
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{area}] {safeMsg}";
        if (ex != null)
        {
            line += $" | {ex.GetType().Name}: {SanitizeSecrets(ex.Message)}";
        }

        Debug.WriteLine(line);

        try
        {
            lock (_logLock)
            {
                bool needReset = !File.Exists(LogFilePath) || new FileInfo(LogFilePath).Length > MaxLogBytes;
                if (needReset)
                {
                    File.WriteAllText(LogFilePath, line + Environment.NewLine, Utf8Bom);
                }
                else
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Utf8Bom);
                }
            }
        }
        catch
        {
            // 로깅 자체 실패는 앱 동작을 중단시키지 않음
        }
    }
}
