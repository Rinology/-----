using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.Tests;

public static class AiAndOcrIntegrationTests
{
    public static void RunAll()
    {
        Test_Ocr_Standard_And_ZeroAlpha_Capture().GetAwaiter().GetResult();
        Test_Ollama_And_SheetStructuring().GetAwaiter().GetResult();
    }

    private static async Task Test_Ocr_Standard_And_ZeroAlpha_Capture()
    {
        // 1. 한글 고객 문의 캡처본 이미지 생성 (작은 말풍선 크기 320x70)
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

        // 일반 캡처 이미지 OCR 판독 테스트
        string normalOcr = await OcrService.RecognizeTextAsync(rtb);
        Console.WriteLine($"  [OCR Normal] => '{normalOcr}'");
        Assert(!string.IsNullOrWhiteSpace(normalOcr) && (normalOcr.Contains("배송") || normalOcr.Contains("반품")),
            $"Normal OCR should recognize Korean text, got '{normalOcr}'");

        // 2. Win+Shift+S 캡처 도구의 Alpha=0 투명 버그 재현 이미지 생성 후 백그라운드 스레드에서 OCR 판독
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        rtb.CopyPixels(pixels, stride, 0);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 0; // 모든 픽셀의 Alpha를 0으로 강제 손상
        }

        var zeroAlphaBmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        zeroAlphaBmp.Freeze();

        string zeroAlphaOcr = await Task.Run(() => OcrService.RecognizeTextAsync(zeroAlphaBmp));
        Console.WriteLine($"  [OCR Zero-Alpha Recovery] => '{zeroAlphaOcr}'");
        Assert(!string.IsNullOrWhiteSpace(zeroAlphaOcr) && (zeroAlphaOcr.Contains("배송") || zeroAlphaOcr.Contains("반품")),
            $"Zero-Alpha OCR recovery failed, got '{zeroAlphaOcr}'");
    }

    private static async Task Test_Ollama_And_SheetStructuring()
    {
        // 3번 시트(톤앤매너) DB 테이블 형식 CSV 파싱 검증 (중복 헤더 행 포함)
        string sampleRulesCsv =
            "type,title,input_condition,rule_or_output,enabled\n" +
            "type,title,input_condition,rule_or_output,enabled\n" +
            "ROLE,기본 역할,모든 메시지 공통,너는 친절한 CS 답변 비서야.,Y\n" +
            "RULE,답변 길이,모든 답변,1~2문장으로 답해.,Y\n" +
            "EXAMPLE,배송 문의,배송 언제 와요?,출고 준비 중입니다.,Y\n" +
            "PARAM,temperature,일관성,0.15,Y\n";
        Assert(OllamaService.Instance.ParseAiRulesFromCsv(sampleRulesCsv), "ParseAiRulesFromCsv should succeed on 5-column DB CSV");

        // 사칙연산 계산기 검증 ("5 * 10 =" -> "50", "(100 - 20) / 4 =" -> "20", "50000 * 0.9 =" -> "45000")
        Assert(MathCalcHelper.TryEvaluate("5 * 10 =", out string calcRes, out string calcEq) && calcRes == "50" && calcEq == "5 * 10 = 50",
            $"Expected '50' and '5 * 10 = 50', got '{calcRes}' / '{calcEq}'");
        Assert(MathCalcHelper.TryEvaluate("12000 + 3000", out string sumRes, out _) && sumRes == "15000",
            $"Expected '15000', got '{sumRes}'");
        Assert(MathCalcHelper.TryEvaluate("(100 - 20) / 4 =", out string divRes, out _) && divRes == "20",
            $"Expected '20', got '{divRes}'");
        Assert(MathCalcHelper.TryEvaluate("50000 * 0.9 =", out string decRes, out _) && decRes == "45000",
            $"Expected '45000', got '{decRes}'");
        Console.WriteLine($"  [MathCalc Direct Test] '5 * 10 =' => '{calcRes}' ('{calcEq}'), '(100 - 20) / 4 =' => '{divRes}', '50000 * 0.9 =' => '{decRes}'");

        var existingKeywords = new[] { "qa", "wtt", "ai1" };
        string longCustomerQuery = "상담 중 안내해 드린 상세 내용은 고객님의 등록된 휴대폰 번호로도 알림톡 발송해 드렸습니다.";
        string sampleAnswer = "확인 부탁드립니다! 안내해 드린 상세 내용은 고객님의 휴대폰 번호로 알림톡 발송 완료되었습니다.";

        // 시트 업로드용 구조화 검증 (긴 문장이 keyword에 절대 들어가지 않는지 확인)
        var structured = await OllamaService.Instance.StructureTemplateForSheetAsync(
            longCustomerQuery, sampleAnswer, existingKeywords);

        Console.WriteLine($"  [Sheet Structure] keyword='{structured.Keyword}', contentKeyword='{structured.ContentKeyword}', title='{structured.Title}', category='{structured.Category}'");
        Assert(Regex.IsMatch(structured.Keyword, @"^[a-z0-9]{2,6}$"),
            $"Structured keyword must be 2~6 lowercase alphanumeric chars, got '{structured.Keyword}'");
        Assert(structured.Keyword != "ai1", "Structured keyword must not collide with existing keyword 'ai1'");
        Assert(structured.Title.Length <= 25 && !string.IsNullOrWhiteSpace(structured.Title),
            $"Structured title should be concise (<=25 chars), got '{structured.Title}'");

        // 로컬 Ollama 서버 응답 시 용어집 RAG 답변 생성 테스트
        if (await OllamaService.Instance.IsServerRespondingAsync())
        {
            var glossary = new List<GlossaryItem>
            {
                new GlossaryItem
                {
                    Term = "폴드",
                    Aliases = "폴더, fold",
                    Description = "퀄리스포츠 접이식 전기자전거 모델이며 배터리 탈부착이 가능하고 기본 무상보증은 1년입니다.",
                    Guide = "'폴더'가 아닌 '폴드'로 표기할 것",
                    Category = "제품"
                }
            };

            string ragAnswer = await OllamaService.Instance.GenerateCsAnswerAsync("폴더 배터리 분리 가능한가요?", glossary);
            Console.WriteLine($"  [Ollama Glossary RAG] => '{ragAnswer}'");
            Assert(!string.IsNullOrWhiteSpace(ragAnswer) && !ragAnswer.StartsWith("답장:"),
                $"RAG answer should be non-empty without '답장:' prefix, got '{ragAnswer}'");
            Assert(!ragAnswer.Contains("폴더"),
                $"RAG answer should normalize alias '폴더' to official term '폴드', got '{ragAnswer}'");

            // Ollama 통한 사칙연산 호출 및 순수 한->영 번역(톤앤매너 미적용) 직접 테스트
            string aiMath = await OllamaService.Instance.GenerateCsAnswerAsync("5 * 10 =");
            Console.WriteLine($"  [Ollama Math Calc] '5 * 10 =' => '{aiMath}'");
            Assert(aiMath == "50", $"Expected '50' from GenerateCsAnswerAsync('5 * 10 ='), got '{aiMath}'");

            string koSource = "주문하신 상품이 오늘 발송되었습니다.";
            string enTranslated = await OllamaService.Instance.TranslateKoToEnAsync(koSource);
            Console.WriteLine($"  [Ollama Ko->En Translation] '{koSource}' => '{enTranslated}'");
            Assert(!string.IsNullOrWhiteSpace(enTranslated) && Regex.IsMatch(enTranslated, @"[A-Za-z]{3,}"),
                $"Expected valid English translation, got '{enTranslated}'");
        }
        else
        {
            Console.WriteLine("  [Ollama Glossary RAG] Ollama daemon not running in sandbox; skipped live generation.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[AiAndOcrIntegrationTests FAILED] {message}");
    }
}
