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
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[AiAndOcrIntegrationTests FAILED] {message}");
    }
}
