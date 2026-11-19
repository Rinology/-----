using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class QuickAiWindow : Window
{
    private IntPtr _targetHwnd = IntPtr.Zero;
    private CancellationTokenSource? _cts;

    public QuickAiWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 창을 열고 클립보드(캡처 이미지 또는 복사 텍스트)를 자동 분석하여 AI 답변 생성 시작
    /// </summary>
    public async void ShowForTarget(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        Show();
        Activate();

        TxtStatus.Text = "클립보드 분석 중...";
        TxtAiResponse.Text = string.Empty;

        // 클립보드 분석 (캡처 이미지 OCR 또는 복사된 텍스트)
        var clipResult = await ClipboardSmartService.GetCustomerMessageAsync();

        if (clipResult.IsFromImage)
        {
            TxtSourceType.Text = "📸 [인식: 화면 캡처 이미지 (자동 OCR 추출)]";
            TxtSourceType.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        else
        {
            TxtSourceType.Text = "📋 [인식: 복사된 텍스트]";
            TxtSourceType.Foreground = System.Windows.Media.Brushes.Navy;
        }

        if (!string.IsNullOrWhiteSpace(clipResult.ExtractedText))
        {
            TxtCustomerInput.Text = clipResult.ExtractedText;
            await StartGenerationAsync();
        }
        else
        {
            TxtSourceType.Text = "⚠️ [클립보드가 비어 있습니다]";
            TxtSourceType.Foreground = System.Windows.Media.Brushes.DarkRed;
            TxtCustomerInput.Text = string.Empty;
            TxtStatus.Text = "고객 문의를 직접 입력한 뒤 [F5: 다시 작성]을 누르거나, 캡처(Win+Shift+S) 후 다시 Alt+X를 누르세요.";
            TxtCustomerInput.Focus();
        }
    }

    private async Task StartGenerationAsync()
    {
        string prompt = TxtCustomerInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            TxtStatus.Text = "고객 문의 내용을 입력해 주세요.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        PnlLoading.Visibility = Visibility.Visible;
        TxtStatus.Text = "⚡ 로컬 AI(Qwen 2.5 1.5B)가 친절한 답변을 생성 중입니다...";

        var sw = Stopwatch.StartNew();

        try
        {
            string answer = await OllamaService.Instance.GenerateCsAnswerAsync(prompt, _cts.Token);
            sw.Stop();

            TxtAiResponse.Text = answer;
            TxtStatus.Text = $"✅ 생성 완료! ({sw.ElapsedMilliseconds}ms) - [Tab]을 누르면 대화창에 바로 입력됩니다.";
            TxtAiResponse.Focus();
            TxtAiResponse.SelectAll();
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "생성이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"⚠️ 생성 실패: {ex.Message}";
        }
        finally
        {
            PnlLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void DoInject(bool pressEnterAfterPaste)
    {
        string text = TxtAiResponse.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Close();
            return;
        }

        Hide();

        // 텍스트 원래 대화창에 주입
        TextInjectionService.FastPasteText(text, _targetHwnd);

        if (pressEnterAfterPaste)
        {
            Thread.Sleep(30);
            Win32Api.keybd_event(Win32Api.VK_RETURN, 0, 0, UIntPtr.Zero);
            Thread.Sleep(5);
            Win32Api.keybd_event(Win32Api.VK_RETURN, 0, Win32Api.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _cts?.Cancel();
            Close();
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = StartGenerationAsync();
            return;
        }

        if (e.Key == Key.Tab)
        {
            // TxtCustomerInput에서 Tab 누를 때 제외하고, Tab 키는 즉시 주입
            if (!TxtCustomerInput.IsFocused || !string.IsNullOrWhiteSpace(TxtAiResponse.Text))
            {
                e.Handled = true;
                DoInject(false);
                return;
            }
        }

        if (e.Key == Key.Enter && (Keyboard.Modifiers == ModifierKeys.Control || TxtAiResponse.IsFocused))
        {
            e.Handled = true;
            DoInject(false);
            return;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnRegenerate_Click(object sender, RoutedEventArgs e)
    {
        _ = StartGenerationAsync();
    }

    private void BtnInject_Click(object sender, RoutedEventArgs e)
    {
        DoInject(false);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
