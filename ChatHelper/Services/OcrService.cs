using System;
using System.Diagnostics;
using System.IO;
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
/// Windows 10/11 네이티브 내장 OCR 서비스 (0.05초 초고속 한글/영문 인식)
/// 윈도우 10/11 캡처 도구(Win+Shift+S)의 32비트 DIB 투명(Alpha=0) 버그 완벽 자동 보정
/// </summary>
public static class OcrService
{
    private static OcrEngine? _koreanEngine;
    private static readonly object _lock = new();

    private static OcrEngine? GetEngine()
    {
        if (_koreanEngine != null) return _koreanEngine;

        lock (_lock)
        {
            if (_koreanEngine != null) return _koreanEngine;

            try
            {
                var lang = new Language("ko");
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    _koreanEngine = OcrEngine.TryCreateFromLanguage(lang);
                }

                _koreanEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OcrService] Engine init failed: {ex.Message}");
            }

            return _koreanEngine;
        }
    }

    /// <summary>
    /// 클립보드 또는 메모리의 WPF BitmapSource 이미지를 인식하여 한글 텍스트로 추출
    /// </summary>
    public static async Task<string> RecognizeTextAsync(BitmapSource bitmapSource)
    {
        try
        {
            var engine = GetEngine();
            if (engine == null || bitmapSource == null)
            {
                return string.Empty;
            }

            // 1. 캡처 도구의 Alpha=0 투명 버그 해결 및 이미지 정규화
            var normalizedSource = NormalizeForOcr(bitmapSource);

            // 2. WPF BitmapSource를 PNG byte 배열로 변환
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                var encoder = new WpfImaging.PngBitmapEncoder();
                encoder.Frames.Add(WpfImaging.BitmapFrame.Create(normalizedSource));
                encoder.Save(ms);
                bytes = ms.ToArray();
            }

            if (bytes.Length == 0) return string.Empty;

            // 3. DataWriter를 통해 WinRT InMemoryRandomAccessStream에 안전하게 쓰기
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

            var ocrResult = await engine.RecognizeAsync(softwareBitmap);
            return ocrResult.Text?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OcrService", "OCR 이미지 텍스트 추출 실패", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// 윈도우 캡처 도구(Win+Shift+S)에서 발생하는 32비트 DIB Alpha=0(완전 투명) 현상을 감지하여
    /// 모든 픽셀의 Alpha를 255(완전 불투명)로 복원하고, 작은 말풍선 캡처는 2배 확대 및 최대 해상도 제한 내로 보정
    /// </summary>
    private static BitmapSource NormalizeForOcr(BitmapSource src)
    {
        BitmapSource formatted = src;

        // 1. 반드시 스케일링(TransformedBitmap) 전에 Bgra32 픽셀의 Alpha=0 투명 버그를 먼저 복원해야
        // 보간 과정에서 RGB 채널이 0(검은색)으로 소실되지 않음!
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

        if (sampleTotal > 0 && (double)zeroAlphaCount / sampleTotal > 0.90)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            formatted = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        }

        // 2. Alpha 복원 완료 후 해상도 보정: 너무 큰 경우 축소(2400px 이하), 작은 한 줄 말풍선은 2배 확대
        if (w > 2400 || h > 2400)
        {
            double scale = Math.Min(2400.0 / w, 2400.0 / h);
            formatted = new TransformedBitmap(formatted, new ScaleTransform(scale, scale));
        }
        else if (h < 90 || w < 350)
        {
            formatted = new TransformedBitmap(formatted, new ScaleTransform(2.0, 2.0));
        }

        if (formatted.CanFreeze && !formatted.IsFrozen)
        {
            formatted.Freeze();
        }

        return formatted;
    }
}
