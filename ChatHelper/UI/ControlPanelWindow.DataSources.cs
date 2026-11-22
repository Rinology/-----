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
        public string DisplayText { get; set; } = "";
        public override string ToString() => DisplayText;
    }

    private void RefreshUserProfilesList()
    {
        if (LstGasUserProfiles != null)
        {
            LstGasUserProfiles.Items.Clear();
            foreach (var p in DataSourceResolver.RemoteProfiles.Where(x => x.IsPersonal))
            {
                LstGasUserProfiles.Items.Add(new UserProfileEntry
                {
                    Name = p.Name,
                    DisplayText = $"[☁️ 서버] {p.Name}  ({p.TemplateTab}) — {p.SpreadsheetUrl}"
                });
            }
        }

        if (LstLocalUserProfiles != null)
        {
            LstLocalUserProfiles.Items.Clear();
            foreach (var p in _configManager.Config.DataSourceProfiles)
            {
                LstLocalUserProfiles.Items.Add(new UserProfileEntry
                {
                    Name = p.Name,
                    DisplayText = $"[💻 로컬] {p.Name}  ({(string.IsNullOrWhiteSpace(p.TemplateTab) ? "채팅템플릿" : p.TemplateTab)}) — {p.SpreadsheetUrl}"
                });
            }
        }
    }

    private void BtnCopyGasEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("taerin@qualisports.kr");
            SetGasProfileBusy(false, "✅ 편집자 이메일(taerin@qualisports.kr)이 클립보드에 복사되었습니다.");
        }
        catch { }
    }

    private void SetGasProfileBusy(bool busy, string message = "", bool isError = false)
    {
        BtnAddGasProfile.IsEnabled = !busy;
        BtnDeleteGasProfile.IsEnabled = !busy;
        PbarGasProfile.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TxtGasProfileStatus.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        TxtGasProfileStatus.Text = message;
        TxtGasProfileStatus.Foreground = isError
            ? System.Windows.Media.Brushes.Crimson
            : (busy ? System.Windows.Media.Brushes.DarkOrange : System.Windows.Media.Brushes.ForestGreen);
    }

    private async void BtnAddGasProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtGasProfileName.Text.Trim();
        string url = TxtGasProfileUrl.Text.Trim();
        string tab = string.IsNullOrWhiteSpace(TxtGasProfileTab.Text) ? "채팅템플릿" : TxtGasProfileTab.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(DataSourceResolver.ExtractId(url)))
        {
            MessageBox.Show("시트 이름과 올바른 구글 시트 URL을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string uid = _configManager.Config.CurrentUserId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            MessageBox.Show("로그인된 사용자 정보가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new DataSourceProfile { Name = name, SpreadsheetUrl = url, TemplateTab = tab };
        SetGasProfileBusy(true, $"⏳ '{name}' 시트 권한 확인 및 서버(GAS)에 등록 중...");
        var (ok, msg) = await DataSourceResolver.AddRemoteUserProfileAsync(uid, profile);
        SetGasProfileBusy(false, ok ? $"✅ '{name}' 서버 등록 완료!" : $"⚠️ {msg}", isError: !ok);
        if (!ok) return;

        TxtGasProfileName.Clear();
        TxtGasProfileUrl.Clear();
        LoadProfiles(name);
        DataSourceResolver.RaiseChanged();
    }

    private async void BtnDeleteGasProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LstGasUserProfiles.SelectedItem is not UserProfileEntry entry)
        {
            MessageBox.Show("삭제할 서버 개인 시트를 목록에서 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"'{entry.Name}' 서버 개인 시트를 삭제할까요?\n(원본 구글 스프레드시트 파일 자체는 삭제되지 않습니다)", "삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        string uid = _configManager.Config.CurrentUserId?.Trim() ?? "";
        SetGasProfileBusy(true, $"⏳ '{entry.Name}' 서버 시트 삭제 중...");
        var (ok, msg) = await DataSourceResolver.DeleteRemoteUserProfileAsync(uid, entry.Name);
        SetGasProfileBusy(false, ok ? $"✅ '{entry.Name}' 삭제 완료." : $"⚠️ {msg}", isError: !ok);
        if (!ok) return;

        if (string.Equals(_configManager.Config.ActiveDataSourceProfile, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            _configManager.Config.ActiveDataSourceProfile = "";
            _configManager.Save();
        }

        LoadProfiles();
        DataSourceResolver.RaiseChanged();
    }

    private void BtnAddLocalProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtLocalProfileName.Text.Trim();
        string url = TxtLocalProfileUrl.Text.Trim();
        string tab = string.IsNullOrWhiteSpace(TxtLocalProfileTab.Text) ? "채팅템플릿" : TxtLocalProfileTab.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(DataSourceResolver.ExtractId(url)))
        {
            MessageBox.Show("시트 이름과 올바른 구글 시트 URL을 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new DataSourceProfile { Name = name, SpreadsheetUrl = url, TemplateTab = tab };
        var list = _configManager.Config.DataSourceProfiles;
        list.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        list.Add(profile);
        _configManager.Save();

        TxtLocalProfileStatus.Text = $"✅ '{name}' 이 PC 로컬 전용 시트로 저장 완료!";
        TxtLocalProfileStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
        TxtLocalProfileStatus.Visibility = Visibility.Visible;

        TxtLocalProfileName.Clear();
        TxtLocalProfileUrl.Clear();
        LoadProfiles(name);
        DataSourceResolver.RaiseChanged();
    }

    private void BtnDeleteLocalProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LstLocalUserProfiles.SelectedItem is not UserProfileEntry entry)
        {
            MessageBox.Show("삭제할 로컬 시트를 목록에서 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"'{entry.Name}' 로컬 시트를 이 PC에서 삭제할까요?", "삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _configManager.Config.DataSourceProfiles.RemoveAll(x =>
            string.Equals(x.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        _configManager.Save();

        TxtLocalProfileStatus.Text = $"✅ '{entry.Name}' 로컬 시트 삭제 완료.";
        TxtLocalProfileStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
        TxtLocalProfileStatus.Visibility = Visibility.Visible;

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
