using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor;

public class HardwareItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Subtitle { get; init; } = "";
    public HardwareType Type { get; init; }
    public ObservableCollection<SensorItem> Sensors { get; } = new();
    public ObservableCollection<HardwareItem> SubGroups { get; } = new();
    public IEnumerable<SensorItem> AllSensors => Sensors.Concat(SubGroups.SelectMany(group => group.AllSensors));
    private bool _changingSelection;
    public bool? IsSelected
    {
        get
        {
            if (Sensors.Count == 0 || Sensors.All(s => !s.IsSelected)) return false;
            return Sensors.All(s => s.IsSelected) ? true : null;
        }
        set
        {
            if (value == null) return;
            _changingSelection = true;
            try { foreach (var sensor in Sensors) sensor.IsSelected = value.Value; }
            finally { _changingSelection = false; }
            OnPropertyChanged();
        }
    }

    public HardwareItem()
    {
        Sensors.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
                foreach (SensorItem sensor in e.OldItems) sensor.PropertyChanged -= SensorChanged;
            if (e.NewItems != null)
                foreach (SensorItem sensor in e.NewItems) sensor.PropertyChanged += SensorChanged;
            OnPropertyChanged(nameof(IsSelected));
        };
    }

    private void SensorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_changingSelection && e.PropertyName == nameof(SensorItem.IsSelected)) OnPropertyChanged(nameof(IsSelected));
    }
}
