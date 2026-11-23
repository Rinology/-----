using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatHelper.Services;

namespace ChatHelper.Tests;

public static class AiAndOcrIntegrationTests
{
    public static void RunAll()
    {
        Test_Ocr_Standard_And_ZeroAlpha_Capture().GetAwaiter().GetResult();
        Test_MathCalc();
        Test_QuickSearch_And_SuggestionBar_Positioning();
    }

    private static void Test_QuickSearch_And_SuggestionBar_Positioning()
    {
        var cfg = new ConfigManager();
        var tm = new TemplateManager(cfg);
        tm.AddTemplate(new ChatHelper.Models.TemplateItem
        {
            Keyword = "ds",
            Title = "첫인사",
            Content = "안녕하세요 고객님",
            Category = "기본인사"
        });

        var qsw = new ChatHelper.UI.QuickSearchWindow(tm, cfg);
        var workArea = SystemParameters.WorkArea;
        double expectedCenterLeft = workArea.Left + (workArea.Width - qsw.Width) / 2.0;
        double expectedCenterTop = workArea.Top + (workArea.Height - qsw.Height) / 2.0;
        foreach (var placement in new[] { "CursorNearby", "MousePointer", "ScreenBottomRight" })
        {
            cfg.Config.PillPlacement = placement;
            qsw.ShowAtCursor(IntPtr.Zero);
            Console.WriteLine($"  [QuickSearchWindow:{placement}] Left={qsw.Left}, Top={qsw.Top}, IsVisible={qsw.IsVisible}");
            Assert(qsw.IsVisible, $"QuickSearchWindow must be visible in {placement} mode");
            Assert(Math.Abs(qsw.Left - expectedCenterLeft) < 2 && Math.Abs(qsw.Top - expectedCenterTop) < 2,
                $"QuickSearchWindow must be centered on screen ({expectedCenterLeft},{expectedCenterTop}), got ({qsw.Left},{qsw.Top})");
            qsw.Hide();
        }
    }

    private static async Task Test_Ocr_Standard_And_ZeroAlpha_Capture()
    {
        int width = 320, height = 70;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var ft = new FormattedText(
                "배송 언제 출발하나요 반품 문의",
                CultureInfo.GetCultureInfo("ko-KR"),
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                20,
                Brushes.Black,
                1.0);
            dc.DrawText(ft, new Point(14, 22));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        string normalOcr = await OcrService.RecognizeTextAsync(rtb);
        Console.WriteLine($"  [OCR Normal] => '{normalOcr}'");
        Assert(!string.IsNullOrWhiteSpace(normalOcr) && (normalOcr.Contains("배송") || normalOcr.Contains("반품")),
            $"Normal OCR should recognize Korean text, got '{normalOcr}'");

        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        rtb.CopyPixels(pixels, stride, 0);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 0;
        }

        var zeroAlphaBmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        zeroAlphaBmp.Freeze();

        string zeroAlphaOcr = await Task.Run(() => OcrService.RecognizeTextAsync(zeroAlphaBmp));
        Console.WriteLine($"  [OCR Zero-Alpha Recovery] => '{zeroAlphaOcr}'");
        Assert(!string.IsNullOrWhiteSpace(zeroAlphaOcr) && (zeroAlphaOcr.Contains("배송") || zeroAlphaOcr.Contains("반품")),
            $"Zero-Alpha OCR recovery failed, got '{zeroAlphaOcr}'");
    }

    private static void Test_MathCalc()
    {
        Assert(MathCalcHelper.TryEvaluate("5 * 10 =", out string calcRes, out string calcEq) && calcRes == "50" && calcEq == "5 * 10 = 50",
            $"Expected '50' and '5 * 10 = 50', got '{calcRes}' / '{calcEq}'");
        Assert(MathCalcHelper.TryEvaluate("12000 + 3000", out string sumRes, out _) && sumRes == "15000",
            $"Expected '15000', got '{sumRes}'");
        Assert(MathCalcHelper.TryEvaluate("(100 - 20) / 4 =", out string divRes, out _) && divRes == "20",
            $"Expected '20', got '{divRes}'");
        Assert(MathCalcHelper.TryEvaluate("50000 * 0.9 =", out string decRes, out _) && decRes == "45000",
            $"Expected '45000', got '{decRes}'");
        Console.WriteLine($"  [MathCalc Direct Test] '5 * 10 =' => '{calcRes}' ('{calcEq}'), '(100 - 20) / 4 =' => '{divRes}', '50000 * 0.9 =' => '{decRes}'");

        // 4. SuggestionBar 화면 밖 커서 -> 마우스 폴백 & 경계 잘림 보정 안내 검증
        var (nL, nT, nNotice) = ChatHelper.UI.SuggestionBar.ComputeAdjustedPosition(
            400, 300, 500, 500, 260, 44, 0, 0, 1920, 1040, "CursorNearby", 26);
        Assert(nNotice == null && nL == 400 && nT == 326,
            $"Normal inside-screen position should not trigger notice, got L={nL}, T={nT}, notice={nNotice}");

        var (outL, outT, outNotice) = ChatHelper.UI.SuggestionBar.ComputeAdjustedPosition(
            2500, 300, 600, 400, 260, 44, 0, 0, 1920, 1040, "CursorNearby", 26);
        Assert(outNotice != null && outNotice.Contains("마우스 위치") && outL == 600,
            $"Off-screen caret should fallback to mouse X=600 with notice, got L={outL}, notice={outNotice}");

        var (clipL, clipT, clipNotice) = ChatHelper.UI.SuggestionBar.ComputeAdjustedPosition(
            1850, 300, 1850, 300, 420, 44, 0, 0, 1920, 1040, "CursorNearby", 26);
        Assert(clipNotice != null && clipNotice.Contains("일시 조정") && clipL <= 1920 - 420 - 10,
            $"Edge-clipped large pill should clamp inward with notice, got L={clipL}, notice={clipNotice}");
        Console.WriteLine("  [SuggestionBar Bounds Test] Normal / Off-Screen Mouse Fallback / Edge-Clamp Notice verified.");

        // 5. SetWindowAlpha (LWA_ALPHA) 실제 OS 레이어드 알파 검증
        var testWin = new System.Windows.Window { Width = 200, Height = 80, WindowStyle = System.Windows.WindowStyle.None };
        testWin.Show();
        ChatHelper.Services.Win32Api.SetWindowAlpha(testWin, 0.45);
        var testHwnd = new System.Windows.Interop.WindowInteropHelper(testWin).Handle;
        int exAfter = ChatHelper.Services.Win32Api.GetWindowLong(testHwnd, ChatHelper.Services.Win32Api.GWL_EXSTYLE);
        bool hasLayered = (exAfter & ChatHelper.Services.Win32Api.WS_EX_LAYERED) != 0;
        bool gotAlpha = ChatHelper.Services.Win32Api.GetLayeredWindowAttributes(testHwnd, out _, out byte actualAlpha, out uint lwaFlags);
        testWin.Close();
        Console.WriteLine($"  [Alpha Test] HWND={testHwnd}, ExStyle=0x{exAfter:X}, HasLayered={hasLayered}, GotAlpha={gotAlpha}, Alpha={actualAlpha}/255, Flags={lwaFlags}");
        Assert(hasLayered && gotAlpha && actualAlpha == 115, $"Expected WS_EX_LAYERED with Alpha=115, got HasLayered={hasLayered}, Alpha={actualAlpha}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[AiAndOcrIntegrationTests FAILED] {message}");
    }
}
