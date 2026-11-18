using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.Tests;

public static class AuthServiceTests
{
    public static void RunAll()
    {
        Test_ValidateUser_Scenarios().GetAwaiter().GetResult();
    }

    private static async Task Test_ValidateUser_Scenarios()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_auth.json");
        string testUsersCsv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_users.csv");

        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testUsersCsv)) File.Delete(testUsersCsv);

            // CSV 데이터 준비 (스키마: user_id,pw,expire_date,status,plan,memo)
            var csvContent = new StringBuilder();
            csvContent.AppendLine("user_id,pw,expire_date,status,plan,memo");
            csvContent.AppendLine("active_user,pass1234,2099-12-31,active,VIP,정상 사용자");
            csvContent.AppendLine("blocked_user,1234,2099-12-31,blocked,Basic,차단된 사용자");
            csvContent.AppendLine("expired_user,1234,2020-01-01,active,Pro,만료된 사용자");
            File.WriteAllText(testUsersCsv, csvContent.ToString(), Encoding.UTF8);

            var configManager = new ConfigManager(testConfig);
            configManager.Config.LocalUserCsvPath = testUsersCsv;
            configManager.Save();

            var authService = new AuthService(configManager);
            authService.LoadUsers();

            Assert(authService.Users.Count == 3, $"Should load 3 users, but loaded {authService.Users.Count}");

            // 1. 공백 아이디 검증
            var (emptySuccess, _, _) = authService.ValidateUser("", "pass1234");
            Assert(!emptySuccess, "Empty user ID should fail");

            // 2. 존재하지 않는 아이디 검증
            var (notFoundSuccess, _, _) = authService.ValidateUser("ghost_user", "pass1234");
            Assert(!notFoundSuccess, "Non-existent user should fail");

            // 3. 비밀번호 불일치 검증
            var (wrongPwSuccess, wrongPwMsg, _) = authService.ValidateUser("active_user", "wrong_password");
            Assert(!wrongPwSuccess, "Wrong password should fail");
            Assert(wrongPwMsg.Contains("비밀번호"), "Message should mention password mismatch");

            // 4. 차단된 계정 검증
            var (blockedSuccess, _, blockedUser) = authService.ValidateUser("blocked_user", "1234");
            Assert(!blockedSuccess, "Blocked user should fail");
            Assert(blockedUser != null, "Blocked user object should be returned");

            // 5. 만료된 계정 검증
            var (expiredSuccess, _, expiredUser) = authService.ValidateUser("expired_user", "1234");
            Assert(!expiredSuccess, "Expired user should fail");
            Assert(expiredUser?.IsExpired == true, "IsExpired flag should be true");

            // 6. 정상 유효 계정 검증 (비동기 호출 테스트)
            var (validSuccess, validMsg, validUser) = await authService.ValidateUserAsync("active_user", "pass1234", syncFirst: false);
            Assert(validSuccess, "Active user with correct password should succeed");
            Assert(validUser != null, "Active user should not be null");
            Assert(authService.CurrentUser?.UserId == "active_user", "CurrentUser should be set to active_user");
            Assert(configManager.Config.CurrentUserId == "active_user", "CurrentUserId in config should be updated");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testUsersCsv)) File.Delete(testUsersCsv);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[AuthServiceTests FAILED] {message}");
    }
}
