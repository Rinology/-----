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
        RefreshUserProfilesList();
    }

    private sealed class UserProfileEntry
    {
        public string Name { get; set; } = "";
        public string StorageType { get; set; } = ""; // "GAS" or "LOCAL"
        public string DisplayText { get; set; } = "";
        public override string ToString() => DisplayText;
    }

    private void RefreshUserProfilesList()
    {
        if (LstUserProfiles == null) return;
        LstUserProfiles.Items.Clear();

        foreach (var p in DataSourceResolver.RemoteProfiles.Where(x => x.IsPersonal))
        {
            LstUserProfiles.Items.Add(new UserProfileEntry
            {
                Name = p.Name,
                StorageType = "GAS",
                DisplayText = $"[☁️ GAS 개인] {p.Name}  ({p.TemplateTab}) — {p.SpreadsheetUrl}"
            });
        }

        foreach (var p in _configManager.Config.DataSourceProfiles)
        {
            LstUserProfiles.Items.Add(new UserProfileEntry
            {
                Name = p.Name,
                StorageType = "LOCAL",
                DisplayText = $"[💻 로컬 PC] {p.Name}  ({(string.IsNullOrWhiteSpace(p.TemplateTab) ? "채팅템플릿" : p.TemplateTab)}) — {p.SpreadsheetUrl}"
            });
        }
    }

    private async void BtnAddUserProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtNewProfileName.Text.Trim();
        string url = TxtNewProfileUrl.Text.Trim();
        string tab = string.IsNullOrWhiteSpace(TxtNewProfileTab.Text) ? "채팅템플릿" : TxtNewProfileTab.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(DataSourceResolver.ExtractId(url)))
        {
            MessageBox.Show("시트 이름과 올바른 구글 시트 URL을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new DataSourceProfile
        {
            Name = name,
            SpreadsheetUrl = url,
            TemplateTab = tab
        };

        if (RadioProfileGas.IsChecked == true)
        {
            string uid = _configManager.Config.CurrentUserId?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(uid))
            {
                MessageBox.Show("로그인된 사용자 정보가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetProfileSyncBusy(true, $"⏳ '{name}' 시트 권한 확인 및 GAS에 등록 중...");
            var (ok, msg) = await DataSourceResolver.AddRemoteUserProfileAsync(uid, profile);
            SetProfileSyncBusy(false, ok ? $"✅ '{name}' GAS 시트 등록 완료!" : $"⚠️ {msg}", isError: !ok);
            if (!ok) return;
        }
        else
        {
            var list = _configManager.Config.DataSourceProfiles;
            list.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            list.Add(profile);
            _configManager.Save();
            SetProfileSyncBusy(false, $"✅ '{name}' 로컬 PC 전용 시트로 저장 완료!");
        }

        TxtNewProfileName.Clear();
        TxtNewProfileUrl.Clear();
        LoadProfiles(name);
        DataSourceResolver.RaiseChanged();
    }

    private async void BtnDeleteUserProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LstUserProfiles.SelectedItem is not UserProfileEntry entry)
        {
            MessageBox.Show("삭제할 내 개인 시트를 목록에서 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"'{entry.Name}' 개인 시트를 삭제할까요?\n(원본 구글 스프레드시트 파일 자체는 삭제되지 않습니다)", "삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (entry.StorageType == "GAS")
        {
            string uid = _configManager.Config.CurrentUserId?.Trim() ?? "";
            SetProfileSyncBusy(true, $"⏳ '{entry.Name}' GAS 시트 삭제 중...");
            var (ok, msg) = await DataSourceResolver.DeleteRemoteUserProfileAsync(uid, entry.Name);
            SetProfileSyncBusy(false, ok ? $"✅ '{entry.Name}' 삭제 완료." : $"⚠️ {msg}", isError: !ok);
            if (!ok) return;
        }
        else
        {
            _configManager.Config.DataSourceProfiles.RemoveAll(x =>
                string.Equals(x.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            _configManager.Save();
            SetProfileSyncBusy(false, $"✅ '{entry.Name}' 로컬 PC 시트 삭제 완료.");
        }

        if (string.Equals(_configManager.Config.ActiveDataSourceProfile, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            _configManager.Config.ActiveDataSourceProfile = "";
            _configManager.Save();
        }

        LoadProfiles();
        DataSourceResolver.RaiseChanged();
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
