using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class QuickAiWindow : Window
{
    private readonly SuggestionEngine? _suggestionEngine;
    private readonly ConfigManager? _configManager;
    private IntPtr _targetHwnd = IntPtr.Zero;
    private CancellationTokenSource? _cts;
    private string _lastInferredContentKeyword = string.Empty;

    public QuickAiWindow() : this(null, null, null)
    {
    }

    public QuickAiWindow(TemplateManager? templateManager, ConfigManager? configManager = null, GlossaryService? glossaryService = null)
    {
        InitializeComponent();
        _configManager = configManager;
        if (templateManager != null)
        {
            _suggestionEngine = new SuggestionEngine(templateManager, null, glossaryService);
        }
    }

    /// <summary>
    /// 창을 열고 클립보드(캡처 이미지 또는 복사 텍스트)를 자동 분석하여 AI 답변 생성 시작
    /// </summary>
    public async void ShowForTarget(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        Show();
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        Win32Api.ForceForegroundWindow(helper.Handle);
        Activate();

        TxtStatus.Text = "클립보드 확인 중...";
        TxtAiResponse.Text = string.Empty;

        // 클립보드 분석 (캡처 이미지 OCR 또는 복사된 텍스트)
        await LoadClipboardAsync();

        if (_configManager?.Config.AutoGenerateOnAltX == true && !string.IsNullOrWhiteSpace(TxtCustomerInput.Text))
        {
            await StartGenerationAsync();
        }
    }

    private async Task LoadClipboardAsync()
    {
        var clipResult = await ClipboardSmartService.GetCustomerMessageAsync();

        if (clipResult.IsFromImage)
        {
            TxtSourceType.Text = "📸 [인식: 화면 캡처 이미지 (자동 OCR 추출)]";
            TxtSourceType.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        else if (!string.IsNullOrWhiteSpace(clipResult.ExtractedText))
        {
            TxtSourceType.Text = "📋 [인식: 복사된 텍스트]";
            TxtSourceType.Foreground = System.Windows.Media.Brushes.Navy;
        }
        else
        {
            TxtSourceType.Text = "⚠️ [클립보드가 비어 있습니다]";
            TxtSourceType.Foreground = System.Windows.Media.Brushes.DarkRed;
        }

        if (!string.IsNullOrWhiteSpace(clipResult.ExtractedText))
        {
            TxtCustomerInput.Text = clipResult.ExtractedText;
            TxtStatus.Text = "고객 문의 내용을 확인/수정한 뒤 [✨ 답변 생성 (Enter)]을 누르세요.";
        }
        else
        {
            TxtCustomerInput.Text = string.Empty;
            TxtStatus.Text = "고객 문의를 직접 입력하거나 캡처(Win+Shift+S) 후 [📋 클립보드 다시 불러오기]를 누르세요.";
        }

        TxtCustomerInput.Focus();
        TxtCustomerInput.SelectAll();
    }

    private async void BtnReloadClipboard_Click(object sender, RoutedEventArgs e)
    {
        await LoadClipboardAsync();
    }

    private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        await StartGenerationAsync();
    }

    private void TxtCustomerInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            _ = StartGenerationAsync();
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
        TxtStatus.Text = "⚡ 로컬 AI(Qwen 2.5 3B)가 친절한 답변을 생성 중입니다...";

        var sw = Stopwatch.StartNew();

        try
        {
            string answer;
            List<TemplateItem> pills = new();

            if (_suggestionEngine != null)
            {
                // 1. Ollama가 스프레드시트 목록 + 로컬 퍼지 검색을 함께 분석해 알약/답변 제안
                string inferredKw;
                (answer, pills, inferredKw) = await _suggestionEngine.GenerateAsync(
                    prompt, useAiFallback: true, extractKeywordWithAi: true, _cts.Token);

                _lastInferredContentKeyword = inferredKw;

                // 제어판 추천 바 형태 반영: InlinePill이면 1개, HorizontalBar이면 설정 개수만큼 알약 표시
                var mode = _configManager?.Config.DisplayMode ?? "HorizontalBar";
                if (mode == "InlinePill" && pills.Count > 1)
                {
                    pills = pills.GetRange(0, 1);
                }
                else if (_configManager != null)
                {
                    int maxPills = Math.Clamp(_configManager.Config.MaxCandidatesCount, 3, 10);
                    if (pills.Count > maxPills)
                        pills = pills.GetRange(0, maxPills);
                }

                RenderPills(pills);
            }
            else
            {
                answer = await OllamaService.Instance.GenerateCsAnswerAsync(prompt, _cts.Token);
            }

            sw.Stop();

            TxtAiResponse.Text = answer;
            TxtStatus.Text = $"✅ 생성 완료! ({sw.ElapsedMilliseconds}ms) - [Tab] 대화창 입력  |  [💾 시트 저장]으로 키워드 등록";
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

    private void RenderPills(List<TemplateItem> pills)
    {
        PnlSuggestedPills.Children.Clear();
        if (pills == null || pills.Count == 0)
        {
            PnlSuggestedPills.Visibility = Visibility.Collapsed;
            return;
        }

        PnlSuggestedPills.Visibility = Visibility.Visible;
        foreach (var item in pills)
        {
            string label = !string.IsNullOrWhiteSpace(item.Keyword)
                ? $"💊 [{item.Keyword}] {item.Title}"
                : $"💊 {item.Title}";

            var pillBtn = new Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 5, 4),
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8EEF7")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000080")),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.Black
            };
            pillBtn.Click += (s, e) =>
            {
                TxtAiResponse.Text = item.Content;
                _lastInferredContentKeyword = !string.IsNullOrWhiteSpace(item.ContentKeyword)
                    ? item.ContentKeyword
                    : item.Keyword;
                TxtStatus.Text = $"💊 시트 템플릿 '{item.Title}' 선택됨 - [Tab]으로 대화창에 바로 입력할 수 있습니다.";
            };
            PnlSuggestedPills.Children.Add(pillBtn);
        }
    }

    private async void BtnSaveToSheet_Click(object sender, RoutedEventArgs e)
    {
        string content = TxtAiResponse.Text.Trim();
        string query = TxtCustomerInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            MessageBox.Show("저장할 답변 내용이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // BtnSaveToSheet.IsEnabled = false;
        TxtStatus.Text = "🤖 Ollama가 시트 저장용 항목(단축키/키워드/제목/분류)을 정리 중입니다...";

        try
        {
            TemplateItem structured;
            if (_suggestionEngine != null)
            {
                structured = await _suggestionEngine.PrepareStructuredTemplateAsync(query, content);
            }
            else
            {
                structured = await OllamaService.Instance.StructureTemplateForSheetAsync(query, content, Array.Empty<string>());
            }

            if (!string.IsNullOrWhiteSpace(_lastInferredContentKeyword) && string.IsNullOrWhiteSpace(structured.ContentKeyword))
            {
                structured.ContentKeyword = _lastInferredContentKeyword;
            }

            // 사용자 확인 및 미세 수정 후 업로드
            var confirmed = ShowStructuredUploadConfirmDialog(structured);
            if (confirmed == null)
            {
                TxtStatus.Text = "시트 저장이 취소되었습니다.";
                return;
            }

            TxtStatus.Text = "💾 구글 스프레드시트에 업로드 중...";
            bool success;
            string msg;
            if (_suggestionEngine != null)
            {
                (success, msg) = await _suggestionEngine.CommitAsync(confirmed);
            }
            else
            {
                var reg = new KeywordRegistrationService();
                (success, msg) = await reg.RegisterAsync(confirmed);
            }

            TxtStatus.Text = success ? $"✅ 시트 저장 완료! (단축키: {confirmed.Keyword}, 키워드: {confirmed.ContentKeyword})" : $"⚠️ 저장 실패: {msg}";
            MessageBox.Show(msg, "구글 시트 저장 결과", MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            // BtnSaveToSheet.IsEnabled = true;
        }
    }

    private TemplateItem? ShowStructuredUploadConfirmDialog(TemplateItem draft)
    {
        TemplateItem? result = null;

        var dlg = new Window
        {
            Title = "AI 정리 결과 확인 및 구글 시트 업로드",
            Width = 480,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")),
            WindowStyle = WindowStyle.ToolWindow,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "🤖 Ollama가 정리한 시트 저장 항목입니다. 필요시 수정 후 업로드하세요.", Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.Bold });

        panel.Children.Add(new TextBlock { Text = "단축어 (keyword - 짧은 영문/숫자):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtKw = new TextBox { Text = draft.Keyword, Height = 28, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 2, 4, 2) };
        panel.Children.Add(txtKw);

        panel.Children.Add(new TextBlock { Text = "본문 키워드 (contentKeyword - 검색용):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtCkw = new TextBox { Text = draft.ContentKeyword, Height = 28, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 2, 4, 2) };
        panel.Children.Add(txtCkw);

        panel.Children.Add(new TextBlock { Text = "제목 (title):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtTitle = new TextBox { Text = draft.Title, Height = 28, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 2, 4, 2) };
        panel.Children.Add(txtTitle);

        panel.Children.Add(new TextBlock { Text = "카테고리 (category):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtCat = new TextBox { Text = draft.Category, Height = 28, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 2, 4, 2) };
        panel.Children.Add(txtCat);

        panel.Children.Add(new TextBlock { Text = "답변 본문 (content):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        var txtBody = new TextBox { Text = draft.Content, Height = 85, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(4) };
        panel.Children.Add(txtBody);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button { Content = "💾 확인 및 시트 업로드", Width = 155, Height = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) };
        var btnCancel = new Button { Content = "취소", Width = 75, Height = 30 };

        btnOk.Click += (s, ev) =>
        {
            result = new TemplateItem
            {
                Keyword = txtKw.Text.Trim(),
                ContentKeyword = txtCkw.Text.Trim(),
                Title = txtTitle.Text.Trim(),
                Category = string.IsNullOrWhiteSpace(txtCat.Text) ? "일반" : txtCat.Text.Trim(),
                Content = txtBody.Text.Trim()
            };
            dlg.DialogResult = true;
            dlg.Close();
        };
        btnCancel.Click += (s, ev) => dlg.Close();

        btnRow.Children.Add(btnOk);
        btnRow.Children.Add(btnCancel);
        panel.Children.Add(btnRow);

        dlg.Content = panel;
        dlg.ShowDialog();
        return result;
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
