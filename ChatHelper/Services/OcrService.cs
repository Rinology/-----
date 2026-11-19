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
/// Windows 10/11 네이티브 내장 OCR 서비스 (0.01초 초고속 한글/영문 인식)
/// 외부 추가 라이브러리나 인터넷 연결 없이 100% 로컬 메모리에서 실행됩니다.
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
                // 한국어 엔진 우선 시도
                var lang = new Language("ko");
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    _koreanEngine = OcrEngine.TryCreateFromLanguage(lang);
                }

                // fallback: 시스템 기본 언어
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

            // WPF BitmapSource를 SoftwareBitmap으로 변환
            using var memoryStream = new InMemoryRandomAccessStream();
            var encoder = new WpfImaging.PngBitmapEncoder();
            encoder.Frames.Add(WpfImaging.BitmapFrame.Create(bitmapSource));
            
            using (var stream = memoryStream.AsStreamForWrite())
            {
                encoder.Save(stream);
                await stream.FlushAsync();
            }

            memoryStream.Seek(0);
            var decoder = await Wgi.BitmapDecoder.CreateAsync(memoryStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

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
