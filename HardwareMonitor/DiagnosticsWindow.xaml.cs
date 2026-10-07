using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;
using System.Collections.ObjectModel;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

namespace HardwareMonitor;

public partial class DiagnosticsWindow : FluentWindow
{
    private readonly ObservableCollection<DiagnosticSensor> _rows = new();
    private readonly Dictionary<string, DiagnosticSensor> _byId = new(StringComparer.Ordinal);

    public DiagnosticsWindow()
    {
        InitializeComponent();
        ApplicationThemeManager.Apply(this);
        SensorGrid.ItemsSource = _rows;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackgroundManager.UpdateBackground(this, ApplicationThemeManager.GetAppTheme(), WindowBackdropType.Mica);
    }

    public void Update(HardwareSnapshot snapshot, string? settingsError)
    {
        var problems = snapshot.Errors.ToList();
        if (settingsError != null) problems.Add(settingsError);
        bool cpuTemperatureUnavailable = snapshot.Sensors.Any(s => s.RootType == HardwareType.Cpu) &&
            !snapshot.Sensors.Any(s => s.RootType == HardwareType.Cpu && s.Type == SensorType.Temperature && s.Value.HasValue);
        StatusText.Text = snapshot.Status + $"\nОбновлено: {snapshot.Timestamp:HH:mm:ss}" +
            (cpuTemperatureUnavailable ? "\nТемпература процессора не читается. Наличие драйвера в системе само по себе не подтверждает доступность его модулей." : "") +
            (problems.Count > 0 ? "\n" + string.Join("\n", problems) : "");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reading in snapshot.Sensors)
        {
            seen.Add(reading.Identifier);
            if (!_byId.TryGetValue(reading.Identifier, out var row))
            {
                row = new DiagnosticSensor(reading);
                _byId.Add(reading.Identifier, row);
                _rows.Add(row);
            }
            row.Value = SensorFormatting.Format(reading.Value, reading.Type);
        }
        foreach (var id in _byId.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _rows.Remove(_byId[id]);
            _byId.Remove(id);
        }
    }
}

public partial class DiagnosticSensor : ObservableObject
{
    public string Identifier { get; }
    public string Name { get; }
    public string HardwareName { get; }
    public string TypeName { get; }
    [ObservableProperty] private string _value = "—";

    public DiagnosticSensor(SensorReading reading)
    {
        Identifier = reading.Identifier;
        Name = reading.Name;
        HardwareName = reading.HardwareName;
        TypeName = reading.Type switch
        {
            SensorType.Temperature => "Температура", SensorType.Load => "Загрузка",
            SensorType.Clock => "Частота", SensorType.Power => "Мощность",
            SensorType.Voltage => "Напряжение", SensorType.Fan => "Обороты",
            SensorType.Data or SensorType.SmallData => "Объём", SensorType.Control => "Управление",
            SensorType.Factor => "Коэффициент", SensorType.Level => "Уровень",
            SensorType.Throughput => "Скорость", SensorType.Energy => "Энергия", SensorType.Frequency => "Частота", _ => "Другой"
        };
    }
}
