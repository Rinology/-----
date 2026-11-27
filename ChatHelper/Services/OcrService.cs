using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RapidOcrNet;
using SkiaSharp;
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Storage.Streams;
using Wgi = Windows.Graphics.Imaging;
using WpfImaging = System.Windows.Media.Imaging;

namespace ChatHelper.Services;

/// <summary>
/// 2단계 고성능 OCR 서비스 (1순위: PaddleOCR PP-OCRv5 한국어 ONNX 로컬 엔진 / 2순위: Windows 내장 OCR 폴백)
/// - Win+Shift+S Alpha=0 투명 버그 자동 복구 & 다크배경 반전
/// - 작은 표/UI 글씨(11~13pt) 고품질 확대 후 PP-OCRv5로 한/영/숫자/소수점/기호 고정밀 인식
/// - TextBlocks Y/X 좌표 정렬로 줄바꿈 보존
/// </summary>
public static class OcrService
{
    private static OcrEngine? _koreanEngine;
    private static OcrEngine? _englishEngine;
    private static RapidOcr? _rapidOcr;
    private static bool _rapidOcrInitAttempted;
    private static readonly object _lock = new();
    private static readonly object _rapidInferLock = new();

    private static RapidOcr? GetRapidOcr()
    {
        if (_rapidOcr != null) return _rapidOcr;
        if (_rapidOcrInitAttempted) return null;

        lock (_lock)
        {
            if (_rapidOcr != null) return _rapidOcr;
            if (_rapidOcrInitAttempted) return null;
            _rapidOcrInitAttempted = true;

            try
            {
                string ocrDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Ocr");
                string detPath = Path.Combine(ocrDir, "ch_PP-OCRv5_mobile_det.onnx");
                string clsPath = Path.Combine(ocrDir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
                string recPath = Path.Combine(ocrDir, "korean_PP-OCRv5_rec_mobile_infer.onnx");
                string keysPath = Path.Combine(ocrDir, "ppocrv5_korean_dict.txt");

                if (!File.Exists(detPath) || !File.Exists(clsPath) || !File.Exists(recPath) || !File.Exists(keysPath))
                {
                    AppLogger.Warn("OcrService", $"PP-OCRv5 한국어 모델 파일 없음 (Windows OCR 폴백 사용): {ocrDir}");
                    return null;
                }

                var modelSet = RapidOcrModelSet.PPOCRv5Latin with
                {
                    DetModelPath = detPath,
                    ClsModelPath = clsPath,
                    RecModelPath = recPath,
                    KeysPath = keysPath
                };

                var engine = new RapidOcr();
                engine.InitModels(modelSet);
                _rapidOcr = engine;
                AppLogger.Info("OcrService", "PP-OCRv5 한국어 ONNX 엔진 초기화 완료");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("OcrService", "PP-OCRv5 한국어 엔진 초기화 실패 (Windows OCR 폴백 사용)", ex);
                _rapidOcr = null;
            }

            return _rapidOcr;
        }
    }

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
            if (bitmapSource == null)
                return string.Empty;

            // 1순위: PaddleOCR PP-OCRv5 한국어 ONNX 엔진
            var rapid = GetRapidOcr();
            if (rapid != null)
            {
                string rapidText = await Task.Run(() => RecognizeWithRapidOcr(rapid, bitmapSource));
                if (!string.IsNullOrWhiteSpace(rapidText))
                    return rapidText;
            }

            // 2순위 폴백: Windows 내장 OCR
            var (koEngine, enEngine) = GetEngines();
            if (koEngine == null)
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
            var koLineData = ExtractLineData(koResult);

            List<OcrLineData> enLineData = new();
            if (enEngine != null)
            {
                try
                {
                    var enResult = await enEngine.RecognizeAsync(softwareBitmap);
                    enLineData = ExtractLineData(enResult);
                }
                catch { }
            }

            return SelectBestOcrLines(koLineData, enLineData);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OcrService", "OCR 이미지 텍스트 추출 실패", ex);
            return string.Empty;
        }
    }

    private static string RecognizeWithRapidOcr(RapidOcr engine, BitmapSource src)
    {
        try
        {
            using var skBmp = ConvertToSkBitmapForRapidOcr(src);
            if (skBmp == null) return string.Empty;

            var options = RapidOcrOptions.PythonCompat with
            {
                Padding = 32,
                DoAngle = false,
                TextScore = 0.45f
            };

            RapidOcrNet.OcrResult result;
            lock (_rapidInferLock)
            {
                result = engine.Detect(skBmp, options);
            }

            if (result?.TextBlocks == null || result.TextBlocks.Length == 0)
                return string.Empty;

            var blocks = result.TextBlocks
                .Where(b => !string.IsNullOrWhiteSpace(b.Text) && b.BoxPoints != null && b.BoxPoints.Length >= 4)
                .Select(b =>
                {
                    double minY = b.BoxPoints.Min(p => (double)p.Y);
                    double maxY = b.BoxPoints.Max(p => (double)p.Y);
                    double minX = b.BoxPoints.Min(p => (double)p.X);
                    return (CenterY: (minY + maxY) * 0.5, Height: Math.Max(10.0, maxY - minY), Left: minX, Text: b.Text.Trim());
                })
                .OrderBy(b => b.CenterY)
                .ToList();

            if (blocks.Count == 0) return string.Empty;

            var lines = new List<List<(double Left, string Text)>>();
            var lineAnchors = new List<(double CenterY, double Height)>();

            foreach (var b in blocks)
            {
                int matchedLine = -1;
                for (int i = 0; i < lineAnchors.Count; i++)
                {
                    double tol = Math.Max(lineAnchors[i].Height, b.Height) * 0.55;
                    if (Math.Abs(b.CenterY - lineAnchors[i].CenterY) <= tol)
                    {
                        matchedLine = i;
                        break;
                    }
                }

                if (matchedLine >= 0)
                {
                    lines[matchedLine].Add((b.Left, b.Text));
                }
                else
                {
                    lines.Add(new List<(double Left, string Text)> { (b.Left, b.Text) });
                    lineAnchors.Add((b.CenterY, b.Height));
                }
            }

            var outputLines = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                string raw = string.Join(" ", line.OrderBy(w => w.Left).Select(w => w.Text));
                string cleaned = FilterKoreanAndEnglishOnly(NormalizeNumericOcrArtifacts(raw));
                if (!string.IsNullOrWhiteSpace(cleaned))
                    outputLines.Add(cleaned);
            }

            return string.Join(Environment.NewLine, outputLines);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OcrService", "RapidOcr 인식 중 오류 (Windows OCR 폴백)", ex);
            return string.Empty;
        }
    }

    private static SKBitmap? ConvertToSkBitmapForRapidOcr(BitmapSource src)
    {
        BitmapSource formatted = src;
        if (formatted.Format != PixelFormats.Bgra32)
            formatted = new FormatConvertedBitmap(formatted, PixelFormats.Bgra32, null, 0);

        int w = formatted.PixelWidth;
        int h = formatted.PixelHeight;
        if (w <= 0 || h <= 0) return null;

        int stride = w * 4;
        byte[] pixels = new byte[h * stride];
        formatted.CopyPixels(pixels, stride, 0);

        int zeroAlphaCount = 0;
        int sampleStep = Math.Max(1, (h * w) / 2000);
        int sampleTotal = 0;
        long lumSum = 0;

        for (int i = 0; i < pixels.Length; i += 4 * sampleStep)
        {
            sampleTotal++;
            if (pixels[i + 3] == 0) zeroAlphaCount++;
            lumSum += (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
        }

        bool fixAlpha = sampleTotal > 0 && (double)zeroAlphaCount / sampleTotal > 0.90;
        bool isDarkBg = sampleTotal > 0 && (lumSum / sampleTotal) < 110;

        if (fixAlpha || isDarkBg)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (fixAlpha) pixels[i + 3] = 255;
                if (isDarkBg)
                {
                    pixels[i] = (byte)(255 - pixels[i]);
                    pixels[i + 1] = (byte)(255 - pixels[i + 1]);
                    pixels[i + 2] = (byte)(255 - pixels[i + 2]);
                }
            }
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var skBmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, skBmp.GetPixels(), pixels.Length);

        // 작은 표/UI 캡처는 2배 고품질 확대해 DBNet 검출률 및 CRNN 소수점 인식률 향상
        if (w < 600 || h < 240)
        {
            double scale = Math.Min(2.0, Math.Min(2000.0 / w, 2000.0 / h));
            if (scale > 1.05)
            {
                int sw = Math.Max(1, (int)Math.Round(w * scale));
                int sh = Math.Max(1, (int)Math.Round(h * scale));
                var resized = skBmp.Resize(new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized != null)
                {
                    skBmp.Dispose();
                    return resized;
                }
            }
        }

        return skBmp;
    }

    public readonly record struct OcrWordData(double Left, double Right, string Text);
    public readonly record struct OcrLineData(double Y, double Height, string Text, IReadOnlyList<OcrWordData> Words);

    private static List<OcrLineData> ExtractLineData(Windows.Media.Ocr.OcrResult? result)
    {
        if (result?.Lines == null || result.Lines.Count == 0)
            return new List<OcrLineData>();

        return result.Lines
            .Select(l =>
            {
                double y = l.Words.Count > 0 ? l.Words.Min(w => w.BoundingRect.Top) : 0;
                double h = l.Words.Count > 0 ? l.Words.Max(w => w.BoundingRect.Bottom) - y : 20;
                var words = l.Words
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .Select(w => new OcrWordData(w.BoundingRect.Left, w.BoundingRect.Right, w.Text.Trim()))
                    .OrderBy(w => w.Left)
                    .ToList();
                return new OcrLineData(y, Math.Max(12, h), (l.Text ?? string.Empty).Trim(), words);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.Y)
            .ToList();
    }

    /// <summary>
    /// 한국어(ko) OCR 줄과 영문/숫자(en) OCR 줄을 단어 단위 바운딩 박스로 교차 검증하여
    /// 한·영·숫자 혼합 줄에서도 영문/소수점/괄호/송장번호 오인식을 최소화합니다.
    /// </summary>
    public static string SelectBestOcrLines(
        IReadOnlyList<OcrLineData> koLines,
        IReadOnlyList<OcrLineData> enLines)
    {
        if (koLines.Count == 0 && enLines.Count == 0) return string.Empty;
        if (koLines.Count == 0) return string.Join(Environment.NewLine, enLines.Select(l => FilterKoreanAndEnglishOnly(l.Text)).Where(s => !string.IsNullOrWhiteSpace(s)));
        if (enLines.Count == 0) return string.Join(Environment.NewLine, koLines.Select(l => FilterKoreanAndEnglishOnly(NormalizeNumericOcrArtifacts(l.Text))).Where(s => !string.IsNullOrWhiteSpace(s)));

        var finalLines = new List<string>(koLines.Count);
        foreach (var koLine in koLines)
        {
            double yTol = Math.Max(20.0, koLine.Height * 0.8);
            var matchEn = enLines
                .OrderBy(e => Math.Abs(e.Y - koLine.Y))
                .FirstOrDefault(e => Math.Abs(e.Y - koLine.Y) <= yTol);

            string chosen;
            if (!string.IsNullOrWhiteSpace(matchEn.Text) && ShouldPreferEnglishLine(koLine.Text, matchEn.Text))
            {
                chosen = Regex.Replace(matchEn.Text, @"\s*-\s*", "-");
            }
            else if (!string.IsNullOrWhiteSpace(matchEn.Text) && koLine.Words.Count > 0 && matchEn.Words.Count > 0)
            {
                chosen = MergeKoAndEnWords(koLine.Words, matchEn.Words);
            }
            else
            {
                chosen = koLine.Text;
            }

            string cleaned = FilterKoreanAndEnglishOnly(NormalizeNumericOcrArtifacts(chosen));
            if (!string.IsNullOrWhiteSpace(cleaned))
                finalLines.Add(cleaned);
        }

        return string.Join(Environment.NewLine, finalLines);
    }

    private static string MergeKoAndEnWords(IReadOnlyList<OcrWordData> koWords, IReadOnlyList<OcrWordData> enWords)
    {
        var merged = new List<string>(koWords.Count);
        foreach (var kw in koWords)
        {
            string koText = kw.Text;
            // 이 한글 엔진 단어와 가로 영역이 겹치는 영문 엔진 단어들 찾기
            var overlappingEn = enWords
                .Where(ew => Math.Min(kw.Right, ew.Right) - Math.Max(kw.Left, ew.Left) > 2.0)
                .ToList();

            if (overlappingEn.Count == 0)
            {
                merged.Add(koText);
                continue;
            }

            string enCombined = string.Join("", overlappingEn.Select(e => e.Text));
            if (ShouldReplaceWordWithEnglish(koText, enCombined))
            {
                merged.Add(enCombined);
            }
            else
            {
                merged.Add(koText);
            }
        }
        return string.Join(" ", merged);
    }

    private static bool ShouldReplaceWordWithEnglish(string koWord, string enWord)
    {
        if (string.IsNullOrWhiteSpace(enWord)) return false;
        // enWord는 출력 가능한 ASCII 문자만 포함하고, 영문자나 숫자가 2개 이상 있어야 함
        if (!enWord.All(c => c >= 0x20 && c <= 0x7E)) return false;
        int enAlnum = enWord.Count(c => char.IsAsciiLetterOrDigit(c));
        if (enAlnum < 2) return false;

        int koHangul = koWord.Count(c => c >= '\uAC00' && c <= '\uD7A3');
        bool koHasHanja = koWord.Any(c => (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF'));
        int koAlnum = koWord.Count(c => char.IsAsciiLetterOrDigit(c));

        // 1. 한글 엔진이 영문/숫자 단어에 한자를 섞은 경우 (단, 순수 한글 단어 비율이 높으면 제외)
        if (koHasHanja && koHangul <= 1 && enAlnum >= 2)
            return true;

        // 2. 한글이 전혀 없는 순수 영문/숫자/기호 토큰은 영문 엔진 우선 (예: ZOOM, NCX, 15Ah(15T))
        if (koHangul == 0 && !koHasHanja && koAlnum >= 1)
            return true;

        // 3. 숫자 소수점 보존: 영문 엔진에 '숫자.숫자'가 있는데 한글 엔진이 소수점을 빠뜨린 경우 (예: 28.5Ah vs 285Ah)
        if (Regex.IsMatch(enWord, @"\d\.\d") && !Regex.IsMatch(koWord, @"\d\.\d") && koHangul <= 1)
            return true;

        // 4. 숫자/영문이 2개 이상인데 한글이 1글자만 잘못 끼어든 경우 (예: 21지 vs 21X, 29지 vs 29X)
        if (koAlnum >= 2 && koHangul == 1 && enAlnum >= koAlnum)
            return true;

        return false;
    }

    /// <summary>
    /// 한국어(ko) OCR 줄과 영문/숫자(en) OCR 줄을 비교하여 줄바꿈을 보존하고,
    /// 숫자·송장번호 줄에서 발생한 한자/한글 오인식을 영문 엔진 결과로 자동 보정합니다.
    /// </summary>
    public static string SelectBestOcrLines(
        IReadOnlyList<(double Y, string Text)> koLines,
        IReadOnlyList<(double Y, string Text)> enLines)
    {
        var koData = koLines.Select(l => new OcrLineData(l.Y, 20, l.Text, Array.Empty<OcrWordData>())).ToList();
        var enData = enLines.Select(l => new OcrLineData(l.Y, 20, l.Text, Array.Empty<OcrWordData>())).ToList();
        return SelectBestOcrLines(koData, enData);
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
        // 영문/숫자 사이에 끼어든 '丁' 한자 오인식(예: K丁x6핀 -> KTx6핀) 보정
        s = Regex.Replace(s, @"(?<=[A-Za-z0-9])丁(?=[A-Za-z0-9])", "T");
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
    /// 윈도우 캡처 도구(Win+Shift+S) Alpha=0 복원 + 다크모드 반전 + 고품질 선확대 + Unsharp 선명화 + 소프트 대비 보정 + 여백 추가
    /// (소수점·얇은 획 손실 방지를 위해 대비 보정 전에 먼저 고품질 확대를 수행)
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
        long lumSum = 0;

        for (int i = 0; i < pixels.Length; i += 4 * sampleStep)
        {
            sampleTotal++;
            if (pixels[i + 3] == 0) zeroAlphaCount++;
            lumSum += (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
        }

        bool fixAlpha = sampleTotal > 0 && (double)zeroAlphaCount / sampleTotal > 0.90;
        bool isDarkBg = sampleTotal > 0 && (lumSum / sampleTotal) < 115;

        // 1. Alpha 복원 + 그레이스케일 변환 (다크 배경이면 흑백 반전, 얇은 획 보존을 위해 이진화는 아직 하지 않음)
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (fixAlpha) pixels[i + 3] = 255;

            int lum = (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
            if (isDarkBg) lum = 255 - lum;
            byte gray = (byte)Math.Clamp(lum, 0, 255);

            pixels[i] = gray;
            pixels[i + 1] = gray;
            pixels[i + 2] = gray;
        }

        formatted = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        // 2. 고품질 선확대: 작은 표/UI 텍스트(11~13pt)의 1px 획과 소수점(.)을 살리기 위해 먼저 2.5~3배 확대
        double scale = 1.0;
        if (w > 2400 || h > 2400)
        {
            scale = Math.Min(2400.0 / w, 2400.0 / h);
        }
        else if (h < 320 || w < 720)
        {
            scale = Math.Min(3.0, Math.Min(2300.0 / w, 2300.0 / h));
        }
        else if (h < 600 || w < 1100)
        {
            scale = Math.Min(2.0, Math.Min(2300.0 / w, 2300.0 / h));
        }

        if (Math.Abs(scale - 1.0) > 0.05)
        {
            int sw = Math.Max(1, (int)Math.Round(w * scale));
            int sh = Math.Max(1, (int)Math.Round(h * scale));
            var scaleDv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(scaleDv, BitmapScalingMode.HighQuality);
            using (var dc = scaleDv.RenderOpen())
            {
                dc.DrawImage(formatted, new System.Windows.Rect(0, 0, sw, sh));
            }
            var scaledRtb = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
            scaledRtb.Render(scaleDv);
            formatted = scaledRtb;
        }

        // 3. 확대된 이미지에 Unsharp 선명화 + 소프트 감마/대비 보정 적용 (격자선 제거 + 글자 윤곽 또렷화)
        int uw = formatted.PixelWidth;
        int uh = formatted.PixelHeight;
        int uStride = uw * 4;
        byte[] uPixels = new byte[uh * uStride];
        formatted.CopyPixels(uPixels, uStride, 0);

        byte[] grayBuf = new byte[uw * uh];
        for (int y = 0; y < uh; y++)
        {
            int rowOff = y * uStride;
            int gOff = y * uw;
            for (int x = 0; x < uw; x++)
            {
                grayBuf[gOff + x] = uPixels[rowOff + x * 4];
            }
        }

        for (int y = 0; y < uh; y++)
        {
            int rowOff = y * uStride;
            int gOff = y * uw;
            for (int x = 0; x < uw; x++)
            {
                int center = grayBuf[gOff + x];
                int val = center;
                if (x > 0 && x < uw - 1 && y > 0 && y < uh - 1)
                {
                    int neighborAvg = (
                        grayBuf[gOff - uw + x] +
                        grayBuf[gOff + uw + x] +
                        grayBuf[gOff + x - 1] +
                        grayBuf[gOff + x + 1]) >> 2;
                    // 과도한 선명화는 'ㅁ→ㅇ', 'ㄹ→ㄷ' 뭉개짐을 유발하므로 15%만 약하게 적용
                    val = center + ((center - neighborAvg) * 15) / 100;
                }

                byte enhanced;
                if (val >= 225) enhanced = 255;
                else if (val <= 40) enhanced = 0;
                else
                {
                    enhanced = (byte)Math.Clamp((val - 40) * 255 / 185, 0, 255);
                }

                int idx = rowOff + x * 4;
                uPixels[idx] = enhanced;
                uPixels[idx + 1] = enhanced;
                uPixels[idx + 2] = enhanced;
                uPixels[idx + 3] = 255;
            }
        }

        formatted = BitmapSource.Create(uw, uh, 96, 96, PixelFormats.Bgra32, null, uPixels, uStride);

        // 4. 32px 흰색 여백 추가
        if (uw > 0 && uh > 0 && uw < 2360 && uh < 2360)
        {
            const int pad = 32;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, uw + pad * 2, uh + pad * 2));
                dc.DrawImage(formatted, new System.Windows.Rect(pad, pad, uw, uh));
            }
            var rtb = new RenderTargetBitmap(uw + pad * 2, uh + pad * 2, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            formatted = rtb;
        }

        if (formatted.CanFreeze && !formatted.IsFrozen)
            formatted.Freeze();

        return formatted;
    }
}
