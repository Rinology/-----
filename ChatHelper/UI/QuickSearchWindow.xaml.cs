using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

public partial class QuickSearchWindow : Window
{
    private readonly TemplateManager _templateManager;
    private IntPtr _targetHwnd = IntPtr.Zero;

    public event Action<TemplateItem, IntPtr>? ItemSelected;

    public QuickSearchWindow(TemplateManager templateManager)
    {
        InitializeComponent();
        _templateManager = templateManager;
    }

    public void ShowAtCursor(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;

        // 커서 위치 계산
        Win32Api.GetCursorPos(out var pt);
        Left = Math.Max(10, Math.Min(pt.X, SystemParameters.PrimaryScreenWidth - Width - 10));
        Top = Math.Max(10, Math.Min(pt.Y + 20, SystemParameters.PrimaryScreenHeight - Height - 10));

        TxtSearch.Clear();
        UpdateList("");

        Show();
        Activate();
        TxtSearch.Focus();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateList(TxtSearch.Text);
    }

    private void UpdateList(string query)
    {
        var items = _templateManager.Search(query);
        LstTemplates.ItemsSource = items;
        if (items.Count > 0)
        {
            LstTemplates.SelectedIndex = 0;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (LstTemplates.SelectedIndex < LstTemplates.Items.Count - 1)
            {
                LstTemplates.SelectedIndex++;
                LstTemplates.ScrollIntoView(LstTemplates.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (LstTemplates.SelectedIndex > 0)
            {
                LstTemplates.SelectedIndex--;
                LstTemplates.ScrollIntoView(LstTemplates.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void LstTemplates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (LstTemplates.SelectedItem is TemplateItem item)
        {
            Hide();
            ItemSelected?.Invoke(item, _targetHwnd);
        }
    }
}
