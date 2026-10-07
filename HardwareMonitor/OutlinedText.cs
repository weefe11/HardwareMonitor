using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;

namespace HardwareMonitor;

/// <summary>Small vector text element: a real outline without bitmap effects.</summary>
public sealed class OutlinedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(OutlinedText), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register(nameof(Outline), typeof(bool), typeof(OutlinedText), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(nameof(TextAlignment), typeof(System.Windows.TextAlignment), typeof(OutlinedText), new FrameworkPropertyMetadata(System.Windows.TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));
    public System.Windows.TextAlignment TextAlignment { get => (System.Windows.TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public bool Outline { get => (bool)GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }
    private Geometry? _geometry;
    private double _textWidth;
    private static readonly Typeface Typeface = new(new System.Windows.Media.FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Pen OutlinePen = CreatePen();
    private FormattedText? _formattedText;

    private static Pen CreatePen()
    {
        var pen = new Pen(Brushes.Black, 1.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = new FormattedText(Text ?? "", CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            Typeface, 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _geometry = text.BuildGeometry(new Point(2, 2));
        _formattedText = text;
        _textWidth = Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 4;
        return new Size(_textWidth, 23);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_geometry == null) return;
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        drawingContext.PushTransform(new TranslateTransform(TextAlignment == System.Windows.TextAlignment.Right ? Math.Max(0, ActualWidth - _textWidth) : 0, 0));
        if (Outline) drawingContext.DrawGeometry(null, OutlinePen, _geometry);
        _formattedText!.SetForegroundBrush(Foreground);
        drawingContext.DrawText(_formattedText, new Point(2, 2));
        drawingContext.Pop(); drawingContext.Pop();
    }
}
