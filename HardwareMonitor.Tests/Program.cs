using HardwareMonitor;
using LibreHardwareMonitor.Hardware;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--probe")) return Probe(args).GetAwaiter().GetResult();
            if (args.Contains("--smoke")) return Smoke();
            if (args.Contains("--interactive")) return Interactive(args);
            if (args.Contains("--soak")) return Interactive(args);
            if (args.Contains("--drag")) return DragTest();
            if (args.Contains("--screenshots")) return Screenshots(args);
            CheckFormattingAndRanges();
            CheckColorsAndAlerts();
            CheckCpuCatalog();
            CheckGpuCatalog();
            CheckCompactCatalog();
            CheckNestedHardware();
            CheckHotkeys();
            CheckSettings();
            CheckOverlayRows();
            CheckDeviceBlocks();
            if (args.Contains("--ui")) CheckUi(args);
            Console.WriteLine($"PASS: {_checks} checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static SensorReading Reading(string name, SensorType type, float? value,
        string root = "/intelcpu/0", HardwareType rootType = HardwareType.Cpu, string? id = null) =>
        new(id ?? root + "/" + type + "/" + name, name, type, value, root, "Device", root, "Device", rootType);

    private static SensorItem Item(SensorDefinition definition) => new()
    {
        Key = definition.Key, Name = definition.Name, Kind = definition.Kind,
        CoreNumber = definition.CoreNumber, ThreadNumber = definition.ThreadNumber
    };

    internal static void CheckFormattingAndRanges()
    {
        Assert(SensorFormatting.Format(null, SensorType.Temperature) == "—", "null must not be zero");
        Assert(SensorFormatting.Format(float.NaN, SensorType.Load) == "—", "NaN must be unavailable");
        Assert(SensorFormatting.Format(float.PositiveInfinity, SensorType.Power) == "—", "Infinity must be unavailable");
        Assert(SensorFormatting.Format(0, SensorType.Load).Contains("0"), "Real zero must remain zero");
        Assert(SensorFormatting.Metric(-1, SensorType.Fan) == "—" && SensorFormatting.Metric(0, SensorType.Fan) == "0 об/мин", "Invalid negative RPM versus real stopped fan");
        Assert(SensorFormatting.Metric(101, SensorType.Load) == "—" && SensorFormatting.Metric(-1, SensorType.Clock) == "—", "Impossible load and negative frequency unavailable");
        Assert(SensorFormatting.Metric(9999, SensorType.Temperature) == "—" && SensorFormatting.Metric(-20, SensorType.Temperature).Contains("-20"), "Reject impossible temperature without rejecting subzero readings");
        Assert(SensorFormatting.Format(15, SensorType.Power).EndsWith("Вт"), "Power unit");
        var item = new SensorItem { Key = "cpu", Name = "ЦП", Kind = MetricKind.CpuTemperature };
        item.Update(Reading("CPU Package", SensorType.Temperature, 40));
        item.Update(Reading("CPU Package", SensorType.Temperature, 60));
        item.Update(Reading("CPU Package", SensorType.Temperature, null));
        Assert(item.Minimum == 40 && item.Maximum == 60 && item.Value == "—", "null must not pollute extrema");
        item.ResetRange();
        Assert(item.Minimum == null && item.Maximum == null, "Reset session range");
        item.Update(Reading("CPU Package", SensorType.Temperature, 50));
        item.Update(Reading("Core Max", SensorType.Temperature, 65));
        Assert(item.Minimum == 65 && item.Maximum == 65, "Different sources must not mix extrema");
        item.MarkUnavailable();
        Assert(item.Current == null && item.Maximum == 65, "Disappearing sensor retains session range");
        Assert(SensorFormatting.Metric(4200, SensorType.Clock).EndsWith("ГГц"), "Readable GHz for clocks");
        Assert(SensorFormatting.Metric(1024, SensorType.SmallData) == SensorFormatting.Metric(1, SensorType.Data), "Normalize VRAM MB to GB without changing raw readings");
        item.Update(Reading("Core Max", SensorType.Temperature, 70));
        Assert(!item.Value.Contains('!'), "No universal thermal warning threshold");
    }

    internal static void CheckCpuCatalog()
    {
        var intel = new[]
        {
            Reading("CPU Core #1 Distance to TjMax", SensorType.Temperature, 30),
            Reading("Core Max", SensorType.Temperature, 61),
            Reading("CPU Package", SensorType.Temperature, 55),
            Reading("CPU Core #1", SensorType.Load, 20),
            Reading("CPU Core #1", SensorType.Clock, 4300),
            Reading("CPU Core #1", SensorType.Factor, 43)
        };
        var catalog = SensorCatalog.Build(intel);
        Assert(catalog.Single(s => s.Kind == MetricKind.CpuTemperature).Reading.Name == "CPU Package", "Intel package priority");
        Assert(SensorCatalog.Build(intel.Reverse()).Single(s => s.Kind == MetricKind.CpuTemperature).Reading.Name == "CPU Package", "Independent of sensor order");
        Assert(catalog.Count(s => s.Kind == MetricKind.CoreLoad) == 1, "Non-SMT core loads must not be dropped");
        Assert(catalog.Count(s => s.Kind == MetricKind.CoreClock) == 1, "Factor is not a clock");
        Assert(catalog.Single(s => s.Kind == MetricKind.CpuClock).Reading.Value == 4300, "Intel average is calculated from core clocks");
        var missing = SensorCatalog.Build(new[] { intel[0], Reading("CPU Package", SensorType.Temperature, null) });
        Assert(missing.Single().Reading.Value == null, "Distance to TjMax cannot replace CPU temperature");
        var fallback = SensorCatalog.Build(new[] { Reading("CPU Package", SensorType.Temperature, null), intel[1] }).Single();
        Assert(fallback.Reading.Name == "Core Max", "Use available temperature fallback");
        Assert(fallback.Key == catalog.Single(s => s.Kind == MetricKind.CpuTemperature).Key, "Stable choice across fallback sources");
        var coreFallback = SensorCatalog.Build(new[] { Reading("CPU Package", SensorType.Temperature, null), Reading("CPU Core #2", SensorType.Temperature, 59) }).Single(s => s.Kind == MetricKind.CpuTemperature);
        Assert(coreFallback.Reading.Value == 59, "Available physical core fallback");
        var amd = SensorCatalog.Build(new[]
        {
            Reading("Core (Tctl)", SensorType.Temperature, 75, "/amdcpu/0"),
            Reading("SoC", SensorType.Temperature, 47, "/amdcpu/0"),
            Reading("Core (Tdie)", SensorType.Temperature, 55, "/amdcpu/0"),
            Reading("Core (Tctl/Tdie)", SensorType.Temperature, 56, "/amdcpu/0"),
            Reading("Package", SensorType.Power, 25, "/amdcpu/0"),
            Reading("Core #1", SensorType.Clock, 3600, "/amdcpu/0"),
            Reading("Core #1 (Effective)", SensorType.Clock, 2000, "/amdcpu/0"),
            Reading("CPU Core #1 Thread #2", SensorType.Load, 15, "/amdcpu/0")
        });
        Assert(amd.Single(s => s.Kind == MetricKind.CpuTemperature).Reading.Name == "Core (Tdie)", "AMD die temperature over control offset");
        Assert(amd.Single(s => s.Kind == MetricKind.CpuPower).Reading.Value == 25, "AMD package power");
        Assert(amd.Count(s => s.Kind == MetricKind.CoreClock) == 1, "Deduplicate base and effective clocks");
        Assert(amd.Single(s => s.Kind == MetricKind.CoreLoad).ThreadNumber == 2, "Keep real thread number");
    }

    internal static void CheckGpuCatalog()
    {
        var sensors = new[]
        {
            Reading("GPU VR VDDC", SensorType.Temperature, 70, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU Core", SensorType.Temperature, 50, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU Hot Spot", SensorType.Temperature, 65, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU PPT", SensorType.Power, 20, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("D3D Dedicated Memory Used", SensorType.SmallData, 500, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU Core", SensorType.Temperature, 45, "/gpuamd/1", HardwareType.GpuAmd)
        };
        var catalog = SensorCatalog.Build(sensors);
        Assert(catalog.Count(s => s.Kind == MetricKind.GpuTemperature) == 2, "Do not merge two GPUs");
        Assert(catalog.First(s => s.Kind == MetricKind.GpuTemperature).Reading.Value == 50, "VRM temperature is not GPU core");
        Assert(catalog.Single(s => s.Kind == MetricKind.GpuPower).Reading.Name == "GPU PPT", "AMD PPT fallback");
        Assert(catalog.Single(s => s.Kind == MetricKind.GpuMemoryUsed).Reading.Type == SensorType.SmallData, "D3D memory fallback with correct unit");
        Assert(catalog.Single(s => s.Kind == MetricKind.GpuHotSpot).Reading.Value == 65, "Preserve hotspot");
        var memory = SensorCatalog.Build(new[]
        {
            Reading("Memory", SensorType.Load, 50, "/ram", HardwareType.Memory),
            Reading("Memory", SensorType.Load, 80, "/vram", HardwareType.Memory)
        });
        Assert(memory.Count == 1 && memory[0].Reading.RootId == "/ram", "Do not confuse physical and virtual RAM");
    }

    internal static void CheckColorsAndAlerts()
    {
        foreach (var color in new[] { Colors.Red, Colors.Lime, Colors.Blue, Colors.Black, Colors.White, Color.FromRgb(43, 176, 221) })
        {
            var hsv = ColorSpectrum.ToHsv(color);
            var result = ColorSpectrum.FromHsv(hsv.Hue, hsv.Saturation, hsv.Value);
            Assert(result == color, "Full-spectrum HSV round-trip");
            Assert(ColorSpectrum.TryHex(ColorSpectrum.Hex(color), out var hex) && hex == color, "Hex round-trip");
        }
        Assert(!ColorSpectrum.TryHex("#XYZ123", out _), "Invalid manual color rejected");
        var alert = new TemperatureAlert();
        Assert(alert.Update(MetricKind.CpuTemperature, 96) == TemperatureState.Normal, "No alarm from a single sample");
        alert.Update(MetricKind.CpuTemperature, 96);
        Assert(alert.Update(MetricKind.CpuTemperature, 96) == TemperatureState.High, "Three hot samples trigger indicator");
        Assert(alert.Update(MetricKind.CpuTemperature, 94) == TemperatureState.High, "Hysteresis suppresses flicker");
        alert.Update(MetricKind.CpuTemperature, 50); alert.Update(MetricKind.CpuTemperature, 50);
        Assert(alert.Update(MetricKind.CpuTemperature, 50) == TemperatureState.Normal, "Recovery is debounced");
        alert.Update(MetricKind.StorageTemperature, 65); alert.Update(MetricKind.StorageTemperature, 65);
        Assert(alert.Update(MetricKind.StorageTemperature, 65) == TemperatureState.Elevated, "Disk has its own thresholds");
        Assert(alert.Update(MetricKind.StorageTemperature, null) == TemperatureState.Normal, "No warning for missing data");
        Assert(alert.Update(MetricKind.FanSpeed, 0) == TemperatureState.Normal, "Fan-stop is not assumed to be a failure");
    }

    private static void CheckRenderedOverlay(MainWindow main, OverlayWindow overlay)
    {
        Settle(overlay);
        var bitmap = OverlayBitmap(overlay);
        var texts = Descendants<OutlinedText>((DependencyObject)overlay.Content).Where(t => t.Text.Length > 0 && t.Visibility == Visibility.Visible && t.ActualWidth > 0).ToList();
        Assert(texts.Count > 0 && texts.All(t => ((SolidColorBrush)t.Foreground).Color == Color.FromRgb(112, 214, 255)), "Every visible normal glyph receives chosen color");
        Assert(texts.All(t => !t.Outline), "Actual text receives outline switch");
        Assert(((System.Windows.Controls.StackPanel)overlay.FindName("OverlayStack")).Opacity == .8, "Alpha applied to text visual subtree");
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert(pixels.Where((_, i) => i % 4 == 3).Max() <= 205, "Rendered glyph alpha respects 80 percent opacity");
        Assert(pixels[3] == 0 && pixels[^1] == 0, "Overlay background is truly transparent");
        var cpu = main.HardwareGroups.First(g => g.Type == HardwareType.Cpu);
        foreach (var sensor in cpu.Sensors) sensor.IsSelected = true;
        Settle(overlay);
        var reference = OverlayBitmap(overlay);
        double width = reference.PixelWidth, height = reference.PixelHeight;
        foreach (var sample in new float?[] { 0, 9, 100, null, 9999, -1 })
        {
            foreach (var sensor in cpu.AllSensors.Where(s => s.IsSelected))
                sensor.Update(Reading(sensor.RawName, sensor.Type, sample, id: sensor.RawIdentifier));
            overlay.SetRows(OverlayRows.Build(main.HardwareGroups));
            var changed = OverlayBitmap(overlay);
            Assert(changed.PixelWidth == width && changed.PixelHeight == height, $"Changing digits/null cannot resize or wrap overlay: {sample}, {width}x{height} -> {changed.PixelWidth}x{changed.PixelHeight}");
        }
        foreach (var sensor in cpu.Sensors) sensor.IsSelected = false;
        for (int i = 2; i <= 32; i++)
        {
            var sensor = new SensorItem { Key = cpu.Key + "::CoreClock:" + i, Name = "Ядро " + i, Kind = MetricKind.CoreClock, CoreNumber = i, IsSelected = true };
            sensor.Update(Reading("CPU Core #" + i, SensorType.Clock, 4500)); cpu.SubGroups[0].Sensors.Add(sensor);
        }
        overlay.SetRows(OverlayRows.Build(main.HardwareGroups)); Settle(overlay);
        var many = OverlayBitmap(overlay);
        foreach (var sensor in cpu.SubGroups[0].Sensors.Where(s => s.CoreNumber >= 2)) sensor.Update(Reading(sensor.RawName, sensor.Type, null, id: sensor.RawIdentifier));
        overlay.SetRows(OverlayRows.Build(main.HardwareGroups));
        var missingCores = OverlayBitmap(overlay);
        Assert(many.PixelWidth == missingCores.PixelWidth && many.PixelHeight == missingCores.PixelHeight, "32 cores keep layout when values disappear");
        foreach (var sensor in cpu.SubGroups[0].Sensors.Where(s => s.CoreNumber >= 2).ToList()) cpu.SubGroups[0].Sensors.Remove(sensor);
        overlay.SetRows(OverlayRows.Build(main.HardwareGroups));
        Settle(overlay);
        var restored = OverlayBitmap(overlay);
        Assert(restored.PixelHeight < many.PixelHeight, "Removing selected cores rebuilds structure cleanly");
        typeof(MainWindow).GetMethod("SetOverlayColor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { "#70D6FF" });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static RenderTargetBitmap OverlayBitmap(OverlayWindow overlay)
    {
        var content = (FrameworkElement)overlay.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;
        content.Arrange(new Rect(new Point(0, 0), size)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); return bitmap;
    }

    private static void CheckPicker(MainWindow main, OverlayWindow overlay, App app)
    {
        void Open(bool accept)
        {
            main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var picker = app.Windows.OfType<ColorPickerWindow>().Single();
                ((TextBox)picker.FindName("HexBox")).Text = "#E04080";
                typeof(ColorPickerWindow).GetMethod("ReadHex", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
                Assert(((SolidColorBrush)overlay.TextBrush).Color == Color.FromRgb(224, 64, 128), "Picker applies arbitrary color to open overlay immediately");
                if (accept) typeof(ColorPickerWindow).GetMethod("Apply_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, new object[] { picker, new RoutedEventArgs() });
                else picker.Close();
            }));
            typeof(MainWindow).GetMethod("ChooseColor_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
        }
        Open(false);
        Assert(((SolidColorBrush)overlay.TextBrush).Color == Color.FromRgb(112, 214, 255), "Cancel restores previous color");
        Open(true);
        Assert(((SolidColorBrush)overlay.TextBrush).Color == Color.FromRgb(224, 64, 128), "Confirm retains selected color");
        typeof(MainWindow).GetMethod("SetOverlayColor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { "#70D6FF" });
    }

    internal static void CheckNestedHardware()
    {
        int parentUpdates = 0, childUpdates = 0, grandchildUpdates = 0;
        var sensor = Proxy<ISensor>(method => method.Name switch
        {
            "get_Identifier" => new Identifier("lpc", "temperature", "0"),
            "get_Name" => "CPU", "get_SensorType" => SensorType.Temperature,
            "get_Value" => (float?)44, _ => null
        });
        var grandchild = Hardware("grandchild", () => grandchildUpdates++, new[] { sensor }, Array.Empty<IHardware>());
        var child = Hardware("child", () => childUpdates++, Array.Empty<ISensor>(), new[] { grandchild });
        var parent = Hardware("mainboard", () => parentUpdates++, Array.Empty<ISensor>(), new[] { child });
        var snapshot = HardwareSampler.Capture(new[] { parent }, "test");
        Assert(parentUpdates == 1 && childUpdates == 1 && grandchildUpdates == 1, "Update all nested levels once per cycle");
        Assert(snapshot.Sensors.Single().RootId == "/mainboard", "Keep root identity for nested sensor");
        Assert(SensorCatalog.Build(snapshot.Sensors).Single().Kind == MetricKind.BoardTemperature, "Board without own sensors still visible");
        var broken = Hardware("broken", () => throw new IOException("read failed"), new[] { sensor }, new[] { child });
        var failed = HardwareSampler.Capture(new[] { broken }, "test");
        Assert(failed.Errors.Count == 1 && failed.Sensors.First().Value == null, "Do not publish stale reading after update failure");
        Assert(childUpdates == 2 && grandchildUpdates == 2, "Continue children when parent fails");
        var cpuPower = Proxy<ISensor>(method => method.Name switch
        {
            "get_Identifier" => new Identifier("intelcpu", "0", "power", "0"),
            "get_Name" => "CPU Package", "get_SensorType" => SensorType.Power,
            "get_Value" => (float?)0, _ => null
        });
        var cpu = Proxy<IHardware>(method => method.Name switch
        {
            "get_Identifier" => new Identifier("intelcpu", "0"), "get_Name" => "CPU",
            "get_HardwareType" => HardwareType.Cpu, "get_Sensors" => new[] { cpuPower },
            "get_SubHardware" => Array.Empty<IHardware>(), _ => null
        });
        Assert(HardwareSampler.Capture(new[] { cpu }, "test", cpuAccess: false).Sensors.Single().Value == null,
            "Driver failure must not masquerade as zero watts");

        static IHardware Hardware(string name, Action update, ISensor[] sensors, IHardware[] children) => Proxy<IHardware>(method =>
        {
            if (method.Name == "Update") { update(); return null; }
            return method.Name switch
            {
                "get_Identifier" => new Identifier(name), "get_Name" => name,
                "get_HardwareType" => HardwareType.Motherboard, "get_Sensors" => sensors,
                "get_SubHardware" => children, _ => null
            };
        });
    }

    internal static void CheckCompactCatalog()
    {
        var catalog = SensorCatalog.Build(new[]
        {
            Reading("Memory", SensorType.Load, 33, "/ram", HardwareType.Memory),
            Reading("Memory Used", SensorType.Data, 5.3f, "/ram", HardwareType.Memory),
            Reading("Memory Available", SensorType.Data, 10.7f, "/ram", HardwareType.Memory),
            Reading("Memory Clock", SensorType.Clock, 3200, "/ram", HardwareType.Memory),
            Reading("Temperature #1", SensorType.Temperature, 99, "/nvme/0", HardwareType.Storage),
            Reading("Composite Temperature", SensorType.Temperature, 35, "/nvme/0", HardwareType.Storage),
            Reading("Temperature", SensorType.Temperature, 36, "/nvme/0", HardwareType.Storage),
            Reading("Used Space", SensorType.Load, 40, "/nvme/0", HardwareType.Storage),
            Reading("Free Space", SensorType.Data, 400, "/nvme/0", HardwareType.Storage),
            Reading("CPU VRM", SensorType.Temperature, 80, "/mainboard", HardwareType.Motherboard),
            Reading("CPU Socket", SensorType.Temperature, 40, "/mainboard", HardwareType.Motherboard),
            Reading("System", SensorType.Temperature, 35, "/mainboard", HardwareType.Motherboard),
            Reading("Motherboard", SensorType.Temperature, 36, "/mainboard", HardwareType.Motherboard)
        });
        Assert(catalog.Count(s => s.Reading.RootId == "/ram") == 3, "RAM used and available without unreliable clock");
        Assert(catalog.Single(s => s.Kind == MetricKind.StorageTemperature).Reading.Name == "Composite Temperature", "Known composite temperature instead of arbitrary disk channel");
        Assert(catalog.Count(s => s.Reading.RootId == "/nvme/0") == 3, "Available disk free space retained");
        Assert(catalog.Count(s => s.Kind == MetricKind.BoardTemperature) == 2 && catalog.All(s => s.Reading.Name != "CPU VRM"), "Known board/socket roles without duplicate or ambiguous VRM");
    }

    private static T Proxy<T>(Func<MethodInfo, object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, LibraryProxy>();
        ((LibraryProxy)(object)result).Handler = handler;
        return result;
    }

    internal static void CheckHotkeys()
    {
        var registered = new Dictionary<int, (uint, uint)>();
        using var manager = new HotkeyManager(IntPtr.Zero, (_, id, mod, key) =>
        {
            if (key == 88 || registered.Values.Contains((mod, key))) return false;
            registered.Add(id, (mod, key)); return true;
        }, (_, id) => registered.Remove(id));
        Assert(manager.TrySet(1, new HotkeySettings(2, 79, "Ctrl + O")), "Initial hotkey");
        int previous = registered.Keys.Single();
        Assert(!manager.TrySet(1, new HotkeySettings(2, 88, "Ctrl + X")), "Busy hotkey rejected");
        Assert(registered.ContainsKey(previous) && manager.FindAction(previous) == 1, "Old combination survives failed replacement");
        Assert(manager.TrySet(1, new HotkeySettings(2, 79, "Ctrl + O")) && registered.Count == 1, "Same hotkey is no-op");
        Assert(manager.TrySet(1, new HotkeySettings(2, 80, "Ctrl + P")), "Successful replacement");
        Assert(!registered.ContainsKey(previous) && registered.Count == 1, "Old registration removed after success");
        manager.Clear(1);
        Assert(registered.Count == 0, "Clear binding");
        manager.TrySet(1, new HotkeySettings(2, 79, "Ctrl + O"));
        manager.TrySet(2, new HotkeySettings(2, 76, "Ctrl + L"));
        manager.Dispose();
        Assert(registered.Count == 0, "Dispose unregisters all hotkeys");
    }

    internal static void CheckSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Tests", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var initial = store.Load(out var loadError);
        Assert(loadError == null && initial.DarkTheme, "First run defaults");
        var settings = new AppSettings
        {
            SelectedSensors = new() { "/amdcpu/0::CpuTemperature" }, DarkTheme = false,
            OverlayTheme = "Чистый белый", OverlayLeft = -100, OverlayTop = 200,
            OverlayLocked = true, OverlayHotkey = new HotkeySettings(2, 79, "Ctrl + O")
        };
        Assert(store.Save(settings) == null, "Atomic settings save");
        var restored = store.Load(out loadError);
        Assert(loadError == null && restored.SelectedSensors.SetEquals(settings.SelectedSensors), "Persist stable sensor choice");
        Assert(!restored.DarkTheme && restored.OverlayLocked && restored.OverlayLeft == -100 && restored.OverlayHotkey == settings.OverlayHotkey, "Restore theme, hotkey, position, lock");
        Assert(!File.Exists(store.Path + ".tmp"), "No leftover temporary settings");
        Assert(restored.OverlayTextColor == "#F4F6FA" && restored.OverlayTheme == null, "Migrate legacy preset to one text color");
        restored.OverlayTextColor = "#70D6FF"; restored.OverlayOutline = false; restored.OverlayOpacity = .8; restored.OverlayFontSize = 18;
        store.Save(restored);
        var appearance = store.Load(out loadError);
        Assert(appearance.OverlayTextColor == "#70D6FF" && !appearance.OverlayOutline && appearance.OverlayOpacity == .8, "Persist overlay appearance");
        Assert(appearance.OverlayFontSize == 18, "Persist text size");
        File.WriteAllText(store.Path, "{\"OverlayTextColor\":\"invalid\",\"OverlayOpacity\":0}");
        var sanitized = store.Load(out loadError);
        Assert(sanitized.OverlayTextColor == "#F4F6FA" && sanitized.OverlayOpacity == .1, "Repair invalid color and prevent invisible overlay");
        File.WriteAllText(store.Path, "{ invalid }");
        Assert(store.Load(out loadError).SelectedSensors.Count == 0 && loadError != null, "Malformed settings do not crash startup");
        File.WriteAllText(store.Path, "{\"SelectedSensors\": null, \"OverlayTheme\": null}");
        Assert(store.Load(out loadError).SelectedSensors.Count == 0 && loadError == null, "Null settings fields repaired");
        Assert(store.Load(out loadError).OverlayFontSize == 14, "Old settings receive readable default size");
        File.WriteAllText(store.Path, "{\"OverlayFontSize\":999}");
        Assert(store.Load(out loadError).OverlayFontSize == 20, "Unsafe text size bounded");
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }

    internal static void CheckOverlayRows()
    {
        var group = new HardwareItem { Key = "/intelcpu/0", Name = "Процессор", Type = HardwareType.Cpu };
        var definitions = SensorCatalog.Build(new[]
        {
            Reading("CPU Package", SensorType.Temperature, 50), Reading("CPU Core #1 Thread #2", SensorType.Load, 25),
            Reading("CPU Core #1", SensorType.Clock, 4000)
        });
        var cores = new HardwareItem { Key = "core", Name = "Ядра" };
        group.SubGroups.Add(cores);
        foreach (var definition in definitions)
        {
            var item = Item(definition); item.Update(definition.Reading); item.IsSelected = true;
            if (item.CoreNumber != null) cores.Sensors.Add(item); else group.Sensors.Add(item);
        }
        var rows = OverlayRows.Build(new[] { group });
        Assert(rows.Count == 2 && rows[1].DisplayValue.Contains("П2:"), "Do not renumber selected thread 2 to thread 1");

        foreach (var sensor in group.Sensors) sensor.IsSelected = false;
        Assert(OverlayRows.Build(new[] { group }).Count == 2, "Core-only choice retains device header above details");
        cores.Sensors[0].IsSelected = false;
        Assert(cores.IsSelected == null, "Partial subgroup selection");
        cores.IsSelected = false;
        Assert(OverlayRows.Build(new[] { group }).Count == 0, "Group deselection clears overlay");
        cores.IsSelected = true;
        Assert(cores.Sensors.All(s => s.IsSelected), "Group selection restores all cores");
    }

    internal static void CheckDeviceBlocks()
    {
        var readings = new[]
        {
            Reading("CPU Package", SensorType.Temperature, 55),
            Reading("CPU Total", SensorType.Load, 10),
            Reading("CPU Core #1", SensorType.Clock, 4400),
            Reading("CPU Core #1 Thread #1", SensorType.Load, 10),
            Reading("CPU Core #1 Thread #2", SensorType.Load, 20),
            Reading("GPU Core", SensorType.Temperature, 60, "/gpu", HardwareType.GpuNvidia),
            Reading("GPU Core", SensorType.Load, 80, "/gpu", HardwareType.GpuNvidia),
            Reading("GPU Hot Spot", SensorType.Temperature, 75, "/gpu", HardwareType.GpuNvidia),
            Reading("GPU Memory", SensorType.Clock, 8000, "/gpu", HardwareType.GpuNvidia),
            Reading("GPU Fan", SensorType.Fan, 1200, "/gpu", HardwareType.GpuNvidia),
            Reading("Memory", SensorType.Load, 50, "/ram", HardwareType.Memory),
            Reading("Memory Used", SensorType.Data, 8, "/ram", HardwareType.Memory),
            Reading("Memory Available", SensorType.Data, 8, "/ram", HardwareType.Memory),
            Reading("Temperature", SensorType.Temperature, 35, "/disk", HardwareType.Storage),
            Reading("Used Space", SensorType.Load, 50, "/disk", HardwareType.Storage),
            Reading("Free Space", SensorType.Data, 100, "/disk", HardwareType.Storage),
            Reading("System", SensorType.Temperature, 32, "/board", HardwareType.Motherboard),
            Reading("Fan #1", SensorType.Fan, 1000, "/board", HardwareType.Motherboard),
            Reading("Fan #2", SensorType.Fan, 0, "/board", HardwareType.Motherboard)
        };
        var groups = SensorCatalog.Build(readings).GroupBy(d => d.Reading.RootId).Select(g =>
        {
            var hardware = new HardwareItem { Key = g.Key, Name = g.Key, Type = g.First().Reading.RootType };
            foreach (var definition in g) { var sensor = Item(definition); sensor.Update(definition.Reading); sensor.IsSelected = true; hardware.Sensors.Add(sensor); }
            return hardware;
        }).ToList();
        var rows = OverlayRows.Build(groups);
        foreach (var group in groups)
        {
            var block = rows.Where(r => r.Key == group.Key || r.Key.StartsWith(group.Key + "::", StringComparison.Ordinal)).ToList();
            Assert(block[0].Key == group.Key && !block[0].IsDetail, "Every device starts with primary row");
            Assert(block.Skip(1).All(r => r.IsDetail), "All continuation rows are indented device details");
            var indices = block.Select(r => rows.ToList().IndexOf(r)).ToList();
            Assert(indices.Last() - indices.First() + 1 == indices.Count, "Device rows are contiguous");
        }
        Assert(rows.Single(r => r.Key == "/gpu").Metrics.All(m => !m.Label.Contains("Макс.") && !m.Text.Contains("об/мин")), "GPU secondary temperatures and fans excluded from primary row");
        Assert(rows.Single(r => r.Key == "/ram").Metrics.Count == 3, "RAM used/free/load stays in one row");
        Assert(rows.Single(r => r.Key == "/disk").Metrics.Count == 3, "Disk temperature/used/free stays in one row");
        Assert(rows.Where(r => r.Key.StartsWith("/board::")).SelectMany(r => r.Metrics).Count(m => m.Text.EndsWith("об/мин")) == 2, "Board fans stay under board");
        foreach (var group in groups) foreach (var sensor in group.Sensors) sensor.IsSelected = sensor.Kind is MetricKind.GpuHotSpot;
        rows = OverlayRows.Build(groups);
        Assert(rows.Count == 2 && rows[0].Name == "ГП" && rows[1].IsDetail, "Secondary-only GPU retains correct device identity");
    }

    private static int Interactive(string[] args)
    {
        string output = Path.GetFullPath(Argument(args, "--output") ?? ".artifacts/ui-audit.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(output)!, "audit-settings.json"));
        var main = new MainWindow(store); app.MainWindow = main;
        bool soak = args.Contains("--soak");
        if (soak)
        {
            main.Opacity = 0; main.ShowActivated = false; main.IsHitTestVisible = false;
            main.Loaded += async (_, _) =>
            {
                while (main.HardwareGroups.Count == 0) await Task.Delay(50);
                foreach (var sensor in main.HardwareGroups.SelectMany(g => g.AllSensors)) sensor.IsSelected = true;
                ((CheckBox)main.FindName("LockPositionCheckbox")).IsChecked = true;
                ((CheckBox)main.FindName("OverlayCheckbox")).IsChecked = true;
            };
        }
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var samples = new List<object>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        double previous = 0; TimeSpan previousCpu = process.TotalProcessorTime;
        timer.Tick += async (_, _) =>
        {
            process.Refresh();
            double seconds = watch.Elapsed.TotalSeconds;
            var cpu = process.TotalProcessorTime;
            var snapshot = (HardwareSnapshot?)typeof(MainWindow).GetField("_latestSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
            samples.Add(new { Seconds = Math.Round(seconds, 2), ManagedBytes = GC.GetTotalMemory(false), PrivateBytes = process.PrivateMemorySize64,
                Threads = process.Threads.Count, Handles = process.HandleCount,
                CpuPercentOfOneCore = Math.Round((cpu - previousCpu).TotalSeconds / (seconds - previous) * 100, 2),
                UiTimerDelaySeconds = Math.Round(seconds - previous - 10, 2), Errors = snapshot?.Errors,
                Sensors = snapshot?.Sensors.Count, SnapshotAgeSeconds = snapshot == null ? (double?)null : (DateTime.Now - snapshot.Timestamp).TotalSeconds });
            previous = seconds; previousCpu = cpu;
            if (seconds >= (soak ? 180 : 360)) { timer.Stop(); await main.ExitAsync(); }
        };
        main.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F12 && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            { e.Handled = true; timer.Stop(); await main.ExitAsync(); }
        };
        timer.Start(); main.Show(); app.Run(); timer.Stop();
        var poll = (Task?)typeof(MainWindow).GetField("_pollTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
        File.WriteAllText(output, JsonSerializer.Serialize(new { ElapsedSeconds = watch.Elapsed.TotalSeconds, WorkerCompleted = poll?.IsCompleted, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        process.Dispose(); return 0;
    }

    private static int DragTest()
    {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow { Left = 100, Top = 100 };
        app.MainWindow = overlay;
        var main = new OverlayRowItem { Key = "cpu", Name = "ЦП" };
        main.Metrics.Add(new OverlayMetricItem { Key = "temperature", Text = "57 °C", CellWidth = 78, Separator = true });
        main.Metrics.Add(new OverlayMetricItem { Key = "load", Text = "18 %", CellWidth = 66, Separator = true });
        main.Metrics.Add(new OverlayMetricItem { Key = "clock", Text = "4,5 ГГц", CellWidth = 92 });
        var detail = new OverlayRowItem { Key = "core", Name = "Ядро 1", IsDetail = true };
        detail.Metrics.Add(new OverlayMetricItem { Key = "thread", Label = "П1: ", Text = "5 %", CellWidth = 114 });
        var gpu = new OverlayRowItem { Key = "gpu", Name = "ГП" };
        gpu.Metrics.Add(new OverlayMetricItem { Key = "gpuTemperature", Text = "49 °C", CellWidth = 78 });
        overlay.SetRows(new[] { main, detail, gpu });
        overlay.PositionSaved += (_, _) => File.WriteAllText(".artifacts/drag-position.json", JsonSerializer.Serialize(new { overlay.Left, overlay.Top }));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(180) };
        timer.Tick += (_, _) => { timer.Stop(); overlay.Close(); app.Shutdown(); };
        overlay.Show(); overlay.ShowInTaskbar = true;
        // Expose the same window to desktop test tools, which omit tool windows from their inventory.
        SetWindowLong(new WindowInteropHelper(overlay).Handle, -20, GetWindowLong(new WindowInteropHelper(overlay).Handle, -20) & ~0x80);
        overlay.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { timer.Stop(); overlay.Close(); app.Shutdown(); } };
        timer.Start(); app.Run(); return 0;
    }
    internal static void CheckUi(string[] args)
    {
        var app = new App(); app.InitializeComponent();

        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string directory = Path.Combine(Path.GetTempPath(), "HardwareMonitor.UiTests", Guid.NewGuid().ToString("N"));
        var main = new MainWindow(new SettingsStore(Path.Combine(directory, "settings.json")), startSampler: false);
        app.MainWindow = main;
        new WindowInteropHelper(main).EnsureHandle();
        main.Opacity = 0;
        main.IsHitTestVisible = false;
        main.ShowActivated = false;
        main.Show();
        var snapshot = new HardwareSnapshot(new[]
        {
            Reading("CPU Package", SensorType.Temperature, null), Reading("CPU Total", SensorType.Load, 12),
            Reading("CPU Core #1 Thread #1", SensorType.Load, 8), Reading("CPU Core #1 Thread #2", SensorType.Load, 16),
            Reading("CPU Core #1", SensorType.Clock, 4200),
            Reading("Core (Tctl/Tdie)", SensorType.Temperature, 55, "/amdcpu/0"),
            Reading("GPU Core", SensorType.Temperature, 49, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU Core", SensorType.Load, 35, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU PPT", SensorType.Power, 25, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("GPU Memory Used", SensorType.SmallData, 1234, "/gpuamd/0", HardwareType.GpuAmd),
            Reading("Memory", SensorType.Load, 33, "/ram", HardwareType.Memory),
            Reading("Memory Used", SensorType.Data, 5.3f, "/ram", HardwareType.Memory),
            Reading("System", SensorType.Temperature, 35, "/board", HardwareType.Motherboard),
            Reading("Fan #1", SensorType.Fan, 1004, "/board", HardwareType.Motherboard)
        }, "Проверка интерфейса: синтетические данные", DateTime.Now, Array.Empty<string>());
        typeof(MainWindow).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { snapshot });
        Assert(main.HardwareGroups.Count == 5, "WPF receives CPU, AMD CPU, GPU, RAM and board groups");
        Settle(main);
        foreach (var expander in Descendants<Expander>(main)) expander.IsExpanded = true;
        Settle(main);
        var fanText = Descendants<TextBlock>(main).Single(t => t.Text == "1004 об/мин");
        Assert(((SolidColorBrush)fanText.Foreground).Color == ((SolidColorBrush)main.FindResource("TextFillColorPrimaryBrush")).Color,
            "Nested RPM digits and unit use readable theme foreground");
        var cpu = main.HardwareGroups[0];
        Assert(cpu.Sensors.First().Value == "—", "WPF displays missing temperature");
        cpu.Sensors[0].IsSelected = true;
        ((CheckBox)main.FindName("OverlayCheckbox")).IsChecked = true;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        overlay.Opacity = 0; overlay.IsHitTestVisible = false;
        ((CheckBox)main.FindName("LockPositionCheckbox")).IsChecked = true;
        Assert(MainWindow.IsOverlayLocked && (GetWindowLong(new WindowInteropHelper(overlay).Handle, -20) & 0x20) != 0,
            "Lock makes overlay click-through");
        ((CheckBox)main.FindName("LockPositionCheckbox")).IsChecked = false;
        Assert(!MainWindow.IsOverlayLocked && (GetWindowLong(new WindowInteropHelper(overlay).Handle, -20) & 0x20) == 0,
            "Unlock restores mouse input");
        overlay.RestorePosition(100000, 100000);
        Assert(overlay.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            overlay.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight, "Restore disconnected screen position");
        Assert(((System.Collections.ICollection)((ItemsControl)overlay.FindName("OverlayItems")).ItemsSource).Count == 1, "Overlay list binding");
        cpu.SubGroups[0].Sensors[0].IsSelected = true;
        Assert(((System.Collections.ICollection)((ItemsControl)overlay.FindName("OverlayItems")).ItemsSource).Count == 2, "Overlay adds selected core");
        cpu.Sensors[0].IsSelected = false;
        Assert(((System.Collections.ICollection)((ItemsControl)overlay.FindName("OverlayItems")).ItemsSource).Count == 2, "Overlay keeps header when only core is selected");
        var diagnostics = new DiagnosticsWindow(); diagnostics.Update(snapshot, null);
        new WindowInteropHelper(diagnostics).EnsureHandle();
        Assert(((DataGrid)diagnostics.FindName("SensorGrid")).Items.Count == snapshot.Sensors.Count, "Diagnostics includes every raw sensor");
        using (var manager = new HotkeyManager(new WindowInteropHelper(main).Handle))
        using (var competitor = new HotkeyManager(new WindowInteropHelper(diagnostics).Handle))
        {
            var candidates = Enumerable.Range(112, 12).Select(key => new HotkeySettings(7, (uint)key, "test")).ToList();
            var initial = candidates.FirstOrDefault(key => manager.TrySet(1, key));
            Assert(initial != null, "Real Win32 hotkey registration");
            var busy = candidates.FirstOrDefault(key => key != initial && competitor.TrySet(1, key));
            Assert(busy != null && !manager.TrySet(1, busy), "Real Win32 collision rejected");
            Assert(!competitor.TrySet(2, initial!), "Previous real hotkey still reserved after failure");
        }
        typeof(MainWindow).GetMethod("ResetRanges_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
        Assert(main.HardwareGroups.SelectMany(g => g.AllSensors).All(s => s.Minimum == null && s.Maximum == null), "UI reset clears all ranges");
        string? renderDirectory = Argument(args, "--render");
        if (renderDirectory != null)
        {
            Directory.CreateDirectory(renderDirectory);
            Render(main, Path.Combine(renderDirectory, "main.png"), 520, 720);
            Render(diagnostics, Path.Combine(renderDirectory, "diagnostics.png"), 920, 600);
            Render(overlay, Path.Combine(renderDirectory, "overlay.png"), 380, 100);
        }
        typeof(MainWindow).GetMethod("SetOverlayColor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { "#70D6FF" });
        ((CheckBox)main.FindName("OutlineCheckbox")).IsChecked = false;
        ((Slider)main.FindName("TransparencySlider")).Value = 20;
        Assert(overlay.Opacity == 0 && overlay.TextAlpha == .8 && !overlay.TextOutline, "Opacity affects text only and applies live");
        CheckRenderedOverlay(main, overlay);
        CheckPicker(main, overlay, app);
        ((Slider)main.FindName("TextSizeSlider")).Value = 18;
        Assert(Math.Abs(overlay.TextScale - 18d / 13) < .001, "Text size updates open overlay");
        ((Wpf.Ui.Controls.ToggleSwitch)main.FindName("ThemeToggle")).IsChecked = false;
        Assert(Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Light, "Light theme toggle");
        Assert(((SolidColorBrush)fanText.Foreground).Color == ((SolidColorBrush)main.FindResource("TextFillColorPrimaryBrush")).Color,
            "Nested RPM foreground follows light theme too");
        Assert(((SolidColorBrush)overlay.TextBrush).Color == Color.FromRgb(112, 214, 255), "Theme change preserves overlay color");
        ((CheckBox)main.FindName("OverlayCheckbox")).IsChecked = false;
        Assert(typeof(MainWindow).GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main) == null, "Disable closes live overlay");
        ((CheckBox)main.FindName("OverlayCheckbox")).IsChecked = true;
        var restarted = (OverlayWindow)typeof(MainWindow).GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        restarted.Opacity = 0; restarted.IsHitTestVisible = false;
        Assert(((SolidColorBrush)restarted.TextBrush).Color == Color.FromRgb(112, 214, 255) && restarted.TextAlpha == .8 && Math.Abs(restarted.TextScale - 18d / 13) < .001,
            "Re-enabling overlay preserves color alpha and size");
        Assert(((SolidColorBrush)restarted.DragSurface).Color.A == 1, "Unlocked window captures blank pixels with minimal native alpha");
        restarted.SetLocked(true);
        Assert(((SolidColorBrush)restarted.DragSurface).Color.A == 0, "Locked window removes native drag surface");
        ((CheckBox)main.FindName("OverlayCheckbox")).IsChecked = false;
        diagnostics.Close();
        main.StopForShutdown(); main.Close();
        Assert(File.Exists(Path.Combine(directory, "settings.json")), "Exit flushes settings");
        var restored = new SettingsStore(Path.Combine(directory, "settings.json")).Load(out var error);
        Assert(error == null && restored.SelectedSensors.Count == 1, "UI sensor selection persisted on exit");
        var reopened = new MainWindow(new SettingsStore(Path.Combine(directory, "settings.json")), startSampler: false);
        app.MainWindow = reopened;
        typeof(MainWindow).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reopened, new object[] { snapshot });
        Assert(reopened.HardwareGroups.SelectMany(g => g.AllSensors).Count(s => s.IsSelected) == 1, "Saved selection restored in WPF");
        Assert(((Wpf.Ui.Controls.ToggleSwitch)reopened.FindName("ThemeToggle")).IsChecked == false &&
            ((TextBlock)reopened.FindName("ColorHex")).Text == "#70D6FF" &&
            Math.Abs(((Slider)reopened.FindName("TransparencySlider")).Value - 20) < .001 &&
            ((CheckBox)reopened.FindName("OutlineCheckbox")).IsChecked == false, "Saved theme and appearance restored in WPF");
        Assert(((Slider)reopened.FindName("TextSizeSlider")).Value == 18, "Text size restored in real WPF window");
        reopened.StopForShutdown(); reopened.Close(); app.Shutdown();
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int style);

    private static void Render(Window window, string path, int width, int height)
    {
        Settle(window);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(window.TryFindResource("ApplicationBackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Settle(Window window)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Screenshots(string[] args)
    {
        string output = Path.GetFullPath(Argument(args, "--output") ?? "Screenshots");
        Directory.CreateDirectory(output);
        string directory = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Screenshots", Guid.NewGuid().ToString("N"));
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var main = new MainWindow(new SettingsStore(Path.Combine(directory, "settings.json")), startSampler: false)
        { Opacity = 0, ShowActivated = false, IsHitTestVisible = false };
        app.MainWindow = main; main.Show();
        var sensors = new List<SensorReading>();
        void Add(string name, SensorType type, float value, string root, HardwareType hardwareType, string model)
            => sensors.Add(Reading(name, type, value, root, hardwareType) with { RootName = model });
        string cpu = "/amdcpu/0", gpu = "/gpunvidia/0", disk = "/nvme/0";
        Add("Core (Tctl/Tdie)", SensorType.Temperature, 57, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
        Add("CPU Total", SensorType.Load, 9, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
        Add("Package", SensorType.Power, 18, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
        Add("Cores (Average)", SensorType.Clock, 4500, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
        for (int i = 1; i <= 8; i++)
        {
            Add($"CPU Core #{i} Thread #1", SensorType.Load, 5 + i, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
            Add($"CPU Core #{i} Thread #2", SensorType.Load, 3 + i, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
            Add($"Core #{i}", SensorType.Clock, 4000 + i * 100, cpu, HardwareType.Cpu, "AMD Ryzen 7 7840HS");
        }
        Add("GPU Core", SensorType.Temperature, 49, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("GPU Core", SensorType.Load, 24, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("GPU Core", SensorType.Clock, 2100, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("GPU Memory", SensorType.Clock, 8000, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("GPU Power", SensorType.Power, 32, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("GPU Memory Used", SensorType.SmallData, 3174, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("GPU Hot Spot", SensorType.Temperature, 61, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        Add("Memory", SensorType.Load, 34, "/ram", HardwareType.Memory, "Оперативная память");
        Add("Memory Used", SensorType.Data, 5.4f, "/ram", HardwareType.Memory, "Оперативная память");
        Add("Memory Available", SensorType.Data, 10.6f, "/ram", HardwareType.Memory, "Оперативная память");
        Add("Composite Temperature", SensorType.Temperature, 37, disk, HardwareType.Storage, "NVMe SSD 1 TB");
        Add("Used Space", SensorType.Load, 42, disk, HardwareType.Storage, "NVMe SSD 1 TB");
        Add("Free Space", SensorType.Data, 182.4f, disk, HardwareType.Storage, "NVMe SSD 1 TB");
        Add("GPU Fan", SensorType.Fan, 1200, gpu, HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        var snapshot = new HardwareSnapshot(sensors, "Демонстрационные данные", DateTime.Now, Array.Empty<string>());
        typeof(MainWindow).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { snapshot });
        foreach (var sensor in main.HardwareGroups.SelectMany(g => g.Sensors)) sensor.IsSelected = true;
        Render(main, Path.Combine(output, "main-dark.png"), 540, 760);
        ((Expander)main.FindName("AppearanceExpander")).IsExpanded = true;
        Render(main, Path.Combine(output, "appearance.png"), 540, 760);
        Assert(((SolidColorBrush)((System.Windows.Shapes.Rectangle)main.FindName("CurrentColorPreview")).Fill).Color == (Color)ColorConverter.ConvertFromString("#F4F6FA"), "Screenshot color remains at default");
        ((Expander)main.FindName("AppearanceExpander")).IsExpanded = false;
        ((Wpf.Ui.Controls.ToggleSwitch)main.FindName("ThemeToggle")).IsChecked = false;
        Render(main, Path.Combine(output, "main-light.png"), 540, 760);
        var palette = new ColorPickerWindow(ColorSpectrum.FromHsv(194, .75, .9), .9, true) { Owner = main, Opacity = 0, IsHitTestVisible = false };
        palette.Show(); Render(palette, Path.Combine(output, "color-picker.png"), 390, 440); palette.Close();
        using (var renderer = new ScreenshotOverlay(main)) renderer.Save(Path.Combine(output, "overlay.png"));
        foreach (var sensor in main.HardwareGroups.SelectMany(g => g.AllSensors).Where(s => s.CoreNumber <= 2 || s.Kind is MetricKind.GpuHotSpot or MetricKind.FanSpeed)) sensor.IsSelected = true;
        using (var renderer = new ScreenshotOverlay(main)) renderer.Save(Path.Combine(output, "overlay-details.png"));
        main.StopForShutdown(); main.Close(); app.Shutdown();
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
        Console.WriteLine("Screenshots saved: " + output);
        return 0;
    }

    private sealed class ScreenshotOverlay : IDisposable
    {
        private readonly OverlayWindow _window;
        public ScreenshotOverlay(MainWindow main)
        {
            _window = new OverlayWindow { Opacity = 0, ShowActivated = false, IsHitTestVisible = false };
            _window.SetRows(OverlayRows.Build(main.HardwareGroups));
            _window.Show();
        }
        public void Save(string path)
        {
            Settle(_window);
            var content = (FrameworkElement)_window.Content;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = content.DesiredSize;
            content.Arrange(new Rect(new Point(0, 0), size)); content.UpdateLayout();
            var widget = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
            widget.Render(content);
            int height = (int)Math.Ceiling(size.Height) + 50;
            int sceneWidth = Math.Max(550, (int)Math.Ceiling(size.Width) + 40);
            var scene = new DrawingVisual();
            using (var drawing = scene.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 239, 245)), null, new Rect(0, 0, sceneWidth, height));
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 31, 39)), null, new Rect(0, height, sceneWidth, height));
                drawing.DrawImage(widget, new Rect(20, 24, size.Width, size.Height));
                drawing.DrawImage(widget, new Rect(20, height + 24, size.Width, size.Height));
            }
            var bitmap = new RenderTargetBitmap(sceneWidth, height * 2, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path); encoder.Save(stream);
        }
        public void Dispose() => _window.Close();
    }

    private static async Task<int> Probe(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        var samples = new List<HardwareSnapshot>();
        await Task.Run(() => new HardwareSampler().RunAsync(snapshot =>
        {
            samples.Add(snapshot);
            if (samples.Count >= 3 || snapshot.Errors.Count > 0) cancel.Cancel();
        }, cancel.Token));
        var last = samples.Last();
        var report = new
        {
            last.Status, last.Errors,
            CpuSensors = last.Sensors.Where(s => s.RootType == HardwareType.Cpu).Select(s => new { s.Name, Type = s.Type.ToString(), s.Identifier, s.Value }),
            Visible = SensorCatalog.Build(last.Sensors).Select(s => new { s.Name, Kind = s.Kind.ToString(), s.Reading.Identifier, s.Reading.Value }),
            Samples = samples.Count
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        string? output = Argument(args, "--output");
        if (output != null)
        {
            var fullPath = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
        }
        else Console.WriteLine(json);
        return 0;
    }

    private static int Smoke()
    {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string directory = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Smoke", Guid.NewGuid().ToString("N"));
        var main = new MainWindow(new SettingsStore(Path.Combine(directory, "settings.json")))
        {
            Opacity = 0, ShowActivated = false, IsHitTestVisible = false
        };
        app.MainWindow = main;
        Exception? failure = null;
        main.Loaded += async (_, _) =>
        {
            try
            {
                var snapshotField = typeof(MainWindow).GetField("_latestSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var deadline = DateTime.UtcNow.AddSeconds(15);
                HardwareSnapshot? first = null, last = null;
                int observed = 0;
                while (DateTime.UtcNow < deadline && observed < 3)
                {
                    await Task.Delay(50);
                    var current = snapshotField.GetValue(main) as HardwareSnapshot;
                    if (current != null && current != last)
                    {
                        first ??= current;
                        last = current;
                        observed++;
                    }
                }
                Assert(observed == 3 && first != null && last != null, "Live worker publishes repeated snapshots while UI stays responsive");
                Assert(main.HardwareGroups.Count > 0 && last!.Errors.Count == 0, "Live hardware displayed without update errors");
                main.Close();
                Assert(!main.IsVisible, "Close hides main window to tray without stopping monitoring");
                var icon = (System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                Assert(icon.Visible, "Tray icon visible after close");
                typeof(MainWindow).GetMethod("ShowMainWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                Assert(main.IsVisible && !icon.Visible, "Restore from tray");
            }
            catch (Exception ex) { failure = ex; }
            finally { await main.ExitAsync(); }
        };
        main.Show();
        app.Run();
        var poll = (Task?)typeof(MainWindow).GetField("_pollTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
        Assert(poll?.IsCompleted == true, "Exit awaits worker completion");
        Assert(File.Exists(Path.Combine(directory, "settings.json")), "Live exit persists settings");
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
        if (failure != null) throw failure;
        Console.WriteLine($"PASS: {_checks} live checks");
        return 0;
    }
}

public class LibraryProxy : DispatchProxy
{
    public Func<MethodInfo, object?> Handler { get; set; } = _ => null;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod == null ? null : Handler(targetMethod);
}
