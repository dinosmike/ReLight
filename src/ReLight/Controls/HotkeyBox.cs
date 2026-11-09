using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReLight.Core;
using ReLight.Services;

namespace ReLight.Controls;

/// <summary>Field that records a key combination when focused; Backspace or Delete clears it.</summary>
public sealed class HotkeyBox : TextBox
{
    string _shortcut = "";

    public string Shortcut
    {
        get => _shortcut;
        set
        {
            _shortcut = value ?? "";
            Text = _shortcut.Length > 0 ? _shortcut : Loc.T("hk.none");
        }
    }

    public HotkeyBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox)); // a subclass does not pick up the implicit TextBox style
        Height = 32;
        Padding = new Thickness(8, 0, 8, 0);
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;
        ContextMenu = null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab) { e.Handled = false; return; }
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            Shortcut = "";
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.None)
            return;

        var text = HotkeyService.Format(Keyboard.Modifiers, key);
        if (HotkeyService.TryParse(text, out _, out _)) Shortcut = text;
    }
}
