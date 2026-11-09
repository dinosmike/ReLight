using System.Windows;
using System.Windows.Input;

namespace ReLight;

public enum MessageKind { Info, Warning, Question }

/// <summary>Themed replacement for MessageBox, in the same look as the About window.</summary>
public partial class MessageWindow : Window
{
    MessageWindow(string message, string title, MessageKind kind)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        TitleBarIcon.Source = AboutWindow.LargestIconFrame();
        IconGlyph.Text = kind switch
        {
            MessageKind.Warning => "",
            MessageKind.Question => "",
            _ => "",
        };
        if (kind == MessageKind.Warning) IconGlyph.SetResourceReference(ForegroundProperty, "Rl.Status.Warn");
        CancelButton.Visibility = kind == MessageKind.Question ? Visibility.Visible : Visibility.Collapsed;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    /// <summary>Show a message; for <see cref="MessageKind.Question"/> returns true when the user pressed OK.</summary>
    public static bool Show(Window? owner, string message, MessageKind kind = MessageKind.Info, string title = "ReLight")
    {
        var w = new MessageWindow(message, title, kind);
        if (owner is { IsVisible: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true;
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
