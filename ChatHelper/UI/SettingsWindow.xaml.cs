using System.Windows;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class SettingsWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly TemplateManager _templateManager;

    public event Action? SettingsSaved;

    public SettingsWindow(ConfigManager configManager, TemplateManager templateManager)
    {
        InitializeComponent();
        _configManager = configManager;
        _templateManager = templateManager;
        LoadCurrentConfig();
    }

    private void LoadCurrentConfig()
    {
        var cfg = _configManager.Config;

        if (cfg.DisplayMode == "HorizontalBar")
            RadioHorizontalBar.IsChecked = true;
        else if (cfg.DisplayMode == "ManualOnly")
            RadioManualOnly.IsChecked = true;
        else
            RadioInlinePill.IsChecked = true;

        TxtTriggerKey.Text = cfg.TriggerKey ?? "\\";
        ChkRequireTriggerForTab.IsChecked = cfg.RequireTriggerForTab;
        ChkEnableNumbers.IsChecked = cfg.EnableNumberSelection;
        TxtRemoteUrl.Text = cfg.RemoteCsvUrl ?? "";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _configManager.Config;

        if (RadioHorizontalBar.IsChecked == true)
            cfg.DisplayMode = "HorizontalBar";
        else if (RadioManualOnly.IsChecked == true)
            cfg.DisplayMode = "ManualOnly";
        else
            cfg.DisplayMode = "InlinePill";

        cfg.TriggerKey = string.IsNullOrEmpty(TxtTriggerKey.Text) ? "\\" : TxtTriggerKey.Text;
        cfg.RequireTriggerForTab = ChkRequireTriggerForTab.IsChecked == true;
        cfg.EnableNumberSelection = ChkEnableNumbers.IsChecked == true;
        cfg.RemoteCsvUrl = TxtRemoteUrl.Text.Trim();

        _configManager.Save();
        SettingsSaved?.Invoke();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtRemoteUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("구글 시트 CSV URL을 먼저 입력해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _configManager.Config.RemoteCsvUrl = url;
        var (success, msg) = await _templateManager.SyncFromRemoteAsync();
        MessageBox.Show(msg, success ? "성공" : "오류", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }
}
