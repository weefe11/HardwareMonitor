using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Point = System.Windows.Point;

namespace HardwareMonitor;

public partial class OverlayWindow : Window
{
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(System.Windows.Media.Brush), typeof(OverlayWindow), new PropertyMetadata(System.Windows.Media.Brushes.White));
    public static readonly DependencyProperty TextAlphaProperty = DependencyProperty.Register(nameof(TextAlpha), typeof(double), typeof(OverlayWindow), new PropertyMetadata(1d));
    public static readonly DependencyProperty TextOutlineProperty = DependencyProperty.Register(nameof(TextOutline), typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true));
    public System.Windows.Media.Brush TextBrush { get => (System.Windows.Media.Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public double TextAlpha { get => (double)GetValue(TextAlphaProperty); set => SetValue(TextAlphaProperty, value); }
    public bool TextOutline { get => (bool)GetValue(TextOutlineProperty); set => SetValue(TextOutlineProperty, value); }
    public static readonly DependencyProperty TextScaleProperty = DependencyProperty.Register(nameof(TextScale), typeof(double), typeof(OverlayWindow), new PropertyMetadata(14d / 13));
    public double TextScale { get => (double)GetValue(TextScaleProperty); set => SetValue(TextScaleProperty, value); }
    public static readonly DependencyProperty DragSurfaceProperty = DependencyProperty.Register(nameof(DragSurface), typeof(System.Windows.Media.Brush), typeof(OverlayWindow),
        new PropertyMetadata(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0))));
    public System.Windows.Media.Brush DragSurface { get => (System.Windows.Media.Brush)GetValue(DragSurfaceProperty); private set => SetValue(DragSurfaceProperty, value); }
    public void ApplyAppearance(System.Windows.Media.Color color, double alpha, bool outline, double fontSize = 14)
    {
        var brush = new System.Windows.Media.SolidColorBrush(color); brush.Freeze();
        TextBrush = brush; TextAlpha = Math.Clamp(alpha, .1, 1); TextOutline = outline;
        TextScale = Math.Clamp(double.IsFinite(fontSize) ? fontSize : 14, 12, 20) / 13;
    }
    private const int ExtendedStyleIndex = -20;
    private const int ToolWindowStyle = 0x80;
    private const int TransparentStyle = 0x20;
    private readonly ObservableCollection<OverlayRowItem> _rows = new();
    public event EventHandler? PositionSaved;

    public OverlayWindow()
    {
        InitializeComponent();
        OverlayItems.ItemsSource = _rows;
    }

    public void SetRows(IReadOnlyList<OverlayRowItem> rows)
    {
        var keys = rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!keys.Contains(_rows[i].Key)) _rows.RemoveAt(i);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = _rows.FirstOrDefault(existing => existing.Key == rows[i].Key);
            if (row == null) _rows.Insert(i, rows[i]);
            else
            {
                int current = _rows.IndexOf(row);
                if (current != i) _rows.Move(current, i);
                row.Name = rows[i].Name;
                var metrics = rows[i].Metrics;
                var metricKeys = metrics.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
                for (int j = row.Metrics.Count - 1; j >= 0; j--)
                    if (!metricKeys.Contains(row.Metrics[j].Key)) row.Metrics.RemoveAt(j);
                for (int j = 0; j < metrics.Count; j++)
                {
                    var existing = row.Metrics.FirstOrDefault(m => m.Key == metrics[j].Key);
                    if (existing == null) row.Metrics.Insert(j, metrics[j]);
                    else
                    {
                        int index = row.Metrics.IndexOf(existing);
                        if (index != j) row.Metrics.Move(index, j);
                        existing.Text = metrics[j].Text;
                        existing.Warning = metrics[j].Warning;
                        existing.Separator = metrics[j].Separator;
                    }
                }
            }
        }
        EmptySelection.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SetLocked(MainWindow.IsOverlayLocked);
    }

    public void SetLocked(bool locked)
    {
        // Windows ignores fully transparent pixels during layered-window hit testing.
        DragSurface = locked ? System.Windows.Media.Brushes.Transparent :
            (System.Windows.Media.Brush)DragSurfaceProperty.DefaultMetadata.DefaultValue;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, ExtendedStyleIndex) | ToolWindowStyle;
        style = locked ? style | TransparentStyle : style & ~TransparentStyle;
        SetWindowLong(handle, ExtendedStyleIndex, style);
    }

    public void RestorePosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top)) return;
        Left = left;
        Top = top;
        KeepOnScreen();
    }

    private void KeepOnScreen()
    {
        // Recheck after row changes: a restored or resized overlay must not leave all screens.
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var workAreas = System.Windows.Forms.Screen.AllScreens.Select(screen =>
        {
            var area = screen.WorkingArea;
            var start = fromDevice?.Transform(new Point(area.Left, area.Top)) ?? new Point(area.Left, area.Top);
            var end = fromDevice?.Transform(new Point(area.Right, area.Bottom)) ?? new Point(area.Right, area.Bottom);
            return new Rect(start, end);
        }).ToList();
        if (workAreas.Count == 0) return;
        var bounds = new Rect(Left, Top, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1));
        var workArea = workAreas.OrderByDescending(area =>
        {
            var intersection = Rect.Intersect(bounds, area);
            return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
        }).First();
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - ActualWidth));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - ActualHeight));
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || MainWindow.IsOverlayLocked) return;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        KeepOnScreen();
        PositionSaved?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded)
        {
            KeepOnScreen();
            PositionSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
