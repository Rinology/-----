using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class GlossaryWindow : Window
{
    private readonly GlossaryService _glossaryService;

    public GlossaryWindow(GlossaryService glossaryService)
    {
        InitializeComponent();
        _glossaryService = glossaryService;
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        string q = TxtSearch.Text.Trim().ToLowerInvariant();
        var items = string.IsNullOrWhiteSpace(q)
            ? _glossaryService.Terms.ToList()
            : _glossaryService.Terms.Where(t =>
                t.Term.ToLowerInvariant().Contains(q) ||
                t.Aliases.ToLowerInvariant().Contains(q) ||
                t.Description.ToLowerInvariant().Contains(q) ||
                t.Category.ToLowerInvariant().Contains(q)).ToList();

        GridGlossary.ItemsSource = items;
        TxtStatus.Text = $"총 {_glossaryService.Terms.Count}개 용어 로드됨 (표시: {items.Count}개)";
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshGrid();
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        BtnSync.IsEnabled = false;
        TxtStatus.Text = "🔄 구글 2번 시트(용어집) 동기화 중...";
        try
        {
            var (_, msg) = await _glossaryService.SyncFromRemoteAsync();
            RefreshGrid();
            TxtStatus.Text = msg;
        }
        finally
        {
            BtnSync.IsEnabled = true;
        }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string term = TxtTerm.Text.Trim();
        string desc = TxtDescription.Text.Trim();
        if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(desc))
        {
            MessageBox.Show("용어(term)와 공식 설명(description)을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnAdd.IsEnabled = false;
        TxtStatus.Text = "💾 구글 시트에 용어 업로드 중...";
        try
        {
            var item = new GlossaryItem
            {
                Term = term,
                Aliases = TxtAliases.Text.Trim(),
                Description = desc,
                Guide = TxtGuide.Text.Trim(),
                Category = string.IsNullOrWhiteSpace(TxtCategory.Text) ? "일반" : TxtCategory.Text.Trim()
            };

            var (success, msg) = await _glossaryService.UploadTermAsync(item);
            RefreshGrid();
            TxtStatus.Text = msg;
            if (success)
            {
                TxtTerm.Clear();
                TxtAliases.Clear();
                TxtDescription.Clear();
                TxtGuide.Clear();
            }
            MessageBox.Show(msg, "용어집 추가 결과", MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            BtnAdd.IsEnabled = true;
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GridGlossary.SelectedItem is not GlossaryItem selected)
        {
            MessageBox.Show("삭제할 용어를 목록에서 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"'{selected.Term}' 용어를 구글 시트에서 삭제하시겠습니까?", "삭제 확인",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        BtnDelete.IsEnabled = false;
        TxtStatus.Text = "🗑 구글 시트에서 삭제 중...";
        try
        {
            var (success, msg) = await _glossaryService.DeleteTermAsync(selected.Term);
            RefreshGrid();
            TxtStatus.Text = msg;
            MessageBox.Show(msg, "용어집 삭제 결과", MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            BtnDelete.IsEnabled = true;
        }
    }
}
