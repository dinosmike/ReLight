using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReLight.Controls;
using ReLight.Core;
using ReLight.Core.Effects;

namespace ReLight;

/// <summary>
/// Programs (mostly games) that replace the chosen mode while they run, plus automatic
/// detection of Unity and Unreal Engine games and of video players in front.
/// Every change applies at once; Save keeps it, Cancel (or closing) puts back what was there.
/// </summary>
public partial class ExclusionsWindow : Window
{
    static readonly ExclusionAction[] Actions = Enum.GetValues<ExclusionAction>();

    readonly AppSettings _settings;
    readonly List<ExclusionRule> _rules;
    readonly ExclusionRule _unity, _unreal, _players; // the automatic options use the same editor as the rules
    readonly Snapshot _before;
    bool _saved;

    /// <summary>Called after every change, so the main window looks for the programs again at once.</summary>
    public event Action? Applied;

    /// <summary>What the exclusion settings were when the window opened, for Cancel.</summary>
    sealed record Snapshot(List<ExclusionRule> Rules, bool AutoUnity, ExclusionRule Unity, bool AutoUnreal, ExclusionRule Unreal,
        bool AutoPlayers, ExclusionRule Players);

    public ExclusionsWindow(AppSettings settings)
    {
        _settings = settings;
        _rules = settings.Exclusions.Select(Copy).ToList();
        _unity = new ExclusionRule { Action = settings.UnityAction, Color = settings.UnityColor, Effect = settings.UnityEffect };
        _unreal = new ExclusionRule { Action = settings.UnrealAction, Color = settings.UnrealColor, Effect = settings.UnrealEffect };
        _players = new ExclusionRule { Action = settings.PlayersAction, Color = settings.PlayersColor, Effect = settings.PlayersEffect };
        _before = new Snapshot(_rules.Select(Copy).ToList(), settings.AutoUnity, Copy(_unity), settings.AutoUnreal, Copy(_unreal),
            settings.AutoPlayers, Copy(_players));
        InitializeComponent();

        UnityEditor.Content = ActionEditor(_unity, Apply);
        UnrealEditor.Content = ActionEditor(_unreal, Apply);
        PlayersEditor.Content = ActionEditor(_players, Apply);
        UnityBox.IsChecked = settings.AutoUnity;
        UnrealBox.IsChecked = settings.AutoUnreal;
        PlayersBox.IsChecked = settings.AutoPlayers;
        ShowRules();
        Closing += (_, _) => { if (!_saved) Restore(); };
    }

    /// <summary>Put the window's state into the settings right away.</summary>
    void Apply()
    {
        _settings.Exclusions = _rules.Select(Copy).ToList();
        _settings.AutoUnity = UnityBox.IsChecked == true;
        _settings.AutoUnreal = UnrealBox.IsChecked == true;
        _settings.AutoPlayers = PlayersBox.IsChecked == true;
        _settings.UnityAction = _unity.Action;
        _settings.UnityColor = _unity.Color;
        _settings.UnityEffect = _unity.Effect;
        _settings.UnrealAction = _unreal.Action;
        _settings.UnrealColor = _unreal.Color;
        _settings.UnrealEffect = _unreal.Effect;
        _settings.PlayersAction = _players.Action;
        _settings.PlayersColor = _players.Color;
        _settings.PlayersEffect = _players.Effect;
        Applied?.Invoke();
    }

    void Restore()
    {
        var b = _before;
        _settings.Exclusions = b.Rules.Select(Copy).ToList();
        _settings.AutoUnity = b.AutoUnity;
        _settings.AutoUnreal = b.AutoUnreal;
        _settings.AutoPlayers = b.AutoPlayers;
        _settings.UnityAction = b.Unity.Action;
        _settings.UnityColor = b.Unity.Color;
        _settings.UnityEffect = b.Unity.Effect;
        _settings.UnrealAction = b.Unreal.Action;
        _settings.UnrealColor = b.Unreal.Color;
        _settings.UnrealEffect = b.Unreal.Effect;
        _settings.PlayersAction = b.Players.Action;
        _settings.PlayersColor = b.Players.Color;
        _settings.PlayersEffect = b.Players.Effect;
        Applied?.Invoke();
    }

    void Engine_Click(object sender, RoutedEventArgs e) => Apply();

    static ExclusionRule Copy(ExclusionRule r) =>
        new() { Exe = r.Exe, Path = r.Path, Action = r.Action, Color = r.Color, Effect = r.Effect };

    /// <summary>
    /// What to do instead of screen capture: the action, and next to it the color (single color)
    /// or the effect (rainbow) for this program only.
    /// </summary>
    static StackPanel ActionEditor(ExclusionRule rule, Action changed)
    {
        var action = new ComboBox { Width = 170, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in Actions) action.Items.Add(Loc.T("exa." + a));
        action.SelectedIndex = Array.IndexOf(Actions, rule.Action);

        var color = new ColorPickButton { Color = rule.Color, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        color.ColorChanged += c => { rule.Color = c; changed(); };

        var effect = new ComboBox { Width = 200, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        effect.Items.Add(Loc.T("ex.effect.own"));
        foreach (var e in EffectCatalog.All) effect.Items.Add(Loc.T(e.NameKey));
        int index = EffectCatalog.All.ToList().FindIndex(e => e.Id == rule.Effect);
        effect.SelectedIndex = index < 0 ? 0 : index + 1;
        effect.SelectionChanged += (_, _) =>
        {
            rule.Effect = effect.SelectedIndex <= 0 ? null : EffectCatalog.All[effect.SelectedIndex - 1].Id;
            changed();
        };

        void ShowExtras()
        {
            color.Visibility = rule.Action == ExclusionAction.Color ? Visibility.Visible : Visibility.Collapsed;
            effect.Visibility = rule.Action == ExclusionAction.Rainbow ? Visibility.Visible : Visibility.Collapsed;
        }
        action.SelectionChanged += (_, _) =>
        {
            rule.Action = Actions[Math.Max(0, action.SelectedIndex)];
            ShowExtras();
            changed();
        };
        ShowExtras();

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(action);
        panel.Children.Add(color);
        panel.Children.Add(effect);
        // Fixed width so the rows line up whether a color or an effect is shown next to the action.
        panel.MinWidth = 170 + 8 + 200;
        return panel;
    }

    void ShowRules()
    {
        RulesList.Children.Clear();
        foreach (var rule in _rules)
        {
            var r = rule;
            var icon = new Image { Width = 24, Height = 24, Source = IconOf(r.Path), Margin = new Thickness(0, 0, 12, 0) };

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = r.Exe, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(r.Path))
            {
                var path = new TextBlock { Text = r.Path, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = r.Path };
                path.SetResourceReference(StyleProperty, "Rl.Muted");
                path.TextWrapping = TextWrapping.NoWrap;
                names.Children.Add(path);
            }

            var action = ActionEditor(r, Apply);
            action.Margin = new Thickness(10, 0, 8, 0);

            var remove = new Button { Content = "", VerticalAlignment = VerticalAlignment.Center };
            remove.SetResourceReference(StyleProperty, "Rl.Button.Icon");
            remove.SetResourceReference(ToolTipProperty, "Loc.ex.remove");
            remove.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "Loc.ex.remove");
            remove.Click += (_, _) => { _rules.Remove(r); ShowRules(); Apply(); };

            var row = new Grid { Margin = new Thickness(12, 8, 10, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(names, 1);
            Grid.SetColumn(action, 2);
            Grid.SetColumn(remove, 3);
            row.Children.Add(icon);
            row.Children.Add(names);
            row.Children.Add(action);
            row.Children.Add(remove);

            var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
            line.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");
            RulesList.Children.Add(line);
        }
        EmptyText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Add(string exe, string? path)
    {
        if (_rules.Any(r => string.Equals(r.Exe, exe, StringComparison.OrdinalIgnoreCase))) return;
        // A known player gets "cinema", anything else (mostly games) the rainbow.
        _rules.Add(new ExclusionRule { Exe = exe, Path = path, Action = VideoPlayers.Known.Contains(exe) ? ExclusionAction.Cinema : ExclusionAction.Rainbow });
        ShowRules();
        Apply();
    }

    // ----- ways to add a program -----

    void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "*.exe|*.exe" };
        if (dialog.ShowDialog(this) == true)
            Add(Path.GetFileName(dialog.FileName), dialog.FileName);
    }

    void Running_Click(object sender, RoutedEventArgs e)
    {
        RunningMenu.Items.Clear();
        foreach (var (exe, path, title) in RunningPrograms())
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Image { Width = 18, Height = 18, Source = IconOf(path), Margin = new Thickness(-14, 0, 10, 0) });
            header.Children.Add(new TextBlock { Text = exe, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) });
            var titleText = new TextBlock { Text = title, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "Rl.Text.Muted");
            header.Children.Add(titleText);
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => Add(exe, path);
            RunningMenu.Items.Add(item);
        }
        if (RunningMenu.Items.Count == 0) RunningMenu.Items.Add(new MenuItem { Header = Loc.T("ex.add.none"), IsEnabled = false });
        RunningMenu.PlacementTarget = RunningButton;
        RunningMenu.Placement = PlacementMode.Bottom;
        RunningMenu.IsOpen = true;
    }

    /// <summary>Programs that have a window, one per exe, without ReLight itself.</summary>
    static List<(string exe, string? path, string title)> RunningPrograms()
    {
        var list = new List<(string, string?, string)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                        continue;
                    string exe = p.ProcessName + ".exe";
                    if (list.Any(x => string.Equals(x.Item1, exe, StringComparison.OrdinalIgnoreCase))) continue;
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    list.Add((exe, path, p.MainWindowTitle));
                }
                catch { }
            }
        }
        return list.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    void Target_Down(object sender, MouseButtonEventArgs e)
    {
        Target.CaptureMouse();
        Target.SetResourceReference(Border.BorderBrushProperty, "Rl.Accent");
        Mouse.OverrideCursor = Cursors.Cross;
        e.Handled = true;
    }

    /// <summary>Released over some window: add the program that owns it.</summary>
    void Target_Up(object sender, MouseButtonEventArgs e)
    {
        if (!Target.IsMouseCaptured) return;
        Target.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        Target.SetResourceReference(Border.BorderBrushProperty, "Rl.Border");
        e.Handled = true;

        if (!GetCursorPos(out var pt)) return;
        var hwnd = GetAncestor(WindowFromPoint(pt), 2 /* GA_ROOT */);
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId) return;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { }
            Add(p.ProcessName + ".exe", path);
        }
        catch
        {
            // The program closed in the meantime.
        }
    }

    static ImageSource? IconOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        _saved = true;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
