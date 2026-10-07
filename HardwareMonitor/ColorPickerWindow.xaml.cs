using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
namespace HardwareMonitor;

public partial class ColorPickerWindow : FluentWindow
{
    public event EventHandler<System.Windows.Media.Color>? ColorChanged;
    public System.Windows.Media.Color SelectedColor { get; private set; }
    private double _saturation, _value;
    private bool _ready;
    public ColorPickerWindow(System.Windows.Media.Color color, double alpha, bool outline)
    {
        InitializeComponent();
        var hsv = ColorSpectrum.ToHsv(color); _saturation = hsv.Saturation; _value = hsv.Value;
        HueSlider.Value = hsv.Hue;
        LightPreview.Opacity = DarkPreview.Opacity = alpha; LightPreview.Outline = DarkPreview.Outline = outline;
        _ready = true; UpdateColor();
    }
    private void UpdateColor()
    {
        if (!_ready) return;
        SelectedColor = ColorSpectrum.FromHsv(HueSlider.Value, _saturation, _value);
        HueSurface.Fill = new SolidColorBrush(ColorSpectrum.FromHsv(HueSlider.Value, 1, 1));
        HexBox.Text = ColorSpectrum.Hex(SelectedColor);
        LightPreview.Foreground = DarkPreview.Foreground = new SolidColorBrush(SelectedColor);
        UpdateMarker(); ColorChanged?.Invoke(this, SelectedColor);
    }
    private void UpdateMarker()
    {
        System.Windows.Controls.Canvas.SetLeft(Marker, _saturation * ColorField.ActualWidth - 6);
        System.Windows.Controls.Canvas.SetTop(Marker, (1 - _value) * ColorField.ActualHeight - 6);
        System.Windows.Controls.Canvas.SetLeft(MarkerEdge, System.Windows.Controls.Canvas.GetLeft(Marker));
        System.Windows.Controls.Canvas.SetTop(MarkerEdge, System.Windows.Controls.Canvas.GetTop(Marker));
    }
    private void Pick(MouseEventArgs e)
    {
        var p = e.GetPosition(ColorField);
        _saturation = Math.Clamp(p.X / Math.Max(1, ColorField.ActualWidth), 0, 1);
        _value = 1 - Math.Clamp(p.Y / Math.Max(1, ColorField.ActualHeight), 0, 1); UpdateColor();
    }
    private void Field_MouseDown(object sender, MouseButtonEventArgs e) { ColorField.Focus(); ColorField.CaptureMouse(); Pick(e); e.Handled = true; }
    private void Field_MouseMove(object sender, MouseEventArgs e) { if (ColorField.IsMouseCaptured) Pick(e); }
    private void Field_MouseUp(object sender, MouseButtonEventArgs e) { if (ColorField.IsMouseCaptured) { Pick(e); ColorField.ReleaseMouseCapture(); } }
    private void Field_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarker();
    private void Hue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateColor();
    private void Field_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01;
        switch (e.Key)
        {
            case Key.Left: _saturation -= step; break; case Key.Right: _saturation += step; break;
            case Key.Up: _value += step; break; case Key.Down: _value -= step; break; default: return;
        }
        _saturation = Math.Clamp(_saturation, 0, 1); _value = Math.Clamp(_value, 0, 1); UpdateColor(); e.Handled = true;
    }
    private void ReadHex()
    {
        if (!ColorSpectrum.TryHex(HexBox.Text, out var color)) { HexBox.Text = ColorSpectrum.Hex(SelectedColor); return; }
        var hsv = ColorSpectrum.ToHsv(color); _ready = false; HueSlider.Value = hsv.Hue; _saturation = hsv.Saturation; _value = hsv.Value; _ready = true; UpdateColor();
    }
    private void Hex_LostFocus(object sender, RoutedEventArgs e) => ReadHex();
    private void Hex_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) { ReadHex(); e.Handled = true; } }
    private void Apply_Click(object sender, RoutedEventArgs e) { ReadHex(); DialogResult = true; }
}
