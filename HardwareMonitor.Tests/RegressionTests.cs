using HardwareMonitor;
using NUnit.Framework;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Tests;

[TestFixture, NonParallelizable]
public sealed class RegressionTests
{
    [Test] public void FormattingAndSessionRanges() => Program.CheckFormattingAndRanges();
    [Test] public void ColorConversionsAndTemperatureWarnings() => Program.CheckColorsAndAlerts();
    [Test] public void IntelAndAmdCpuSelection() => Program.CheckCpuCatalog();
    [Test] public void GpuSensorsAndDedicatedMemory() => Program.CheckGpuCatalog();
    [Test] public void RamStorageAndBoardCatalog() => Program.CheckCompactCatalog();
    [Test] public void RecursiveHardwareUpdates() => Program.CheckNestedHardware();
    [Test] public void HotkeyReplacementAndDisposal() => Program.CheckHotkeys();
    [Test] public void SettingsPersistenceAndMigration() => Program.CheckSettings();
    [Test] public void OverlaySelectionAndCoreDetails() => Program.CheckOverlayRows();
    [Test] public void DeviceBlocksAndSecondaryMetrics() => Program.CheckDeviceBlocks();

    [Test]
    public void AmdLogicalLoadsUseWindowsCoreAndThreadTopology()
    {
        var cores = Enumerable.Range(0, 8).Select(c => (IReadOnlyList<GroupAffinity>)new[]
            { GroupAffinity.Single(0, c * 2), GroupAffinity.Single(0, c * 2 + 1) }).ToArray();
        // Sensor enumeration order must not determine the core/thread association.
        var loads = Enumerable.Range(0, 16).Reverse().Select(t => (t + 2, GroupAffinity.Single(0, t))).ToArray();
        var map = CpuLoadTopology.Map(cores, loads);
        Assert.That(map.Count, Is.EqualTo(16));
        var readings = Enumerable.Range(0, 16).Select(t => new SensorReading(
            $"/amdcpu/0/load/{t + 2}", $"CPU Core #{t + 1}", SensorType.Load, t,
            "/amdcpu/0", "Ryzen 7 7840HS", "/amdcpu/0", "Ryzen 7 7840HS", HardwareType.Cpu)
            { PhysicalCoreNumber = map[t + 2].Core, PhysicalThreadNumber = map[t + 2].Thread }).ToArray();
        var catalog = SensorCatalog.Build(readings);
        Assert.That(catalog.Count, Is.EqualTo(16));
        for (int t = 0; t < 16; t++)
        {
            var sensor = catalog.Single(s => s.Reading.Identifier == readings[t].Identifier);
            Assert.That(sensor.Name, Is.EqualTo($"Ядро {t / 2 + 1} · поток {t % 2 + 1}"));
            Assert.That(sensor.Key, Is.EqualTo($"/amdcpu/0::CoreLoad:{t + 1}:"), "Keep saved selections");
            Assert.That(sensor.Reading.Name, Is.EqualTo($"CPU Core #{t + 1}"), "Keep raw diagnostics");
        }
        var cpu = new HardwareItem { Key = "/amdcpu/0", Name = "Процессор", Type = HardwareType.Cpu };
        foreach (var definition in catalog)
            cpu.Sensors.Add(new SensorItem { Key = definition.Key, Name = definition.Name, Kind = definition.Kind,
                CoreNumber = definition.CoreNumber, ThreadNumber = definition.ThreadNumber, IsSelected = true });
        var rows = OverlayRows.Build(new[] { cpu });
        Assert.That(rows.Count, Is.EqualTo(9), "CPU header followed by eight physical core rows");
        Assert.That(rows[0].Name, Is.EqualTo("ЦП"));
        foreach (var row in rows.Skip(1))
            Assert.That(row.Metrics.Select(m => m.Label), Is.EqualTo(new[] { "П1: ", "П2: " }), "Both loads stay under their physical core");
        Assert.That(CpuLoadTopology.Map(cores, loads[..15]), Is.Empty, "Incomplete mapping must not guess");
        var otherGroup = loads.Select(l => (l.Item1, GroupAffinity.Single(1, l.Item1 - 2))).ToArray();
        Assert.That(CpuLoadTopology.Map(cores, otherGroup), Is.Empty, "Processor group is part of the identity");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void WpfAppearanceLayoutAndSettings() => Program.CheckUi(Array.Empty<string>());

    [Test]
    public async Task PollingStopsAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int captured = 0, published = 0;
        var worker = Task.Run(() => HardwareSampler.PollAsync(() =>
        {
            Interlocked.Increment(ref captured);
            return new HardwareSnapshot(Array.Empty<SensorReading>(), "Моделируемый опрос", DateTime.Now, Array.Empty<string>());
        }, _ =>
        {
            if (Interlocked.Increment(ref published) == 3) cancellation.Cancel();
        }, cancellation.Token, TimeSpan.FromMilliseconds(10)));
        try { await worker.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Assert.That(worker.IsCompleted, Is.True);
        Assert.That(captured, Is.EqualTo(3));
        await Task.Delay(30);
        Assert.That(published, Is.EqualTo(3), "После завершения новые снимки не публикуются");
    }
}
