using System.Windows;
using System.Windows.Controls;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

// 데이터 소스(상황별 시트) 프로필 선택 & 고급 직접 연결 탭
public partial class ControlPanelWindow
{
    private const string NewProfileLabel = "(새 프로필 작성)";
    private bool _profileLoading;

    private void LoadProfiles(string? selectName = null)
    {
        _profileLoading = true;
        var cfg = _configManager.Config;
        cfg.DataSourceProfiles ??= new List<DataSourceProfile>();

        // 1) 일반 유저용 업무 프로필 선택 드롭다운 (GAS 중앙 목록 + .env + 사용자 프로필)
        CmbActivePreset.Items.Clear();
        var all = DataSourceResolver.GetAllProfiles();
        if (DataSourceResolver.RemoteProfiles.Count == 0)
            CmbActivePreset.Items.Add(DataSourceResolver.DefaultProfileName);
        foreach (var (p, _) in all)
            CmbActivePreset.Items.Add(p.Name);

        string active = !string.IsNullOrWhiteSpace(cfg.ActiveDataSourceProfile)
            ? cfg.ActiveDataSourceProfile
            : DataSourceResolver.ActiveName;
        int activeIdx = CmbActivePreset.Items.IndexOf(active);
        CmbActivePreset.SelectedIndex = activeIdx >= 0 ? activeIdx : 0;

        // 2) 고급 설정용 사용자 직접 추가 목록
        CmbProfiles.Items.Clear();
        foreach (var p in cfg.DataSourceProfiles) CmbProfiles.Items.Add(p.Name);
        CmbProfiles.Items.Add(NewProfileLabel);

        string target = selectName ?? cfg.ActiveDataSourceProfile ?? "";
        int idx = cfg.DataSourceProfiles.FindIndex(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));
        CmbProfiles.SelectedIndex = idx >= 0 ? idx : CmbProfiles.Items.Count - 1;
        _profileLoading = false;

        FillProfileFields(idx >= 0 ? cfg.DataSourceProfiles[idx] : null);
        LblActiveProfile.Text = $"현재 적용 중: {DataSourceResolver.ActiveName}";
    }

    private void BtnPresetSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (CmbActivePreset.SelectedItem is not string selected) return;
        bool isDefault = string.Equals(selected, DataSourceResolver.DefaultProfileName, StringComparison.OrdinalIgnoreCase);

        _configManager.Config.ActiveDataSourceProfile = isDefault ? "" : selected;
        _configManager.Save();
        LoadProfiles(isDefault ? "" : selected);
        DataSourceResolver.RaiseChanged();
        LblStatusBar.Text = $"'{DataSourceResolver.ActiveName}' 적용 - 동기화 중...";
    }

    private void FillProfileFields(DataSourceProfile? p)
    {
        TxtProfName.Text = p?.Name ?? "";
        TxtProfSpreadsheetUrl.Text = p?.SpreadsheetUrl ?? "";
        TxtProfTemplateTab.Text = p?.TemplateTab ?? "";
        TxtProfGlossaryTab.Text = p?.GlossaryTab ?? "";
        TxtProfRulesTab.Text = p?.AiRulesTab ?? "";
    }

    private void CmbProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_profileLoading || CmbProfiles.SelectedItem is not string name) return;
        var p = _configManager.Config.DataSourceProfiles
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        FillProfileFields(p);
    }

    private void BtnProfileNew_Click(object sender, RoutedEventArgs e)
    {
        _profileLoading = true;
        CmbProfiles.SelectedIndex = CmbProfiles.Items.Count - 1;
        _profileLoading = false;
        FillProfileFields(null);
        TxtProfName.Focus();
    }

    private void BtnProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CmbProfiles.SelectedItem is not string name || name == NewProfileLabel) return;
        if (MessageBox.Show($"직접 추가한 프로필 '{name}'을(를) 삭제할까요?\n(스프레드시트 자체는 삭제되지 않습니다)", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var cfg = _configManager.Config;
        bool wasActive = string.Equals(cfg.ActiveDataSourceProfile, name, StringComparison.OrdinalIgnoreCase);
        cfg.DataSourceProfiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (wasActive) cfg.ActiveDataSourceProfile = "";
        _configManager.Save();
        LoadProfiles("");
        if (wasActive) DataSourceResolver.RaiseChanged();
    }

    private void BtnProfileApply_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtProfName.Text.Trim();
        if (string.IsNullOrEmpty(name) || name == NewProfileLabel || name == DataSourceResolver.DefaultProfileName)
        {
            MessageBox.Show("사용 가능한 프로필 이름을 입력해 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string url = TxtProfSpreadsheetUrl.Text.Trim();
        if (string.IsNullOrEmpty(DataSourceResolver.ExtractId(url)))
        {
            MessageBox.Show("스프레드시트 주소가 올바르지 않습니다.\n브라우저 주소창의 https://docs.google.com/spreadsheets/d/.../edit 형태를 붙여넣어 주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cfg = _configManager.Config;
        string? prev = CmbProfiles.SelectedItem as string;
        var existing = cfg.DataSourceProfiles.FirstOrDefault(p => string.Equals(p.Name, prev, StringComparison.OrdinalIgnoreCase))
                       ?? cfg.DataSourceProfiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new DataSourceProfile();
            cfg.DataSourceProfiles.Add(existing);
        }

        existing.Name = name;
        existing.SpreadsheetUrl = url;
        existing.TemplateTab = TxtProfTemplateTab.Text.Trim();
        existing.GlossaryTab = TxtProfGlossaryTab.Text.Trim();
        existing.AiRulesTab = TxtProfRulesTab.Text.Trim();

        cfg.ActiveDataSourceProfile = name;
        _configManager.Save();
        LoadProfiles(name);
        DataSourceResolver.RaiseChanged();
        LblStatusBar.Text = $"데이터 소스 '{name}' 적용 - 동기화 중...";
    }

    private void BtnProfileDefault_Click(object sender, RoutedEventArgs e)
    {
        _configManager.Config.ActiveDataSourceProfile = "";
        _configManager.Save();
        LoadProfiles("");
        DataSourceResolver.RaiseChanged();
        LblStatusBar.Text = "기본(.env) 데이터 소스로 복귀 - 동기화 중...";
    }
}
