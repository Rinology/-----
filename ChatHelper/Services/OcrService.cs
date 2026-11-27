using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Storage.Streams;
using Wgi = Windows.Graphics.Imaging;
using WpfImaging = System.Windows.Media.Imaging;

namespace ChatHelper.Services;

/// <summary>
/// Windows 10/11 네이티브 내장 OCR 서비스 (0.05초 초고속 한글/영문/숫자 인식)
/// - Win+Shift+S Alpha=0 투명 버그 자동 복구
/// - ocrResult.Lines 기반 줄바꿈 보존
/// - 한(ko) + 영(en) 이중 엔진 교차 검증으로 송장번호/숫자 한자·한글 오인식 방지
/// </summary>
public static class OcrService
{
    private static OcrEngine? _koreanEngine;
    private static OcrEngine? _englishEngine;
    private static readonly object _lock = new();

    private static (OcrEngine? Ko, OcrEngine? En) GetEngines()
    {
        if (_koreanEngine != null) return (_koreanEngine, _englishEngine);

        lock (_lock)
        {
            if (_koreanEngine != null) return (_koreanEngine, _englishEngine);

            try
            {
                var ko = new Language("ko");
                if (OcrEngine.IsLanguageSupported(ko))
                    _koreanEngine = OcrEngine.TryCreateFromLanguage(ko);
                _koreanEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();

                var en = new Language("en");
                if (OcrEngine.IsLanguageSupported(en))
                    _englishEngine = OcrEngine.TryCreateFromLanguage(en);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OcrService] Engine init failed: {ex.Message}");
            }

            return (_koreanEngine, _englishEngine);
        }
    }

    /// <summary>
    /// 클립보드 또는 메모리의 WPF BitmapSource 이미지를 인식하여 줄바꿈이 보존된 텍스트로 추출
    /// </summary>
    public static async Task<string> RecognizeTextAsync(BitmapSource bitmapSource)
    {
        try
        {
            var (koEngine, enEngine) = GetEngines();
            if (koEngine == null || bitmapSource == null)
                return string.Empty;

            var normalizedSource = NormalizeForOcr(bitmapSource);

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                var encoder = new WpfImaging.PngBitmapEncoder();
                encoder.Frames.Add(WpfImaging.BitmapFrame.Create(normalizedSource));
                encoder.Save(ms);
                bytes = ms.ToArray();
            }

            if (bytes.Length == 0) return string.Empty;

            var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            ras.Seek(0);
            var decoder = await Wgi.BitmapDecoder.CreateAsync(ras);
            using var rawBitmap = await decoder.GetSoftwareBitmapAsync();
            using var softwareBitmap = Wgi.SoftwareBitmap.Convert(rawBitmap, Wgi.BitmapPixelFormat.Bgra8, Wgi.BitmapAlphaMode.Premultiplied);

            var koResult = await koEngine.RecognizeAsync(softwareBitmap);
            var koLines = ExtractLines(koResult);

            List<(double Y, string Text)> enLines = new();
            if (enEngine != null)
            {
                try
                {
                    var enResult = await enEngine.RecognizeAsync(softwareBitmap);
                    enLines = ExtractLines(enResult);
                }
                catch { }
            }

            return SelectBestOcrLines(koLines, enLines);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OcrService", "OCR 이미지 텍스트 추출 실패", ex);
            return string.Empty;
        }
    }

    private static List<(double Y, string Text)> ExtractLines(OcrResult? result)
    {
        if (result?.Lines == null || result.Lines.Count == 0)
            return new List<(double Y, string Text)>();

        return result.Lines
            .Select(l =>
            {
                double y = l.Words.Count > 0 ? l.Words.Min(w => w.BoundingRect.Top) : 0;
                return (Y: y, Text: (l.Text ?? string.Empty).Trim());
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.Y)
            .ToList();
    }

    /// <summary>
    /// 한국어(ko) OCR 줄과 영문/숫자(en) OCR 줄을 비교하여 줄바꿈을 보존하고,
    /// 숫자·송장번호 줄에서 발생한 한자/한글 오인식을 영문 엔진 결과로 자동 보정합니다.
    /// </summary>
    public static string SelectBestOcrLines(
        IReadOnlyList<(double Y, string Text)> koLines,
        IReadOnlyList<(double Y, string Text)> enLines)
    {
        if (koLines.Count == 0 && enLines.Count == 0) return string.Empty;
        if (koLines.Count == 0) return string.Join(Environment.NewLine, enLines.Select(l => l.Text));
        if (enLines.Count == 0) return string.Join(Environment.NewLine, koLines.Select(l => l.Text));

        var finalLines = new List<string>(koLines.Count);
        foreach (var (y, koText) in koLines)
        {
            var matchEn = enLines
                .OrderBy(e => Math.Abs(e.Y - y))
                .FirstOrDefault(e => Math.Abs(e.Y - y) <= 18.0);

            if (!string.IsNullOrWhiteSpace(matchEn.Text) && ShouldPreferEnglishLine(koText, matchEn.Text))
            {
                // 하이픈 주변 불필요한 공백 정리 및 숫자 사이 '&' 오인식 보정 + 한/영/숫자/기본기호 정제
                string cleanedEn = Regex.Replace(matchEn.Text, @"\s*-\s*", "-");
                cleanedEn = FilterKoreanAndEnglishOnly(NormalizeNumericOcrArtifacts(cleanedEn));
                if (!string.IsNullOrWhiteSpace(cleanedEn))
                    finalLines.Add(cleanedEn);
            }
            else
            {
                string cleanedKo = FilterKoreanAndEnglishOnly(NormalizeNumericOcrArtifacts(koText));
                if (!string.IsNullOrWhiteSpace(cleanedKo))
                    finalLines.Add(cleanedKo);
            }
        }

        return string.Join(Environment.NewLine, finalLines);
    }

    /// <summary>
    /// OCR 결과에서 한자·특수도형 깨짐(예: 싯포스二>, 셋ㅁ)을 제거하고 한글·영문·숫자·기본 문장부호만 남깁니다.
    /// </summary>
    public static string FilterKoreanAndEnglishOnly(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;

        // 특수 화살표/구분자(▶, ►, 二>)를 깔끔한 구분 공백 또는 '>'로 정규화
        string s = line.Replace("▶", " > ").Replace("►", " > ").Replace("▷", " > ");
        s = Regex.Replace(s, @"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF]+", "");
        // 한글(완성형 가-힣), 영문(A-Za-z), 숫자(0-9), 기본 문장부호만 허용 (깨진 단독 자모 ㅁ 등 제거)
        s = Regex.Replace(s, @"[^\uAC00-\uD7A3A-Za-z0-9\s.,_+/()\[\]:~!?%>=-]", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s;
    }

    /// <summary>
    /// 숫자 송장번호 등에서 '0-'가 '&amp;' 또는 한자(年 등)로 뭉개지는 OCR 오인식(예: 456-828&amp;8150, 69013年 1802438 → 690130-1802438)을 보정합니다.
    /// </summary>
    public static string NormalizeNumericOcrArtifacts(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;
        string s = Regex.Replace(line, @"(?<=\d{3})&(?=\d{4})", "0-");
        s = Regex.Replace(s, @"(?<=\d)[\u4E00-\u9FFF]\s*(?=\d)", "0-");
        return s;
    }

    private static bool ShouldPreferEnglishLine(string koText, string enText)
    {
        bool hasHanja = koText.Any(c => c >= '\u4E00' && c <= '\u9FFF');
        int koDigits = koText.Count(char.IsDigit);
        int enDigits = enText.Count(char.IsDigit);

        // 한자가 섞여 있고 영문 엔진 결과에 숫자가 더 많거나 같으면 영문 엔진 채택
        if (hasHanja && enDigits >= koDigits)
            return true;

        // 숫자·하이픈 위주 라인에서 한글 자모/단일 음절이 숫자 사이에 끼어든 경우 (예: 456-828우8161)
        bool hasStrayHangulBetweenDigits = Regex.IsMatch(koText, @"\d[\uAC00-\uD7A3\u3131-\u318E]\d");
        if (hasStrayHangulBetweenDigits && enDigits > koDigits)
            return true;

        // 전체적으로 숫자가 4개 이상이고, 한글 완성 음절이 1개 이하이며, enDigits >= koDigits 인 코드/송장 줄
        int hangulCount = koText.Count(c => c >= '\uAC00' && c <= '\uD7A3');
        if (enDigits >= 4 && hangulCount <= 1 && enDigits >= koDigits)
            return true;

        return false;
    }

    /// <summary>
    /// 윈도우 캡처 도구(Win+Shift+S) Alpha=0 복원 + 대비 강화(배경 노이즈 제거) + 고품질 확대 + 여백 추가
    /// </summary>
    private static BitmapSource NormalizeForOcr(BitmapSource src)
    {
        BitmapSource formatted = src;

        if (formatted.Format != PixelFormats.Bgra32)
        {
            formatted = new FormatConvertedBitmap(formatted, PixelFormats.Bgra32, null, 0);
        }

        int w = formatted.PixelWidth;
        int h = formatted.PixelHeight;
        int stride = w * 4;
        byte[] pixels = new byte[h * stride];
        formatted.CopyPixels(pixels, stride, 0);

        int zeroAlphaCount = 0;
        int sampleStep = Math.Max(1, (h * w) / 2000);
        int sampleTotal = 0;

        for (int i = 3; i < pixels.Length; i += 4 * sampleStep)
        {
            sampleTotal++;
            if (pixels[i] == 0) zeroAlphaCount++;
        }

        bool fixAlpha = sampleTotal > 0 && (double)zeroAlphaCount / sampleTotal > 0.90;

        // Alpha 복원 + 그레이스케일 대비 보정 (밝은 회색/파란 선택 배경을 흰색으로 밀어 표/숫자 인식률 극대화)
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (fixAlpha) pixels[i + 3] = 255;

            int lum = (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
            byte enhanced;
            if (lum > 185) enhanced = 255;
            else if (lum < 75) enhanced = 0;
            else enhanced = (byte)((lum - 75) * 255 / 110);

            pixels[i] = enhanced;
            pixels[i + 1] = enhanced;
            pixels[i + 2] = enhanced;
        }

        formatted = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        // 2. 해상도 보정: 너무 큰 경우 축소, 좁거나 작은 표/문장 캡처는 최대 2200px 한도 내 2~3배 확대
        if (w > 2400 || h > 2400)
        {
            double scale = Math.Min(2400.0 / w, 2400.0 / h);
            formatted = new TransformedBitmap(formatted, new ScaleTransform(scale, scale));
        }
        else if (h < 60 || w < 240)
        {
            double scale = Math.Min(3.0, Math.Min(2200.0 / w, 2200.0 / h));
            if (scale > 1.05)
                formatted = new TransformedBitmap(formatted, new ScaleTransform(scale, scale));
        }
        else if (h < 200 || w < 520)
        {
            double scale = Math.Min(2.0, Math.Min(2200.0 / w, 2200.0 / h));
            if (scale > 1.05)
                formatted = new TransformedBitmap(formatted, new ScaleTransform(scale, scale));
        }

        // 3. 24px 흰색 여백 추가
        int pw = formatted.PixelWidth;
        int ph = formatted.PixelHeight;
        if (pw > 0 && ph > 0 && pw < 2300 && ph < 2300)
        {
            const int pad = 24;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, pw + pad * 2, ph + pad * 2));
                dc.DrawImage(formatted, new System.Windows.Rect(pad, pad, pw, ph));
            }
            var rtb = new RenderTargetBitmap(pw + pad * 2, ph + pad * 2, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            formatted = rtb;
        }

        if (formatted.CanFreeze && !formatted.IsFrozen)
            formatted.Freeze();

        return formatted;
    }
}
