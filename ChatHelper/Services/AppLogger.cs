using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ChatHelper.Services;

/// <summary>
/// 보안 강화 경량 스레드 안전 로거 (방안 C).
/// 로그는 실행 폴더(publish/)가 아닌 %LocalAppData%\ChatHelper\logs\ 에 일자별로 분리 저장되며,
/// 현재 날짜 기준 7일이 지난 로그는 자동 삭제됩니다.
/// </summary>
public static class AppLogger
{
    private static readonly object _logLock = new();
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private const long MaxLogBytes = 512 * 1024; // 일일 파일 512KB 초과 시 롤오버
    private const int RetentionDays = 7;
    private static DateTime _lastCleanupDate = DateTime.MinValue;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChatHelper",
        "logs");

    private static string GetTodayLogFilePath(DateTime now) =>
        Path.Combine(LogDirectory, $"ChatHelper_{now:yyyy-MM-dd}.log");

    static AppLogger()
    {
        TryRemoveLegacyBaseDirLog();
        CleanupOldLogs(LogDirectory, DateTime.Now.Date, RetentionDays);
    }

    private static void TryRemoveLegacyBaseDirLog()
    {
        try
        {
            string legacyLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatHelper.log");
            if (File.Exists(legacyLog))
                File.Delete(legacyLog);
        }
        catch { }
    }

    /// <summary>
    /// 현재 날짜 기준으로 보관 기간(7일)이 지난 로그 파일을 자동 삭제합니다.
    /// 로그인 횟수와 무관하게 달력 날짜 기준으로 동작하므로 7일 이상 미사용 시 로그가 0개로 정리됩니다.
    /// </summary>
    internal static int CleanupOldLogs(string logDir, DateTime today, int retentionDays = RetentionDays)
    {
        int deletedCount = 0;
        try
        {
            if (!Directory.Exists(logDir)) return 0;

            DateTime cutoffDate = today.Date.AddDays(-retentionDays);
            foreach (var file in Directory.GetFiles(logDir, "*.log"))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    DateTime fileDate = File.GetLastWriteTime(file).Date;

                    if (name.StartsWith("ChatHelper_", StringComparison.OrdinalIgnoreCase) &&
                        DateTime.TryParseExact(name.Substring("ChatHelper_".Length), "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        fileDate = parsedDate.Date;
                    }

                    if (fileDate < cutoffDate)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }
                catch { }
            }
        }
        catch { }
        return deletedCount;
    }

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
            s, @"(token|pw|password|userId|uid)=[^&\s]+", "$1=[숨김]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    private static void Write(string level, string area, string message, Exception? ex)
    {
        DateTime now = DateTime.Now;
        string safeMsg = SanitizeSecrets(SummarizeResponse(message));
        string line = $"[{now:yyyy-MM-dd HH:mm:ss}] [{level}] [{area}] {safeMsg}";
        if (ex != null)
        {
            line += $" | {ex.GetType().Name}: {SanitizeSecrets(ex.Message)}";
        }

        Debug.WriteLine(line);

        try
        {
            lock (_logLock)
            {
                if (_lastCleanupDate != now.Date)
                {
                    CleanupOldLogs(LogDirectory, now.Date, RetentionDays);
                    _lastCleanupDate = now.Date;
                }

                Directory.CreateDirectory(LogDirectory);
                string logPath = GetTodayLogFilePath(now);

                bool needReset = !File.Exists(logPath) || new FileInfo(logPath).Length > MaxLogBytes;
                if (needReset)
                {
                    File.WriteAllText(logPath, line + Environment.NewLine, Utf8Bom);
                }
                else
                {
                    File.AppendAllText(logPath, line + Environment.NewLine, Utf8Bom);
                }
            }
        }
        catch
        {
            // 로깅 자체 실패는 앱 동작을 중단시키지 않음
        }
    }
}
