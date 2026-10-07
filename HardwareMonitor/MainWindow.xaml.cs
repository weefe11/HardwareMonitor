using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;

namespace HardwareMonitor;

public partial class MainWindow : FluentWindow
{
    public static bool IsOverlayLocked { get; private set; }
    public ObservableCollection<HardwareItem> HardwareGroups { get; } = new();
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _pollCancellation = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly Dictionary<string, SensorItem> _items = new(StringComparer.Ordinal);
    private OverlayWindow? _overlay;
    private DiagnosticsWindow? _diagnostics;
    private HotkeyManager? _hotkeys;
    private HwndSource? _source;
    private Task? _pollTask;
    private HardwareSnapshot? _latestSnapshot;
    private string? _settingsError;
    private bool _ready;
    private bool _applyingSnapshot;
    private bool _exiting;
    private bool _disposed;
    private readonly bool _startSampler;

    public MainWindow() : this(new SettingsStore()) { }

    internal MainWindow(SettingsStore settingsStore, bool startSampler = true)
    {
        _startSampler = startSampler;
        _settingsStore = settingsStore;
        _settings = _settingsStore.Load(out _settingsError);
        InitializeComponent();
        SensorsList.ItemsSource = HardwareGroups;
        OutlineCheckbox.IsChecked = _settings.OverlayOutline;
        TransparencySlider.Value = (1 - _settings.OverlayOpacity) * 100;
        TextSizeSlider.Value = _settings.OverlayFontSize;

        ThemeToggle.IsChecked = _settings.DarkTheme;
        ApplyMainTheme(_settings.DarkTheme);
        ApplyOverlayAppearance();
        LockPositionCheckbox.IsChecked = _settings.OverlayLocked;
        IsOverlayLocked = _settings.OverlayLocked;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += SaveTimer_Tick;
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/HardwareMonitor;component/Assets/app.ico")).Stream)
            _trayIcon = new Icon(stream);
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Мониторинг ПК",
            ContextMenuStrip = new ContextMenuStrip()
        };
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        _notifyIcon.ContextMenuStrip.Items.Add("Открыть", null, (_, _) => ShowMainWindow());
        _notifyIcon.ContextMenuStrip.Items.Add("Выход", null, async (_, _) => await ExitAsync());
        _ready = true;
        Loaded += MainWindow_Loaded;
        RefreshSettingsStatus();
        RefreshOverlay();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_startSampler || _pollTask != null) return;
        _pollTask = Task.Run(() => new HardwareSampler().RunAsync(snapshot =>
        {
            if (_pollCancellation.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_exiting && !_disposed) ApplySnapshot(snapshot);
            });
        }, _pollCancellation.Token));
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
        bool firstSnapshot = _latestSnapshot == null;
        _latestSnapshot = snapshot;
        if (firstSnapshot) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SensorsScroll.ScrollToTop()));
        _applyingSnapshot = true;
        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in SensorCatalog.Build(snapshot.Sensors))
            {
                seen.Add(definition.Key);
                if (!_items.TryGetValue(definition.Key, out var item))
                {
                    var reading = definition.Reading;
                    var group = HardwareGroups.FirstOrDefault(g => g.Key == reading.RootId);
                    if (group == null)
                    {
                        group = new HardwareItem
                        {
                            Key = reading.RootId, Name = SensorCatalog.GroupName(reading), Type = reading.RootType,
                            Subtitle = reading.RootType == LibreHardwareMonitor.Hardware.HardwareType.Memory ? "" : reading.RootName
                        };
                        int index = 0;
                        while (index < HardwareGroups.Count && GroupOrder(HardwareGroups[index].Type) <= GroupOrder(group.Type)) index++;
                        HardwareGroups.Insert(index, group);
                    }
                    item = new SensorItem
                    {
                        Key = definition.Key, Name = definition.Name, Kind = definition.Kind,
                        CoreNumber = definition.CoreNumber, ThreadNumber = definition.ThreadNumber,
                        IsSelected = _settings.SelectedSensors.Contains(definition.Key)
                    };
                    _items.Add(item.Key, item);
                    item.PropertyChanged += Sensor_PropertyChanged;
                    if (definition.Kind is MetricKind.CoreLoad or MetricKind.CoreClock or MetricKind.CoreTemperature or MetricKind.GpuHotSpot or MetricKind.GpuMemoryTemperature or MetricKind.FanSpeed)
                    {
                        bool extraTemperature = definition.Kind is MetricKind.GpuHotSpot or MetricKind.GpuMemoryTemperature;
                        string key = group.Key + "::" + (extraTemperature ? "ExtraTemperatures" : definition.Kind.ToString());
                        var sub = group.SubGroups.FirstOrDefault(g => g.Key == key);
                        if (sub == null)
                        {
                            sub = new HardwareItem
                            {
                                Key = key, Type = group.Type,
                                Name = extraTemperature ? "Температуры видеокарты" : definition.Kind switch { MetricKind.CoreLoad => "Загрузка ядер", MetricKind.CoreTemperature => "Температуры ядер", MetricKind.FanSpeed => "Вентиляторы", _ => "Частоты ядер" }
                            };
                            if (definition.Kind == MetricKind.CoreLoad) group.SubGroups.Insert(0, sub);
                            else group.SubGroups.Add(sub);
                        }
                        sub.Sensors.Add(item);
                    }
                    else group.Sensors.Add(item);
                }
                item.Update(definition.Reading);
            }
            foreach (var item in _items.Values.Where(item => !seen.Contains(item.Key))) item.MarkUnavailable();
        }
        finally { _applyingSnapshot = false; }

        RefreshOverlay();
        _diagnostics?.Update(snapshot, _settingsError);
        RefreshSettingsStatus();
    }

    private void Sensor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingSnapshot || e.PropertyName != nameof(SensorItem.IsSelected) || sender is not SensorItem item) return;
        if (item.IsSelected) _settings.SelectedSensors.Add(item.Key);
        else _settings.SelectedSensors.Remove(item.Key);
        RefreshOverlay();
        ScheduleSave();
    }

    private void OverlayCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _exiting) return;
        if (!_items.Values.Any(s => s.IsSelected))
        {
            System.Windows.MessageBox.Show(this, "Выберите датчики для отображения!", "Мониторинг ПК");
            OverlayCheckbox.IsChecked = false;
            return;
        }
        _overlay ??= new OverlayWindow();
        _overlay.PositionSaved += Overlay_PositionSaved;
        _overlay.SetRows(OverlayRows.Build(HardwareGroups));
        ApplyOverlayAppearance();
        _overlay.Show();
        if (_settings.OverlayLeft is double left && _settings.OverlayTop is double top)
            _overlay.RestorePosition(left, top);
    }

    private void Overlay_PositionSaved(object? sender, EventArgs e)
    {
        if (_overlay == null) return;
        if (_settings.OverlayLeft == _overlay.Left && _settings.OverlayTop == _overlay.Top) return;
        _settings.OverlayLeft = _overlay.Left;
        _settings.OverlayTop = _overlay.Top;
        ScheduleSave();
    }

    private void OverlayCheckbox_Unchecked(object sender, RoutedEventArgs e) => CloseOverlay();
    private void CloseOverlay()
    {
        if (_overlay == null) return;
        _settings.OverlayLeft = _overlay.Left;
        _settings.OverlayTop = _overlay.Top;
        _overlay.PositionSaved -= Overlay_PositionSaved;
        _overlay.Close();
        _overlay = null;
        ScheduleSave();
    }

    private void RefreshOverlay()
    {
        _overlay?.SetRows(OverlayRows.Build(HardwareGroups));
        int count = _items.Values.Count(s => s.IsSelected);
        SelectionSummary.Text = count == 0 ? "Выберите показатели галочками выше" : $"Выбрано показателей: {count}. " +
            (IsOverlayLocked ? "Положение закреплено." : "Перемещайте виджет за любую область.");
    }

    private static int GroupOrder(LibreHardwareMonitor.Hardware.HardwareType type) => type switch
    {
        LibreHardwareMonitor.Hardware.HardwareType.Cpu => 0,
        _ when SensorCatalog.IsGpu(type) => 1,
        LibreHardwareMonitor.Hardware.HardwareType.Memory => 2,
        LibreHardwareMonitor.Hardware.HardwareType.Storage => 3,
        _ => 4
    };

    private void LockPositionCheckbox_Checked(object sender, RoutedEventArgs e) => SetOverlayLock(true);
    private void LockPositionCheckbox_Unchecked(object sender, RoutedEventArgs e) => SetOverlayLock(false);
    private void SetOverlayLock(bool locked)
    {
        IsOverlayLocked = locked;
        _settings.OverlayLocked = locked;
        _overlay?.SetLocked(locked);
        RefreshOverlay();
        ScheduleSave();
    }

    private void ThemeToggle_Checked(object sender, RoutedEventArgs e) => ChangeMainTheme(true);
    private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e) => ChangeMainTheme(false);
    private void ChangeMainTheme(bool dark)
    {
        if (!_ready) return;
        _settings.DarkTheme = dark;
        ApplyMainTheme(dark);
        ScheduleSave();
    }
    private static void ApplyMainTheme(bool dark) =>
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);

    private void SetOverlayColor(string color)
    {
        if (!ColorSpectrum.TryHex(color, out var selected)) return;
        _settings.OverlayTextColor = ColorSpectrum.Hex(selected);
        ApplyOverlayAppearance(); ScheduleSave();
    }

    private void ChooseColor_Click(object sender, RoutedEventArgs e)
    {
        string previous = _settings.OverlayTextColor!;
        var picker = new ColorPickerWindow((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(previous), _settings.OverlayOpacity, _settings.OverlayOutline) { Owner = this };
        picker.ColorChanged += (_, color) => SetOverlayColor(ColorSpectrum.Hex(color));
        if (picker.ShowDialog() != true) SetOverlayColor(previous);
    }
    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.OverlayOutline = OutlineCheckbox.IsChecked == true;

        ApplyOverlayAppearance();
        RefreshOverlay();
        ScheduleSave();
    }

    private void Transparency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.OverlayOpacity = 1 - TransparencySlider.Value / 100;
        ApplyOverlayAppearance();
        ScheduleSave();
    }

    private void TextSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.OverlayFontSize = TextSizeSlider.Value;
        ApplyOverlayAppearance();
        ScheduleSave();
    }

    private void ApplyOverlayAppearance()
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.OverlayTextColor!);
        var brush = new SolidColorBrush(color); brush.Freeze();
        CurrentColorPreview.Fill = brush; ColorHex.Text = _settings.OverlayTextColor;
        _overlay?.ApplyAppearance(color, _settings.OverlayOpacity, _settings.OverlayOutline, _settings.OverlayFontSize);
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(HwndHook);
        _hotkeys = new HotkeyManager(handle);
        RestoreHotkey(HotkeyManager.OverlayAction, _settings.OverlayHotkey, OverlayHotkeyBox);
        RestoreHotkey(HotkeyManager.LockAction, _settings.LockHotkey, LockHotkeyBox);
    }

    private void RestoreHotkey(int action, HotkeySettings? settings, TextBox box)
    {
        if (settings == null) return;
        if (_hotkeys?.TrySet(action, settings) == true) box.Text = settings.Text;
        else
        {
            box.Text = "Не задан";
            box.ToolTip = $"Сохранённая комбинация {settings.Text} недоступна. Назначьте другую.";
            _settingsError = $"Комбинация {settings.Text} занята или недопустима.";
            RefreshSettingsStatus();
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0312) return IntPtr.Zero;
        switch (_hotkeys?.FindAction(wParam.ToInt32()))
        {
            case HotkeyManager.OverlayAction:
                OverlayCheckbox.IsChecked = OverlayCheckbox.IsChecked != true;
                handled = true;
                break;
            case HotkeyManager.LockAction:
                LockPositionCheckbox.IsChecked = LockPositionCheckbox.IsChecked != true;
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        if (_hotkeys == null || sender is not TextBox box) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        int action = Equals(box.Tag, "Overlay") ? HotkeyManager.OverlayAction : HotkeyManager.LockAction;
        if (key is Key.Delete or Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            _hotkeys.Clear(action);
            box.Text = "Не задан";
            if (action == HotkeyManager.OverlayAction) _settings.OverlayHotkey = null;
            else _settings.LockHotkey = null;
            ScheduleSave();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        uint nativeModifiers = 0;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) { nativeModifiers |= 2; parts.Add("Ctrl"); }
        if (modifiers.HasFlag(ModifierKeys.Alt)) { nativeModifiers |= 1; parts.Add("Alt"); }
        if (modifiers.HasFlag(ModifierKeys.Shift)) { nativeModifiers |= 4; parts.Add("Shift"); }
        if (modifiers.HasFlag(ModifierKeys.Windows)) { nativeModifiers |= 8; parts.Add("Win"); }
        parts.Add(key.ToString());
        var settings = new HotkeySettings(nativeModifiers, (uint)KeyInterop.VirtualKeyFromKey(key), string.Join(" + ", parts));
        if (!_hotkeys.TrySet(action, settings))
        {
            System.Windows.MessageBox.Show(this, "Комбинация занята или недоступна. Прежняя горячая клавиша сохранена.", "Ошибка привязки");
            return;
        }
        box.Text = settings.Text;
        if (action == HotkeyManager.OverlayAction) _settings.OverlayHotkey = settings;
        else _settings.LockHotkey = settings;
        ScheduleSave();
    }

    private void ResetRanges_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items.Values) item.ResetRange();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnostics == null)
        {
            _diagnostics = new DiagnosticsWindow { Owner = this };
            _diagnostics.Closed += (_, _) => _diagnostics = null;
        }
        if (_latestSnapshot != null) _diagnostics.Update(_latestSnapshot, _settingsError);
        _diagnostics.Show();
        _diagnostics.Activate();
    }

    private void ScheduleSave()
    {
        if (!_ready || _exiting || _disposed) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SaveSettings();
    }
    private void SaveSettings()
    {
        _settingsError = _settingsStore.Save(_settings);
        RefreshSettingsStatus();
    }
    private void RefreshSettingsStatus()
    {
        SettingsStatus.Text = _settingsError ?? "";
        SettingsStatus.Visibility = _settingsError == null ? Visibility.Collapsed : Visibility.Visible;
        string? hardwareIssue = _latestSnapshot?.Errors.FirstOrDefault();
        bool cpuMissing = _latestSnapshot != null && _latestSnapshot.Sensors.Any(s => s.RootType == LibreHardwareMonitor.Hardware.HardwareType.Cpu) &&
            !_latestSnapshot.Sensors.Any(s => s.RootType == LibreHardwareMonitor.Hardware.HardwareType.Cpu &&
                s.Type == LibreHardwareMonitor.Hardware.SensorType.Temperature && s.Value.HasValue);
        HardwareStatus.Text = hardwareIssue != null ? "Часть показателей недоступна. Подробнее — в диагностике." :
            cpuMissing ? "Температура ЦП недоступна. Подробнее — в диагностике." :
            _latestSnapshot == null ? "Поиск оборудования…" : "";
        HardwareStatus.Visibility = HardwareStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e) => ShowMainWindow();
    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _notifyIcon.Visible = false;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            _diagnostics?.Hide();
            Hide();
            _notifyIcon.Visible = true;
            SaveSettings();
        }
        base.OnClosing(e);
    }

    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _saveTimer.Stop();
        CloseOverlay();
        _diagnostics?.Close();
        _notifyIcon.Visible = false;
        _pollCancellation.Cancel();
        try { if (_pollTask != null) await _pollTask; }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        SaveSettings();
        Close();
        Application.Current.Shutdown();
    }

    // Also used during Windows session ending, when the normal asynchronous exit cannot be awaited.
    public void StopForShutdown()
    {
        if (_disposed) return;
        _exiting = true;
        _pollCancellation.Cancel();
        _saveTimer.Stop();
        CloseOverlay();
        SaveSettings();
        // Publishing does not await the Dispatcher, so waiting here cannot deadlock on the UI.
        try { _pollTask?.GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        DisposeResources();
    }

    private void DisposeResources()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkeys?.Dispose();
        _source?.RemoveHook(HwndHook);
        _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
        _saveTimer.Tick -= SaveTimer_Tick;
        foreach (var item in _items.Values) item.PropertyChanged -= Sensor_PropertyChanged;
        _pollCancellation.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopForShutdown();
        base.OnClosed(e);
    }


}
