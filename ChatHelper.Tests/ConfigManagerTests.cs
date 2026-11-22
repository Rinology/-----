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
            manager.Config.CurrentUserId = "test_user_42";
            manager.Save();

            // Reload via new instance
            var reloaded = new ConfigManager(testConfig);
            Assert(reloaded.Config.TriggerKey == "/", "TriggerKey should persist");
            Assert(reloaded.Config.PillScale == 3, "PillScale should persist");
            Assert(reloaded.Config.CurrentUserId == "test_user_42", "CurrentUserId should persist");
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[ConfigManagerTests FAILED] {message}");
    }
}
