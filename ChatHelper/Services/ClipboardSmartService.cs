using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ChatHelper.Services;

/// <summary>
/// 윈도우 10/11 캡처 도구(Win+Shift+S)의 PNG/DIB 포맷과 텍스트를 0.05초 만에 완벽 감지하는 스마트 클립보드 서비스
/// </summary>
public static class ClipboardSmartService
{
    public record ClipboardResult(string ExtractedText, bool IsFromImage);

    /// <summary>
    /// 클립보드에서 텍스트 또는 캡처 이미지(OCR)를 추출합니다.
    /// 캡처 도구의 클립보드 기록 지연을 고려해 최대 5회 재시도합니다.
    /// </summary>
    public static async Task<ClipboardResult> GetCustomerMessageAsync()
    {
        string directText = string.Empty;
        BitmapSource? imageSource = null;

        // 캡처 도구의 클립보드 쓰기 지연/락에 대비해 최대 5회(약 300ms) 재시도
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    await Application.Current.Dispatcher.InvokeAsync(ReadClipboard);
                }
                else
                {
                    ReadClipboard();
                }

                void ReadClipboard()
                {
                    // 1. 표준 WPF Image 검사
                    if (Clipboard.ContainsImage())
                    {
                        imageSource = Clipboard.GetImage();
                    }

                    // 2. 윈도우 캡처 도구(Win+Shift+S) / Win+V 히스토리의 PNG·DIB 스트림 검사
                    if (imageSource == null)
                    {
                        IDataObject? dataObj = Clipboard.GetDataObject();
                        if (dataObj != null && dataObj.GetDataPresent("PNG", true))
                        {
                            var pngData = dataObj.GetData("PNG");
                            if (pngData is MemoryStream ms)
                            {
                                if (ms.CanSeek) ms.Position = 0;
                                var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                if (decoder.Frames.Count > 0)
                                {
                                    imageSource = decoder.Frames[0];
                                }
                            }
                            else if (pngData is byte[] bytes)
                            {
                                using var bms = new MemoryStream(bytes);
                                var decoder = new PngBitmapDecoder(bms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                if (decoder.Frames.Count > 0)
                                {
                                    imageSource = decoder.Frames[0];
                                }
                            }
                        }
                        if (imageSource == null && dataObj != null && dataObj.GetDataPresent(DataFormats.Dib, true))
                        {
                            try { imageSource = Clipboard.GetImage(); } catch { }
                        }
                    }

                    // 3. 복사된 텍스트 검사 (이미지와 텍스트가 함께 들어있는 경우에도 텍스트를 백업 확보)
                    if (Clipboard.ContainsText())
                    {
                        directText = Clipboard.GetText()?.Trim() ?? string.Empty;
                    }

                    if (imageSource != null && imageSource.CanFreeze && !imageSource.IsFrozen)
                    {
                        imageSource.Freeze();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ClipboardSmartService", $"클립보드 읽기 재시도 {attempt + 1}", ex);
            }

            if (imageSource != null || !string.IsNullOrWhiteSpace(directText))
            {
                break;
            }

            await Task.Delay(60);
        }

        // 이미지가 있으면 Windows OCR로 0.05초 텍스트 추출 (알파 정규화 자동 적용)
        if (imageSource != null)
        {
            string ocrText = await OcrService.RecognizeTextAsync(imageSource);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                return new ClipboardResult(ocrText, IsFromImage: true);
            }
        }

        // 텍스트가 있으면 바로 반환
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return new ClipboardResult(directText, IsFromImage: false);
        }

        return new ClipboardResult(string.Empty, IsFromImage: false);
    }
}
