using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Cpu;
using System.Runtime.InteropServices;

namespace HardwareMonitor;

// LHM 0.9.6 can report Ryzen 7840HS SMT threads as separate physical cores.
// Resolve load sensors by their actual Windows affinities, never by adjacent names.
internal static class CpuLoadTopology
{
    private static readonly Lazy<IReadOnlyList<IReadOnlyList<GroupAffinity>>> WindowsCores = new(ReadWindowsCores);

    internal static Dictionary<int, (int Core, int Thread)> Map(
        IReadOnlyList<IReadOnlyList<GroupAffinity>> cores, IReadOnlyList<(int LoadIndex, GroupAffinity Affinity)> loads)
    {
        var result = new Dictionary<int, (int Core, int Thread)>();
        var affinities = loads.Select(l => l.Affinity).ToHashSet();
        // Use only complete physical cores belonging to this CPU, including processor group IDs.
        var owned = cores.Where(c => c.Count > 0 && c.All(affinities.Contains)).ToList();
        for (int core = 0; core < owned.Count; core++)
            for (int thread = 0; thread < owned[core].Count; thread++)
            {
                var matches = loads.Where(l => l.Affinity == owned[core][thread]).ToArray();
                if (matches.Length != 1 || !result.TryAdd(matches[0].LoadIndex, (core + 1, thread + 1))) return new();
            }
        return result.Count == loads.Count ? result : new();
    }

    internal static void Correct(IEnumerable<IHardware> hardware, List<SensorReading> readings)
    {
        foreach (var cpu in hardware.OfType<GenericCpu>().Where(c => c.Identifier.ToString().StartsWith("/amdcpu/", StringComparison.Ordinal)))
        {
            // Leave correctly labelled SMT and non-SMT backends unchanged.
            if (cpu.CpuId.Any(c => c.Length != 1)) continue;
            var loads = cpu.CpuId.SelectMany(c => c).Select(t => (LoadIndex: t.Thread + 2, t.Affinity)).ToArray();
            var map = Map(WindowsCores.Value, loads);
            int coreCount = map.Values.Select(v => v.Core).Distinct().Count();
            if (coreCount == 0 || coreCount >= cpu.CpuId.Length) continue;
            string root = cpu.Identifier.ToString();
            // Confirm the physical numbering also agrees with the independent AMD clock sensors.
            if (Enumerable.Range(1, coreCount).Any(n => !readings.Any(r => r.RootId == root && r.Type == SensorType.Clock && r.Name == $"Core #{n}")) ||
                readings.Any(r => r.RootId == root && r.Type == SensorType.Clock && r.Name == $"Core #{coreCount + 1}")) continue;
            for (int i = 0; i < readings.Count; i++)
            {
                var reading = readings[i];
                if (reading.RootId != root || reading.Type != SensorType.Load) continue;
                string prefix = root + "/load/";
                if (reading.Identifier.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(reading.Identifier[prefix.Length..], out int index) && map.TryGetValue(index, out var position))
                    readings[i] = reading with { PhysicalCoreNumber = position.Core, PhysicalThreadNumber = position.Thread };
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<GroupAffinity>> ReadWindowsCores()
    {
        var result = new List<IReadOnlyList<GroupAffinity>>();
        if (IntPtr.Size != 8) return result;
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length == 0) return result;
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return result;
            for (int offset = 0; offset < length;)
            {
                if (length - offset < 32) return Array.Empty<IReadOnlyList<GroupAffinity>>();
                var record = IntPtr.Add(buffer, offset);
                int size = Marshal.ReadInt32(record, 4);
                int groups = (ushort)Marshal.ReadInt16(record, 30);
                if (size < 32 + groups * 16 || size > length - offset) return Array.Empty<IReadOnlyList<GroupAffinity>>();
                var threads = new List<GroupAffinity>();
                for (int g = 0; g < groups; g++)
                {
                    ulong mask = (ulong)Marshal.ReadInt64(record, 32 + g * 16);
                    ushort group = (ushort)Marshal.ReadInt16(record, 40 + g * 16);
                    for (int bit = 0; bit < 64; bit++)
                        if ((mask & (1UL << bit)) != 0) threads.Add(GroupAffinity.Single(group, bit));
                }
                result.Add(threads);
                offset += size;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
}
