using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ChatHelper.Services;

/// <summary>
/// 빈 catch 블록을 대체하는 경량 스레드 안전 로거.
/// </summary>
public static class AppLogger
{
    private static readonly object _logLock = new();
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatHelper.log");
    private const long MaxLogBytes = 512 * 1024; // 512KB 초과 시 자동 롤오버

    public static void Info(string area, string message) => Write("INFO", area, message, null);

    public static void Warn(string area, string message, Exception? ex = null) => Write("WARN", area, message, ex);

    public static void Error(string area, string message, Exception? ex = null) => Write("ERROR", area, message, ex);

    private static void Write(string level, string area, string message, Exception? ex)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{area}] {message}";
        if (ex != null)
        {
            line += $" | {ex.GetType().Name}: {ex.Message}";
        }

        Debug.WriteLine(line);

        try
        {
            lock (_logLock)
            {
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogBytes)
                {
                    File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
                }
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 로깅 자체 실패는 앱 동작을 중단시키지 않음
        }
    }
}
