using System;
using System.IO;
using System.Text;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.Tests;

public static class TemplateManagerTests
{
    public static void RunAll()
    {
        Test_ParseCsvRecords_Multiline_And_Escaping();
        Test_Search_And_Category_Filtering();
        Test_Candidate_Matching();
        Test_ContentKeyword_Parsing_And_SuggestionEngine();
    }

    private static void Test_ParseCsvRecords_Multiline_And_Escaping()
    {
        // 콤마, 따옴표 이스케이프, 줄바꿈이 포함된 표준 CSV 텍스트
        string csv = "Keyword,Title,Content,Category\n" +
                     "hi,기본인사,\"안녕하세요!\n반갑습니다.\",인사\n" +
                     "price,가격문의,\"가격은 10,000원이며 \"\"무료배송\"\" 입니다.\",안내\n";

        var records = TemplateManager.ParseCsvRecords(csv);

        Assert(records.Count == 3, $"Expected 3 records (1 header + 2 rows), but got {records.Count}");

        // Row 1 (인사)
        var row1 = records[1];
        Assert(row1[0] == "hi", "Keyword should be 'hi'");
        Assert(row1[1] == "기본인사", "Title should be '기본인사'");
        Assert(row1[2] == "안녕하세요!\n반갑습니다.", "Content should preserve internal newline");
        Assert(row1[3] == "인사", "Category should be '인사'");

        // Row 2 (가격문의 - 따옴표 및 콤마)
        var row2 = records[2];
        Assert(row2[0] == "price", "Keyword should be 'price'");
        Assert(row2[2] == "가격은 10,000원이며 \"무료배송\" 입니다.", "Quotes and comma should be unescaped properly");
    }

    private static void Test_Search_And_Category_Filtering()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_tmpl.json");
        string testCsv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_templates.csv");

        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testCsv)) File.Delete(testCsv);

            var sb = new StringBuilder();
            sb.AppendLine("Keyword,Title,Content,Category");
            sb.AppendLine("환불,환불 절차 안내,환불은 7일 이내 가능합니다.,교환반품");
            sb.AppendLine("교환,교환 배송비 안내,\"왕복 배송비 6,000원입니다.\",교환반품");
            sb.AppendLine("배송,배송 일정 안내,주문 후 2일 이내 출고됩니다.,배송");
            sb.AppendLine("안녕,기본 인사말,안녕하세요 고객님!,기본인사");
            File.WriteAllText(testCsv, sb.ToString(), Encoding.UTF8);

            var configManager = new ConfigManager(testConfig);
            configManager.Config.CsvFilePath = testCsv;
            configManager.Save();

            var templateManager = new TemplateManager(configManager);

            Assert(templateManager.Templates.Count == 4, $"Expected 4 templates, got {templateManager.Templates.Count}");

            // 1. 카테고리 목록 테스트
            var categories = templateManager.GetCategories();
            Assert(categories.Contains("전체"), "Categories should start with '전체'");
            Assert(categories.Contains("교환반품"), "Categories should contain '교환반품'");

            // 2. 카테고리 필터링
            var returnItems = templateManager.Search("", "교환반품");
            Assert(returnItems.Count == 2, $"Expected 2 return items, got {returnItems.Count}");

            // 3. 키워드 검색
            var searchResults = templateManager.Search("배송");
            Assert(searchResults.Count == 2, $"Search '배송' should find 2 items (교환 배송비 + 배송 일정), got {searchResults.Count}");

            // 4. 초성 검색 (ㅎㅂ -> 환불, ㅎㅂㅈㅊ -> 환불 절차 안내)
            Assert(HangulAssembler.IsChoseongQuery("ㅎㅂ"), "ㅎㅂ should be recognized as choseong query");
            var choseongResults = templateManager.Search("ㅎㅂ");
            Assert(choseongResults.Any(r => r.Keyword == "환불"),
                $"Choseong search 'ㅎㅂ' should include '환불', got {choseongResults.Count}");
            var exactChoResults = templateManager.Search("ㅎㅂㅈㅊ");
            Assert(exactChoResults.Count == 1 && exactChoResults[0].Keyword == "환불",
                $"Choseong search 'ㅎㅂㅈㅊ' should find 1 item '환불', got {exactChoResults.Count}");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testCsv)) File.Delete(testCsv);
        }
    }

    private static void Test_Candidate_Matching()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_match.json");
        string testCsv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_match.csv");

        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testCsv)) File.Delete(testCsv);

            var sb = new StringBuilder();
            sb.AppendLine("Keyword,Title,Content,Category");
            sb.AppendLine("qv,반품 접수 안내,반품 접수 완료,교환반품");
            sb.AppendLine("qvx,반품 상세 확인,반품 상세 내용,교환반품");
            sb.AppendLine("qa,사과 안내,불편을 드려 죄송합니다.,사과");
            File.WriteAllText(testCsv, sb.ToString(), Encoding.UTF8);

            var configManager = new ConfigManager(testConfig);
            configManager.Config.CsvFilePath = testCsv;
            configManager.Config.TriggerKey = "\\";
            configManager.Save();

            var templateManager = new TemplateManager(configManager);

            // 트리거 키 + 키워드 매칭 테스트 ("\qv" 및 "/qv" 입력 시 앞의 '\' 또는 '/' 포함 총 3글자가 치환 대상)
            var candidates = templateManager.FindMatchingCandidates("\\qv", out int matchedLen);
            Assert(candidates.Count >= 1, "Should find candidates starting with qv");
            Assert(candidates[0].Keyword == "qv", "First candidate should be 'qv'");
            Assert(matchedLen == 3, $"Matched length should be 3 ('\\qv' to be erased), got {matchedLen}");

            var slashCandidates = templateManager.FindMatchingCandidates("앞글자/qv", out int slashMatchedLen);
            Assert(slashCandidates.Count >= 1 && slashCandidates[0].Keyword == "qv" && slashMatchedLen == 3,
                $"Should match '/qv' even after prefix text with matchedLen=3, got {slashMatchedLen}");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testCsv)) File.Delete(testCsv);
        }
    }

    private static void Test_ContentKeyword_Parsing_And_SuggestionEngine()
    {
        string testConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_config_ckw.json");
        string testCsv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_ckw.csv");

        try
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testCsv)) File.Delete(testCsv);

            var sb = new StringBuilder();
            sb.AppendLine("keyword,contentKeyword,title,content,category");
            sb.AppendLine("반품,반품안내,반품 절차 안내,상품 수령 후 7일 이내 반품 접수가 가능합니다.,교환반품");
            sb.AppendLine("사과,화난 고객 달래주기,불만 고객 응대,이용에 불편을 드려 대단히 죄송합니다. 즉시 확인하여 안내드리겠습니다.,CS응대");
            sb.AppendLine("wtt2,배송,배송 일정 안내,주문 후 영업일 기준 1~2일 이내 출고됩니다.,배송");
            File.WriteAllText(testCsv, sb.ToString(), Encoding.UTF8);

            var configManager = new ConfigManager(testConfig);
            configManager.Config.CsvFilePath = testCsv;
            configManager.Save();

            var templateManager = new TemplateManager(configManager);
            Assert(templateManager.Templates.Count == 3, $"Expected 3 templates, got {templateManager.Templates.Count}");
            Assert(templateManager.Templates[0].Keyword == "반품", "Keyword should be '반품'");
            Assert(templateManager.Templates[0].ContentKeyword == "반품안내", "ContentKeyword should be '반품안내'");

            var engine = new SuggestionEngine(templateManager);
            var pills = engine.SearchRelatedPills("화난 고객 달래주기", maxResults: 3);
            Assert(pills.Count >= 1, "Should find at least 1 pill for '화난 고객 달래주기'");
            Assert(pills[0].Keyword == "사과", $"Top pill should be '사과', got '{pills[0].Keyword}'");

            var deliveryPills = engine.SearchRelatedPills("배송안내", maxResults: 3);
            Assert(deliveryPills.Count >= 1, "Should find at least 1 pill for '배송안내'");
            Assert(deliveryPills[0].Keyword == "wtt2", $"Top delivery pill should be 'wtt2', got '{deliveryPills[0].Keyword}'");

            Assert(HangulAssembler.ConvertQwertyToHangul("dlstkakftodtjd") == "인사말생성",
                $"Expected '인사말생성', got '{HangulAssembler.ConvertQwertyToHangul("dlstkakftodtjd")}'");
            Assert(HangulAssembler.ConvertQwertyToHangul("qothddksso") == "배송안내",
                $"Expected '배송안내', got '{HangulAssembler.ConvertQwertyToHangul("qothddksso")}'");

            // Windows 한국어 IME 음절 확정/조합 상태별 Backspace 재현 테스트 ("가로형 " -> Backspace 2회 -> "형" 재입력 => "가로형")
            var imeBuf = new HangulAssembler.HangulImeBuffer();
            foreach (char c in "rkfhgud ") imeBuf.Append(c); // "가로형 "
            Assert(imeBuf.GetHangulText() == "가로형 ", $"Expected '가로형 ', got '{imeBuf.GetHangulText()}'");
            imeBuf.Backspace(); // 공백 삭제
            imeBuf.Backspace(); // 확정된 '형' 음절 전체(gud)가 1번의 Backspace로 삭제되어야 함!
            Assert(imeBuf.GetHangulText() == "가로", $"Expected '가로' after 2 backspaces, got '{imeBuf.GetHangulText()}'");
            foreach (char c in "gud") imeBuf.Append(c); // '형' 재입력
            Assert(imeBuf.GetHangulText() == "가로형", $"Expected '가로형' (no duplicate '가로형 형'), got '{imeBuf.GetHangulText()}'");

            var glossary = new GlossaryService(configManager, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_glossary.csv"));
            glossary.ParseGlossaryCsv("term,aliases,description,guide,category\n폴드,\"폴더, fold\",퀄리스포츠 접이식 전기자전거 모델입니다.,폴드 표기 유지,제품\n");
            var matchedTerms = glossary.FindRelevantTerms("폴더 추천해줘");
            Assert(matchedTerms.Count == 1 && matchedTerms[0].Term == "폴드", "Glossary should match alias '폴더' -> '폴드'");

            // 양방향 번역 방향 판별 테스트
            Assert(DataSourceResolver.ResolveTranslationPair("KoToEn", "Hello") == ("ko", "en"), "KoToEn should force ko->en");
            Assert(DataSourceResolver.ResolveTranslationPair("EnToKo", "안녕하세요") == ("en", "ko"), "EnToKo should force en->ko");
            Assert(DataSourceResolver.ResolveTranslationPair("Auto", "배송 언제 출발하나요?") == ("ko", "en"), "Auto should detect Korean -> ko->en");
            Assert(DataSourceResolver.ResolveTranslationPair("Auto", "Where is my order?") == ("en", "ko"), "Auto should detect English -> en->ko");

            // 스무고개 다중 키워드 필터링 및 최종 문장 조합 테스트
            var memoTemplates = new List<TemplateItem>
            {
                new() { Keyword = "bd1", Title = "배송 일반 안내", Content = "오늘 출고 예정입니다.", Category = "배송" },
                new() { Keyword = "bd2", Title = "배송 지연 사과", Content = "물량 증가로 배송이 지연되고 있습니다.", Category = "배송" },
                new() { Keyword = "rf1", Title = "반품 접수 안내", Content = "수거 접수를 도와드리겠습니다.", Category = "반품" }
            };
            var step1 = ConsultationMemoService.FilterTemplates(memoTemplates, new[] { "배송" }, "");
            Assert(step1.Count == 2, $"Step 1 should match 2 delivery items, got {step1.Count}");
            var step2 = ConsultationMemoService.FilterTemplates(memoTemplates, new[] { "배송", "지연" }, "");
            Assert(step2.Count == 1 && step2[0].Keyword == "bd2", "Step 2 should narrow down to 'bd2'");
            var stepCho = ConsultationMemoService.FilterTemplates(memoTemplates, new[] { "ㅂㅅ", "ㅈㅇ" }, "");
            Assert(stepCho.Count == 1 && stepCho[0].Keyword == "bd2", "Choseong progressive filter should match 'bd2'");

            var entries = new List<MemoEntry>
            {
                new() { Keyword = "bd2", Content = "물량 증가로 배송이 지연되고 있습니다." },
                new() { Keyword = "rf1", Content = "수거 접수를 도와드리겠습니다." }
            };
            Assert(ConsultationMemoService.ComposeFinalText(entries, MemoJoinMode.Numbered) ==
                "1. 물량 증가로 배송이 지연되고 있습니다.\r\n\r\n2. 수거 접수를 도와드리겠습니다.",
                "Numbered join mode mismatch");
            Assert(ConsultationMemoService.ComposeFinalText(entries, MemoJoinMode.Bulleted) ==
                "• 물량 증가로 배송이 지연되고 있습니다.\r\n• 수거 접수를 도와드리겠습니다.",
                "Bulleted join mode mismatch");

            // {메모}/{1} 자리표시자 자동 치환 테스트 (소비된 자유 메모는 중복 출력되지 않아야 함)
            var placeholderEntries = new List<MemoEntry>
            {
                new() { Keyword = "내일오전", Category = "메모", Content = "내일 오전" },
                new() { Keyword = "폴드7", Category = "메모", Content = "폴드7" },
                new() { Keyword = "as1", Category = "AS", Content = "확인 감사합니다. {날짜}에 {모델} 건으로 방문하겠습니다." }
            };
            string composedPlaceholder = ConsultationMemoService.ComposeFinalText(placeholderEntries, MemoJoinMode.Paragraph);
            Assert(composedPlaceholder == "확인 감사합니다. 내일 오전에 폴드7 건으로 방문하겠습니다.",
                $"Placeholder substitution mismatch: '{composedPlaceholder}'");

            // 의미 기반 변수 자동 매칭 테스트 (순서가 달라도 송장번호는 송장 패턴 메모에 매칭)
            var mixedMemos = new List<MemoEntry>
            {
                new() { Keyword = "내일 오전", Category = "메모", Content = "내일 오전" },
                new() { Keyword = "456-8280-8146", Category = "메모", Content = "456-8280-8146" }
            };
            string resolvedTracking = ConsultationMemoService.ResolveTemplateWithMemosOrClipboard(
                "{날짜} 출고 송장: {송장번호}", mixedMemos, null);
            Assert(resolvedTracking == "내일 오전 출고 송장: 456-8280-8146",
                $"Semantic variable match failed: '{resolvedTracking}'");

            // OCR 이중 엔진(ko+en) 줄바꿈 보존 및 한자/한글 오인식 보정 테스트
            var koLines = new List<(double Y, string Text)>
            {
                (10.0, "456-828堡8146"),
                (30.0, "456-828우8161"),
                (50.0, "배송 문의드립니다")
            };
            var enLines = new List<(double Y, string Text)>
            {
                (10.0, "456-8280-8146"),
                (30.0, "456-8280-8161"),
                (50.0, "Baesong Muneui")
            };
            string ocrCombined = OcrService.SelectBestOcrLines(koLines, enLines);
            string expectedOcr = string.Join(Environment.NewLine, new[] { "456-8280-8146", "456-8280-8161", "배송 문의드립니다" });
            Assert(ocrCombined == expectedOcr, $"OCR line/numeric selection failed: '{ocrCombined}'");
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
            if (File.Exists(testCsv)) File.Delete(testCsv);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[TemplateManagerTests FAILED] {message}");
    }
}
