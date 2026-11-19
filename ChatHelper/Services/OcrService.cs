using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Storage.Streams;
using Wgi = Windows.Graphics.Imaging;
using WpfImaging = System.Windows.Media.Imaging;

namespace ChatHelper.Services;

/// <summary>
/// Windows 10/11 네이티브 내장 OCR 서비스 (0.05초 초고속 한글/영문 인식)
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
            if (engine == null)
            {
                return string.Empty;
            }

            // 1. WPF BitmapSource를 PNG byte 배열로 변환
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                var encoder = new WpfImaging.PngBitmapEncoder();
                encoder.Frames.Add(WpfImaging.BitmapFrame.Create(bitmapSource));
                encoder.Save(ms);
                bytes = ms.ToArray();
            }

            if (bytes.Length == 0) return string.Empty;

            // 2. DataWriter를 통해 WinRT InMemoryRandomAccessStream에 안전하게 쓰기 (Dispose 문제 완전 해결)
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
            Debug.WriteLine($"[OcrService] Recognize failed: {ex.Message}");
            return string.Empty;
        }
    }
}
