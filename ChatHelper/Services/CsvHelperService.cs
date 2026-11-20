using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatHelper.Services;

/// <summary>
/// RFC 4180 호환 공통 CSV 파서 및 직렬화 이스케이프 유틸리티.
/// </summary>
public static class CsvHelperService
{
    public static List<List<string>> ParseRecords(string csvText)
    {
        var records = new List<List<string>>();
        if (string.IsNullOrEmpty(csvText))
            return records;

        var currentRecord = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csvText.Length; i++)
        {
            char c = csvText[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < csvText.Length && csvText[i + 1] == '\"')
                {
                    currentField.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                currentRecord.Add(currentField.ToString());
                currentField.Clear();
            }
            else if ((c == '\r' || c == '\n') && !inQuotes)
            {
                if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                {
                    i++;
                }
                currentRecord.Add(currentField.ToString());
                currentField.Clear();

                if (currentRecord.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    records.Add(currentRecord);
                }
                currentRecord = new List<string>();
            }
            else
            {
                currentField.Append(c);
            }
        }

        if (currentField.Length > 0 || currentRecord.Count > 0)
        {
            currentRecord.Add(currentField.ToString());
            if (currentRecord.Any(f => !string.IsNullOrWhiteSpace(f)))
            {
                records.Add(currentRecord);
            }
        }

        return records;
    }

    public static string EscapeField(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r'))
        {
            return $"\"{v.Replace("\"", "\"\"")}\"";
        }
        return v;
    }
}
