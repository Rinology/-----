using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ChatHelper.Services;

/// <summary>
/// 입력 버퍼에서 사칙연산 수식(예: "5 * 10 =", "12000 + 3000")을 감지하고 정확한 계산 결과를 반환합니다.
/// </summary>
public static class MathCalcHelper
{
    private static readonly Regex MathExprRegex = new(
        @"^\s*(\(?[\d]+(?:\.[\d]+)?\)?(?:\s*[\+\-\*\/]\s*\(?[\d]+(?:\.[\d]+)?\)?)+)\s*=?\s*$",
        RegexOptions.Compiled);

    public static bool TryEvaluate(string input, out string resultOnly, out string fullEquation)
    {
        resultOnly = string.Empty;
        fullEquation = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = MathExprRegex.Match(input);
        if (!match.Success)
            return false;

        string expr = match.Groups[1].Value.Trim();
        try
        {
            using var dt = new DataTable();
            object? val = dt.Compute(expr, null);
            if (val == null || val == DBNull.Value)
                return false;

            double num = Convert.ToDouble(val, CultureInfo.InvariantCulture);
            if (double.IsNaN(num) || double.IsInfinity(num))
                return false;

            // 정수로 떨어지면 소수점 없이, 소수면 불필요한 0 제거
            resultOnly = Math.Abs(num - Math.Round(num)) < 1e-9
                ? Math.Round(num).ToString("0", CultureInfo.InvariantCulture)
                : num.ToString("0.####", CultureInfo.InvariantCulture);

            fullEquation = $"{expr} = {resultOnly}";
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikePartialMath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        string trimmed = input.Trim();
        return char.IsDigit(trimmed[0]) && Regex.IsMatch(trimmed, @"^[\d\s\+\-\*\/\(\)\.]+$");
    }
}
