using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace HardwareMonitor;

// Only the worker owns library objects; the UI receives value snapshots.
public sealed record SensorReading(string Identifier, string Name, SensorType Type, float? Value,
    string HardwareId, string HardwareName, string RootId, string RootName, HardwareType RootType);

public sealed record HardwareSnapshot(IReadOnlyList<SensorReading> Sensors, string Status,
    DateTime Timestamp, IReadOnlyList<string> Errors);

public sealed class HardwareSampler
{
    public async Task RunAsync(Action<HardwareSnapshot> publish, CancellationToken cancellationToken)
    {
        var computer = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
            IsStorageEnabled = true, IsMotherboardEnabled = true
        };
        try
        {
            computer.Open();
            var (status, cpuAccess) = DriverStatus();
            await PollAsync(() => Capture(computer.Hardware, status, cpuAccess), publish, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            publish(new HardwareSnapshot(Array.Empty<SensorReading>(), "Не удалось запустить опрос оборудования.",
                DateTime.Now, new[] { ex.Message }));
        }
        finally
        {
            computer.Close();
        }
    }

    internal static async Task PollAsync(Func<HardwareSnapshot> capture, Action<HardwareSnapshot> publish,
        CancellationToken cancellationToken, TimeSpan? interval = null)
    {
        using var timer = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(1));
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            publish(capture());
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    internal static HardwareSnapshot Capture(IEnumerable<IHardware> hardware, string status, bool cpuAccess = true)
    {
        var readings = new List<SensorReading>();
        var errors = new List<string>();
        foreach (var root in hardware) ReadHardware(root, root, readings, errors);
        if (!cpuAccess)
        {
            // Some library backends return zero for a failed MSR read. Software CPU load remains valid.
            for (int i = 0; i < readings.Count; i++)
                if (readings[i].RootType == HardwareType.Cpu && readings[i].Type is
                    SensorType.Temperature or SensorType.Clock or SensorType.Power or SensorType.Voltage or SensorType.Factor)
                    readings[i] = readings[i] with { Value = null };
        }
        return new HardwareSnapshot(readings, status, DateTime.Now, errors);
    }

    private static void ReadHardware(IHardware hardware, IHardware root,
        List<SensorReading> readings, List<string> errors)
    {
        bool updated = true;
        try { hardware.Update(); }
        catch (Exception ex)
        {
            updated = false;
            errors.Add($"{hardware.Name}: {ex.Message}");
        }
        foreach (var sensor in hardware.Sensors)
            readings.Add(new SensorReading(sensor.Identifier.ToString(), sensor.Name, sensor.SensorType,
                updated ? SensorFormatting.ValidValue(sensor.Value, sensor.SensorType) : null,
                hardware.Identifier.ToString(), hardware.Name, root.Identifier.ToString(), root.Name, root.HardwareType));
        // An unavailable parent must not prevent independent child devices from updating.
        foreach (var child in hardware.SubHardware)
            ReadHardware(child, root, readings, errors);
    }

    private static (string Status, bool CpuAccess) DriverStatus()
    {
        using var identity = WindowsIdentity.GetCurrent();
        bool administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        string version = typeof(Computer).Assembly.GetName().Version?.ToString() ?? "неизвестна";
        string pawnVersion = PawnIo.Version?.ToString() ?? "установка не распознана";
        using var handle = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        string access = handle.IsInvalid ? "недоступен: " + new Win32Exception(Marshal.GetLastWin32Error()).Message : "устройство доступно";
        // Installation metadata alone does not prove the module can read this CPU.
        string status = $"LibreHardwareMonitor: {version}\nПрава администратора: {(administrator ? "есть" : "нет")}\n" +
               $"PawnIO: {pawnVersion}.\n" +
               $"Доступ к драйверу: {access}.\n" +
               "Если температура и частоты процессора недоступны: проверьте запуск от администратора и установку PawnIO " +
               "из официального источника. После установки перезапустите приложение.";
        return (status, !handle.IsInvalid);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
}
