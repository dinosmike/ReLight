using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ReLight;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = " " + typeof(App).Assembly.GetName().Version?.ToString(2);
        TitleBarIcon.Source = LargestIconFrame();
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    public static void ShowFor(Window owner)
    {
        var w = new AboutWindow();
        if (owner.IsVisible) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
    }

    /// <summary>The 256 px frame of app.ico, so the title icon stays sharp.</summary>
    internal static BitmapFrame? LargestIconFrame()
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/app.ico"),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var best = decoder.Frames.MaxBy(f => f.PixelWidth);
            best?.Freeze();
            return best;
        }
        catch
        {
            return null;
        }
    }

    static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    void Social_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }) Open(url);
    }

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
