using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReLight.Controls;

/// <summary>
/// Press anywhere on a slider and move the mouse: the value jumps under the pointer and then
/// follows it live while the button is held — on the track as well as on the thumb.
/// Switched on for every slider by the styles in Controls.xaml.
/// </summary>
/// <remarks>
/// WPF's own IsMoveToPointEnabled must stay off: its class handler runs first, moves the value
/// and marks the press as handled, so the drag never starts. Here the press is taken even when
/// it is already marked handled.
/// </remarks>
public static class SliderDrag
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    static readonly MouseButtonEventHandler DownHandler = Down;
    static readonly MouseEventHandler MoveHandler = Move;
    static readonly MouseButtonEventHandler UpHandler = Up;

    static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;
        if ((bool)e.NewValue)
        {
            slider.IsMoveToPointEnabled = false;
            slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, DownHandler, handledEventsToo: true);
            slider.AddHandler(UIElement.PreviewMouseMoveEvent, MoveHandler, handledEventsToo: true);
            slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, UpHandler, handledEventsToo: true);
        }
        else
        {
            slider.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, DownHandler);
            slider.RemoveHandler(UIElement.PreviewMouseMoveEvent, MoveHandler);
            slider.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, UpHandler);
        }
    }

    static void Down(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (!slider.IsEnabled) return;
        slider.Focus();
        // Capture on the slider itself so the thumb does not start its own drag.
        slider.CaptureMouse();
        Follow(slider, e);
        e.Handled = true;
    }

    static void Move(object sender, MouseEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Follow(slider, e);
            e.Handled = true;
        }
    }

    static void Up(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (!slider.IsMouseCaptured) return;
        Follow(slider, e);
        slider.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>
    /// Value under the pointer: the thumb's centre travels from half a thumb in from the left
    /// to half a thumb in from the right. (Track.ValueFromPoint works relative to the current
    /// value and does not land under the cursor on the first press.)
    /// </summary>
    static void Follow(Slider slider, MouseEventArgs e)
    {
        if (slider.Template?.FindName("PART_Track", slider) is not Track track || track.ActualWidth <= 0) return;
        double thumb = track.Thumb?.ActualWidth ?? 0;
        double usable = Math.Max(1, track.ActualWidth - thumb);
        double share = Math.Clamp((e.GetPosition(track).X - thumb / 2) / usable, 0, 1);
        slider.Value = slider.Minimum + share * (slider.Maximum - slider.Minimum);
    }
}
