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

            // 트리거 키 + 키워드 매칭 테스트 ("\qv" 입력 시 백스페이스 대상 길이인 'qv'.Length = 2)
            var candidates = templateManager.FindMatchingCandidates("\\qv", out int matchedLen);
            Assert(candidates.Count >= 1, "Should find candidates starting with qv");
            Assert(candidates[0].Keyword == "qv", "First candidate should be 'qv'");
            Assert(matchedLen == 2, $"Matched length should be 2 ('qv' to be erased), got {matchedLen}");
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
