using System.Collections.ObjectModel;
using System.Windows;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

// 데이터 소스(업무 프로필) 선택 및 동기화 로딩 안내
public partial class ControlPanelWindow
{
    private void LoadProfiles(string? selectName = null)
    {
        var cfg = _configManager.Config;

        CmbActivePreset.Items.Clear();
        var all = DataSourceResolver.GetAllProfiles();
        if (all.Count == 0)
            CmbActivePreset.Items.Add(DataSourceResolver.DefaultProfileName);
        foreach (var (p, _) in all)
            CmbActivePreset.Items.Add(p.Name);

        string active = !string.IsNullOrWhiteSpace(selectName)
            ? selectName
            : (!string.IsNullOrWhiteSpace(cfg.ActiveDataSourceProfile)
                ? cfg.ActiveDataSourceProfile
                : DataSourceResolver.ActiveName);
        int activeIdx = CmbActivePreset.Items.IndexOf(active);
        CmbActivePreset.SelectedIndex = activeIdx >= 0 ? activeIdx : 0;

        LblActiveProfile.Text = $"현재 적용 중: {DataSourceResolver.ActiveName}";
    }

    private void SetProfileSyncBusy(bool busy, string message = "", bool isError = false)
    {
        BtnPresetSwitch.IsEnabled = !busy;
        BtnPresetRefresh.IsEnabled = !busy;
        CmbActivePreset.IsEnabled = !busy;

        PbarProfileSync.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TxtProfileSyncStatus.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        TxtProfileSyncStatus.Text = message;
        TxtProfileSyncStatus.Foreground = isError
            ? System.Windows.Media.Brushes.Crimson
            : (busy ? System.Windows.Media.Brushes.DarkOrange : System.Windows.Media.Brushes.ForestGreen);
    }

    private async void BtnPresetSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (CmbActivePreset.SelectedItem is not string selected) return;
        bool isDefault = string.Equals(selected, DataSourceResolver.DefaultProfileName, StringComparison.OrdinalIgnoreCase);

        _configManager.Config.ActiveDataSourceProfile = isDefault ? "" : selected;
        _configManager.Save();
        LoadProfiles(isDefault ? "" : selected);

        SetProfileSyncBusy(true, $"⏳ '{DataSourceResolver.ActiveName}' 시트 데이터를 불러오는 중...");
        LblStatusBar.Text = $"'{DataSourceResolver.ActiveName}' 동기화 중...";

        try
        {
            var (success, msg) = await _templateManager.SyncFromRemoteAsync();
            _templatesObs = new ObservableCollection<TemplateItem>(_templateManager.Templates);
            DgTemplates.ItemsSource = _templatesObs;

            // MainWindow 상태창 · 용어집 · 톤앤매너도 즉시 함께 갱신
            DataSourceResolver.RaiseChanged();

            if (success)
            {
                SetProfileSyncBusy(false, $"✅ '{DataSourceResolver.ActiveName}' 적용 완료! ({_templateManager.Templates.Count}개 템플릿)");
                LblStatusBar.Text = $"'{DataSourceResolver.ActiveName}' 적용 완료.";
            }
            else
            {
                SetProfileSyncBusy(false, $"⚠️ 동기화 오류: {msg}", isError: true);
                LblStatusBar.Text = "동기화 실패.";
            }
        }
        catch (Exception ex)
        {
            SetProfileSyncBusy(false, $"⚠️ 동기화 오류: {ex.Message}", isError: true);
        }
    }

    private async void BtnPresetRefresh_Click(object sender, RoutedEventArgs e)
    {
        SetProfileSyncBusy(true, "⏳ 서버에서 최신 업무 프로필 목록을 확인하는 중...");
        try
        {
            bool ok = await DataSourceResolver.RefreshRemoteProfilesAsync();
            LoadProfiles();
            SetProfileSyncBusy(false,
                ok ? $"✅ 프로필 목록 갱신 완료 ({CmbActivePreset.Items.Count}개)" : "⚠️ 목록을 불러오지 못했습니다. (네트워크/GAS 확인)",
                isError: !ok);
        }
        catch (Exception ex)
        {
            SetProfileSyncBusy(false, $"⚠️ 오류: {ex.Message}", isError: true);
        }
    }
}
