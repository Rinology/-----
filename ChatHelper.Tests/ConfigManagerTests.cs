using System;
using System.IO;
using System.Threading.Tasks;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.Tests;

public static class ConfigManagerTests
{
    public static void RunAll()
    {
        Test_DefaultConfig_Generation();
        Test_SaveAndLoad_Persistence();
        Test_Concurrent_Save_ThreadSafety();
        Test_Security_Path_Dpapi_And_7DayLogCleanup();
    }

    private static void Test_DefaultConfig_Generation()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_default.json");
        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);

            var manager = new ConfigManager(testConfig);
            Assert(manager.Config != null, "Default Config must not be null.");
            Assert(manager.Config!.TriggerKey == "\\", "Default TriggerKey should be '\\'");
            Assert(manager.Config.Theme == "FluentLight", "Default Theme should be 'FluentLight'");
            Assert(SecretVault.GasWebhookUrl.StartsWith("https://script.google.com/"), "SecretVault GasWebhookUrl should decrypt");
            Assert(!string.IsNullOrWhiteSpace(SecretVault.GasApiToken), "SecretVault GasApiToken should decrypt");
            Assert(manager.Config.WindowOpacity == 0.95, "Default WindowOpacity should be 0.95");
            Assert(manager.Config.MaxCandidatesCount == 5, "Default MaxCandidatesCount should be 5");
            Assert(manager.Config.CompleteKeyMode == "SlashTab", "Default CompleteKeyMode should be 'SlashTab'");
            Assert(HotkeyValidator.ValidateHotkey("Q").IsConflict, "Single key without modifier must report conflict");
            Assert(HotkeyValidator.ValidateHotkey("Alt+X").IsConflict, "Internal hotkey Alt+X must report conflict");
            Assert(!HotkeyValidator.ValidateHotkey("Alt+Q").IsConflict, "Default Alt+Q must be valid");
            Assert(File.Exists(testConfig), "Default config file should be created on disk");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
        }
    }

    private static void Test_SaveAndLoad_Persistence()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_persist.json");
        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);

            var manager = new ConfigManager(testConfig);
            manager.Config.TriggerKey = "/";
            manager.Config.PillScale = 3;
            manager.Config.KeepLoggedIn = false;
            manager.Config.CurrentUserId = "secret_not_remembered";
            manager.Save();

            // KeepLoggedIn == false 이면 디스크에 아이디가 저장되지 않아야 함
            string rawJsonNoKeep = File.ReadAllText(testConfig);
            Assert(!rawJsonNoKeep.Contains("secret_not_remembered"), "UserId must NOT be written to disk when KeepLoggedIn is false");
            var reloadedNoKeep = new ConfigManager(testConfig);
            Assert(string.IsNullOrEmpty(reloadedNoKeep.Config.CurrentUserId), "CurrentUserId must be empty on reload when KeepLoggedIn is false");

            // KeepLoggedIn == true 이면 DPAPI 암호화되어 저장되고 평문 노출이 없어야 함
            manager.Config.KeepLoggedIn = true;
            manager.Config.CurrentUserId = "test_user_42";
            manager.Save();

            string rawJsonKeep = File.ReadAllText(testConfig);
            Assert(!rawJsonKeep.Contains("test_user_42"), "Plaintext UserId must NEVER appear in config.json");

            // Reload via new instance
            var reloaded = new ConfigManager(testConfig);
            Assert(reloaded.Config.TriggerKey == "/", "TriggerKey should persist");
            Assert(reloaded.Config.PillScale == 3, "PillScale should persist");
            Assert(reloaded.Config.CurrentUserId == "test_user_42", "DPAPI-encrypted CurrentUserId should decrypt on reload");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
        }
    }

    private static void Test_Concurrent_Save_ThreadSafety()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_concurrent.json");
        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);

            var manager = new ConfigManager(testConfig);

            // 10개의 태스크가 동시에 Config를 변경하고 Save() 호출
            manager.Config.KeepLoggedIn = true;
            Parallel.For(0, 20, i =>
            {
                manager.Config.CurrentUserId = $"user_{i}";
                manager.Config.PillOffsetY = 15 + i;
                manager.Save();
            });

            Assert(File.Exists(testConfig), "Config file must survive concurrent writes without corruption");
            var reloaded = new ConfigManager(testConfig);
            Assert(!string.IsNullOrEmpty(reloaded.Config.CurrentUserId), "CurrentUserId should have a valid value");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
        }
    }

    private static void Test_Security_Path_Dpapi_And_7DayLogCleanup()
    {
        // 1. 기본 config.json은 실행 폴더가 아닌 LocalAppData\ChatHelper 에 위치해야 함
        string resolved = ConfigManager.ResolveConfigPath("config.json");
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert(resolved.StartsWith(Path.Combine(localApp, "ChatHelper"), StringComparison.OrdinalIgnoreCase),
            $"Default config.json must resolve under LocalAppData\\ChatHelper, got: {resolved}");

        // 2. 7일 초과 로그 자동 삭제(현재 날짜 기준) 검증
        string tempLogDir = Path.Combine(Path.GetTempPath(), $"ChatHelper_LogTest_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempLogDir);
            DateTime today = new DateTime(2026, 11, 26);
            string old8Days = Path.Combine(tempLogDir, "ChatHelper_2026-11-18.log");
            string edge7Days = Path.Combine(tempLogDir, "ChatHelper_2026-11-19.log");
            string todayLog = Path.Combine(tempLogDir, "ChatHelper_2026-11-26.log");

            File.WriteAllText(old8Days, "old");
            File.WriteAllText(edge7Days, "edge");
            File.WriteAllText(todayLog, "today");

            int deleted = AppLogger.CleanupOldLogs(tempLogDir, today, 7);
            Assert(deleted == 1, $"Expected 1 old log deleted, got {deleted}");
            Assert(!File.Exists(old8Days), "8-day-old log must be deleted");
            Assert(File.Exists(edge7Days), "7-day-old log must be kept");
            Assert(File.Exists(todayLog), "Today log must be kept");
        }
        finally
        {
            if (Directory.Exists(tempLogDir)) Directory.Delete(tempLogDir, true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[ConfigManagerTests FAILED] {message}");
    }
}
