using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Storage.Streams;

namespace ChatHelper.Tests;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0] == "--ocr")
        {
            RunDirectOcrTest();
            return;
        }

        Console.WriteLine("=== ChatHelper Unit Tests ===");
        AuthServiceTests.RunAll();
        Console.WriteLine("[PASS] AuthServiceTests");

        ConfigManagerTests.RunAll();
        Console.WriteLine("[PASS] ConfigManagerTests");

        TemplateManagerTests.RunAll();
        Console.WriteLine("[PASS] TemplateManagerTests");

        AiAndOcrIntegrationTests.RunAll();
        Console.WriteLine("[PASS] AiAndOcrIntegrationTests");

        KeyboardHookAndHotkeyTests.RunAll();
        Console.WriteLine("[PASS] KeyboardHookAndHotkeyTests");

        Console.WriteLine("All unit tests passed!");
    }

    private static void RunDirectOcrTest()
    {
        Console.WriteLine("=== Direct OCR Test ===");

        // Get clipboard image directly in STA thread
        var data = System.Windows.Clipboard.GetDataObject();
        var img = System.Windows.Clipboard.GetImage();
        if (img == null)
        {
            Console.WriteLine("Image is null on clipboard!");
            return;
        }

        Console.WriteLine($"Image size: {img.PixelWidth}x{img.PixelHeight}");

        try
        {
            // 1. Save to memory stream
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            encoder.Save(ms);
            byte[] bytes = ms.ToArray();
            Console.WriteLine($"PNG bytes: {bytes.Length}");

            // 2. Write to WinRT stream using DataWriter safely
            var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                writer.StoreAsync().AsTask().Wait();
                writer.FlushAsync().AsTask().Wait();
                writer.DetachStream();
            }

            ras.Seek(0);
            var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras).AsTask().Result;
            using var raw = decoder.GetSoftwareBitmapAsync().AsTask().Result;
            using var softwareBitmap = SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var lang = new Language("ko");
            var engine = OcrEngine.TryCreateFromLanguage(lang) ?? OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                Console.WriteLine("No OCR Engine available!");
                return;
            }

            var ocrResult = engine.RecognizeAsync(softwareBitmap).AsTask().Result;
            Console.WriteLine($"OCR Extracted Text: '{ocrResult.Text}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during OCR: {ex}");
        }
    }
}
