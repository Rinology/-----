using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ChatHelper.Constants;
using ChatHelper.Models;
using ChatHelper.Services;

namespace ChatHelper.UI;

/// <summary>
/// Alt+X 전용 특수문자·이모티콘 빠른 입력 및 사용자 관리 창.
/// Alt 없이 숫자 1~9 또는 방향키+Enter로 즉시 입력하므로 지포스 등 외부 단축키와 충돌하지 않음.
/// </summary>
public partial class SymbolPickerWindow : Window
{
    private static readonly string[] DefaultSymbols =
        { "→", "➔", "▶", "■", "□", "●", "★", "☆", "※", "✔", "✅", "📌", "📦", "📞", "😊", "🙏" };

    private readonly ConfigManager _configManager;
    private readonly List<Button> _symbolButtons = new();
    private IntPtr _targetHwnd = IntPtr.Zero;
    private int _selectedIndex = 0;

    public event Action<string, IntPtr>? SymbolSelected;

    public SymbolPickerWindow(ConfigManager configManager)
    {
        _configManager = configManager;
        InitializeComponent();
        EnsureSymbolsInitialized();
        RebuildGrid();
    }

    private List<string> Symbols
    {
        get
        {
            EnsureSymbolsInitialized();
            return _configManager.Config.CustomSymbols;
        }
    }

    private void EnsureSymbolsInitialized()
    {
        if (_configManager.Config.CustomSymbols == null || _configManager.Config.CustomSymbols.Count == 0)
        {
            _configManager.Config.CustomSymbols = new List<string>(DefaultSymbols);
        }
    }

    public void ShowForTarget(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        _selectedIndex = 0;
        TxtNewSymbol.Clear();
        RebuildGrid();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        Opacity = Math.Clamp(_configManager.Config.WindowOpacity, AppConstants.MinWindowOpacity, AppConstants.MaxWindowOpacity);

        Show();
        WindowState = WindowState.Normal;

        var helper = new WindowInteropHelper(this);
        Win32Api.ForceForegroundWindow(helper.Handle);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void RebuildGrid()
    {
        SymbolWrapPanel.Children.Clear();
        _symbolButtons.Clear();

        var list = Symbols;
        if (_selectedIndex >= list.Count) _selectedIndex = Math.Max(0, list.Count - 1);

        for (int i = 0; i < list.Count; i++)
        {
            string sym = list[i];
            int capturedIdx = i;

            var grid = new Grid { Width = 44, Height = 38 };
            if (i < 9)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(3, 1, 0, 0)
                });
            }
            grid.Children.Add(new TextBlock
            {
                Text = sym,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new Button
            {
                Content = grid,
                Width = 48,
                Height = 42,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Tahoma, Gulim"),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                Focusable = false,
                ToolTip = i < 9 ? $"'{sym}' (숫자키 {i + 1} 또는 클릭)" : $"'{sym}' (클릭 또는 방향키+Enter)"
            };

            btn.Click += (s, e) => InsertSymbol(sym);
            btn.MouseRightButtonUp += (s, e) =>
            {
                _selectedIndex = capturedIdx;
                UpdateSelectionVisual();
            };

            SymbolWrapPanel.Children.Add(btn);
            _symbolButtons.Add(btn);
        }

        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        for (int i = 0; i < _symbolButtons.Count; i++)
        {
            bool sel = i == _selectedIndex;
            _symbolButtons[i].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#000080" : "#FFFFFF"));
            _symbolButtons[i].Foreground = sel ? Brushes.White : Brushes.Black;
            _symbolButtons[i].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sel ? "#000000" : "#808080"));
        }
    }

    private void InsertSymbol(string sym)
    {
        Hide();
        SymbolSelected?.Invoke(sym, _targetHwnd);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;

        if (key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        // 하단 새 기호 입력칸에 포커스가 있을 때는 일반 타이핑 허용
        if (TxtNewSymbol.IsKeyboardFocusWithin)
            return;

        // 숫자 1~9 (Alt/Ctrl 없이 단독 입력 -> 지포스 단축키 충돌 없음)
        int numIdx = -1;
        if (key >= Key.D1 && key <= Key.D9) numIdx = key - Key.D1;
        else if (key >= Key.NumPad1 && key <= Key.NumPad9) numIdx = key - Key.NumPad1;

        if (numIdx >= 0 && numIdx < Symbols.Count)
        {
            InsertSymbol(Symbols[numIdx]);
            e.Handled = true;
            return;
        }

        if (Symbols.Count == 0) return;

        const int cols = 8;
        if (key == Key.Right)
        {
            _selectedIndex = (_selectedIndex + 1) % Symbols.Count;
            UpdateSelectionVisual();
            e.Handled = true;
        }
        else if (key == Key.Left)
        {
            _selectedIndex = (_selectedIndex - 1 + Symbols.Count) % Symbols.Count;
            UpdateSelectionVisual();
            e.Handled = true;
        }
        else if (key == Key.Down)
        {
            _selectedIndex = (_selectedIndex + cols) % Symbols.Count;
            UpdateSelectionVisual();
            e.Handled = true;
        }
        else if (key == Key.Up)
        {
            _selectedIndex = (_selectedIndex - cols + Symbols.Count) % Symbols.Count;
            UpdateSelectionVisual();
            e.Handled = true;
        }
        else if (key == Key.Enter)
        {
            InsertSymbol(Symbols[_selectedIndex]);
            e.Handled = true;
        }
    }

    private void TxtNewSymbol_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnAddSymbol_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnAddSymbol_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtNewSymbol.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (!Symbols.Contains(text))
        {
            Symbols.Add(text);
            _configManager.Save();
            _selectedIndex = Symbols.Count - 1;
            RebuildGrid();
        }
        TxtNewSymbol.Clear();
        Focus();
        Keyboard.Focus(this);
    }

    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= Symbols.Count) return;
        Symbols.RemoveAt(_selectedIndex);
        if (Symbols.Count == 0) Symbols.AddRange(DefaultSymbols);
        _configManager.Save();
        RebuildGrid();
        Focus();
        Keyboard.Focus(this);
    }

    private void BtnResetSymbols_Click(object sender, RoutedEventArgs e)
    {
        _configManager.Config.CustomSymbols = new List<string>(DefaultSymbols);
        _configManager.Save();
        _selectedIndex = 0;
        RebuildGrid();
        Focus();
        Keyboard.Focus(this);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();
}
