using HardwareMonitor;
using NUnit.Framework;

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
