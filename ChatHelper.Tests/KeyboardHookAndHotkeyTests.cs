using System;
using System.Windows;
using System.Windows.Interop;
using ChatHelper.Services;

namespace ChatHelper.Tests;

public static class KeyboardHookAndHotkeyTests
{
    public static void RunAll()
    {
        TestNormalTypingNeverBlocksHook();
        TestTriggerKeysOnlyActiveWhenArmedOrCandidateShown();
        TestHwndSourceAndAltQRegistration();
    }

    private static void TestNormalTypingNeverBlocksHook()
    {
        var cfg = new ConfigManager();
        cfg.Config.CompleteKeyMode = "SlashTab";
        var tm = new TemplateManager(cfg);
        using var hook = new KeyboardHookService(cfg, tm);

        hook.SetTestTriggerState(hasCandidates: false, triggerArmed: false);

        int[] normalKeys =
        {
            Win32Api.VK_SPACE,
            Win32Api.VK_A,
            Win32Api.VK_E,
            Win32Api.VK_1,
            Win32Api.VK_9,
            Win32Api.VK_TAB
        };

        foreach (int vk in normalKeys)
        {
            if (hook.ShouldHandleTriggerInHook(vk, isShiftPressed: false) ||
                hook.ShouldHandleTriggerInHook(vk, isShiftPressed: true))
            {
                throw new Exception($"일반 채팅 중 키(0x{vk:X})가 훅 스레드 _lock 플러시를 유발했습니다.");
            }
        }

        if (!hook.ShouldHandleTriggerInHook(Win32Api.VK_OEM_5, isShiftPressed: false))
        {
            throw new Exception("역슬래시(\\) 트리거 키가 인식되지 않았습니다.");
        }
    }

    private static void TestTriggerKeysOnlyActiveWhenArmedOrCandidateShown()
    {
        var cfg = new ConfigManager();
        cfg.Config.CompleteKeyMode = "SlashTab";
        var tm = new TemplateManager(cfg);
        using var hook = new KeyboardHookService(cfg, tm);

        // 후보 표시 중이지만 \ 미입력 상태
        hook.SetTestTriggerState(hasCandidates: true, triggerArmed: false);

        if (hook.ShouldHandleTriggerInHook(Win32Api.VK_SPACE, isShiftPressed: false))
            throw new Exception("SlashTab 모드에서 일반 Space가 훅 트리거로 오인되었습니다.");
        if (hook.ShouldHandleTriggerInHook(Win32Api.VK_SPACE, isShiftPressed: true))
            throw new Exception("SlashTab 모드에서 Shift+Space가 훅 트리거로 오인되었습니다.");
        if (hook.ShouldHandleTriggerInHook(Win32Api.VK_1, isShiftPressed: false) ||
            hook.ShouldHandleTriggerInHook(Win32Api.VK_A, isShiftPressed: false) ||
            hook.ShouldHandleTriggerInHook(Win32Api.VK_E, isShiftPressed: false))
            throw new Exception("\\ 미입력 상태에서 숫자/A/E 키가 훅 트리거로 오인되었습니다.");
        if (!hook.ShouldHandleTriggerInHook(Win32Api.VK_TAB, isShiftPressed: false))
            throw new Exception("후보 표시 중 Tab 키가 트리거로 인식되지 않았습니다.");

        // ShiftSpace 모드에서는 후보 표시 중 Shift+Space만 허용
        cfg.Config.CompleteKeyMode = "ShiftSpace";
        if (!hook.ShouldHandleTriggerInHook(Win32Api.VK_SPACE, isShiftPressed: true))
            throw new Exception("ShiftSpace 모드에서 Shift+Space가 트리거로 인식되지 않았습니다.");

        // \ 입력(armed) 상태에서는 1~9, A, E 허용
        hook.SetTestTriggerState(hasCandidates: true, triggerArmed: true);
        if (!hook.ShouldHandleTriggerInHook(Win32Api.VK_1, isShiftPressed: false) ||
            !hook.ShouldHandleTriggerInHook(Win32Api.VK_A, isShiftPressed: false) ||
            !hook.ShouldHandleTriggerInHook(Win32Api.VK_E, isShiftPressed: false))
            throw new Exception("\\ 대기 상태에서 1/A/E 트리거가 인식되지 않았습니다.");
    }

    private static void TestHwndSourceAndAltQRegistration()
    {
        var win = new Window
        {
            Width = 100,
            Height = 40,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false
        };

        IntPtr hwnd = new WindowInteropHelper(win).EnsureHandle();
        if (hwnd == IntPtr.Zero)
            throw new Exception("EnsureHandle()이 유효한 HWND를 반환하지 않았습니다.");

        var src = HwndSource.FromHwnd(hwnd);
        if (src == null || src.Handle == IntPtr.Zero)
            throw new Exception("HwndSource.FromHwnd(hwnd)가 null을 반환했습니다.");

        const int testHotkeyId = 9901;
        bool okQ = Win32Api.RegisterHotKey(hwnd, testHotkeyId, Win32Api.MOD_ALT | Win32Api.MOD_NOREPEAT, Win32Api.VK_Q);
        if (okQ)
        {
            Win32Api.UnregisterHotKey(hwnd, testHotkeyId);
        }
        win.Close();
    }
}
