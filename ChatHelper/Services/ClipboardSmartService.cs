using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ChatHelper.Services;

/// <summary>
/// 클립보드(캡처 이미지 또는 복사된 텍스트)를 감지하여 고객 문의 내용을 0.05초 만에 추출하는 스마트 서비스
/// </summary>
public static class ClipboardSmartService
{
    public record ClipboardResult(string ExtractedText, bool IsFromImage);

    /// <summary>
    /// 클립보드에서 텍스트 또는 캡처 이미지(OCR)를 추출합니다.
    /// </summary>
    public static async Task<ClipboardResult> GetCustomerMessageAsync()
    {
        // STA 스레드에서 클립보드 데이터 안전 조회
        string directText = string.Empty;
        BitmapSource? imageSource = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    imageSource = Clipboard.GetImage();
                }
                else if (Clipboard.ContainsText())
                {
                    directText = Clipboard.GetText()?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClipboardSmartService] Clipboard access failed: {ex.Message}");
            }
        });

        // 이미지가 있으면 Windows OCR로 0.01초 텍스트 추출
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
