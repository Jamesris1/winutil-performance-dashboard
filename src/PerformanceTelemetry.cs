using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

// Binary units match saved before/after captures: MiB = 1,048,576 bytes;
// GiB = 1,073,741,824 bytes. Existing GB/MBps field names remain compatible.
// Null means unavailable or awaiting the first interval, never an invented zero.
public class TelemetrySample
{
    public string Timestamp;
    public double? CpuPercent, MemoryUsedGB, MemoryTotalGB, MemoryPercent;
    public double? DiskReadMBps, DiskWriteMBps, DiskActivePercent, DiskLatencyMs;
    public double? NetworkReceiveMBps, NetworkSendMBps;
    public List<StorageReading> Storage = new List<StorageReading>();
    public List<NetworkReading> Networks = new List<NetworkReading>();
    public GpuReading Gpu;
    public List<string> Notes = new List<string>();
}

public class StorageReading
{
    public string Name, Label, Type, Format, Status, Timestamp;
    public double? TotalGB, UsedGB, FreeGB, UsedPercent;
}

public class NetworkReading
{
    public string Name, Description, Type, Status;
    public double? ReceiveMBps, SendMBps, LinkMbps;
    public bool IsVirtual;
}

public class GpuReading
{
    public string Name, Timestamp, Status;
    public double? UtilizationPercent, MemoryUsedMB, MemoryTotalMB, MemoryPercent, TemperatureC, PowerWatts;
    public bool IsStale;
}

public sealed class PerformanceTelemetry : IDisposable
{
    private const double MB = 1048576.0;
    private const double GB = 1073741824.0;
    private readonly object sampleLock = new object();
    private readonly object cacheLock = new object();
    private readonly Dictionary<string, NetworkBaseline> networkBaseline = new Dictionary<string, NetworkBaseline>();
    private readonly CounterReader diskRead = new CounterReader("Disk Read Bytes/sec");
    private readonly CounterReader diskWrite = new CounterReader("Disk Write Bytes/sec");
    private readonly CounterReader diskIdle = new CounterReader("% Idle Time");
    private readonly CounterReader diskLatency = new CounterReader("Avg. Disk sec/Transfer");
    private bool cpuBaselineReady;
    private ulong previousIdle, previousKernel, previousUser;
    private volatile bool disposed;
    private bool gpuBusy, storageBusy;
    private DateTime gpuAttemptUtc = DateTime.MinValue, storageAttemptUtc = DateTime.MinValue;
    private GpuReading cachedGpu;
    private List<StorageReading> cachedStorage = new List<StorageReading>();
    private string storageStatus = "Storage capacities are loading.";
    private DateTime storageCapturedUtc = DateTime.MinValue;

    // Constructor deliberately does no GPU, disk, network, or counter I/O.
    public PerformanceTelemetry() { }

    public TelemetrySample Sample()
    {
        lock (sampleLock)
        {
            if (disposed) throw new ObjectDisposedException("PerformanceTelemetry");
            var sample = new TelemetrySample { Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
            ReadCpu(sample);
            ReadMemory(sample);
            ReadDiskActivity(sample);
            ReadNetworks(sample);
            QueueCacheRefresh();
            lock (cacheLock)
            {
                foreach (StorageReading reading in cachedStorage) sample.Storage.Add(CloneStorage(reading));
                if (!String.IsNullOrEmpty(storageStatus)) sample.Notes.Add(storageStatus);
                if (storageCapturedUtc != DateTime.MinValue && (DateTime.UtcNow - storageCapturedUtc).TotalSeconds > 15)
                    sample.Notes.Add("Storage capacities are cached and may be stale.");
                sample.Gpu = cachedGpu == null ? new GpuReading { Name = "NVIDIA GPU", Status = "Loading GPU telemetry." } : CloneGpu(cachedGpu);
            }
            DateTime gpuTime;
            if (DateTime.TryParse(sample.Gpu.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out gpuTime) &&
                (DateTime.UtcNow - gpuTime.ToUniversalTime()).TotalSeconds > 12)
            {
                sample.Gpu.IsStale = true;
                if (sample.Gpu.Status == "Live NVIDIA telemetry") sample.Gpu.Status = "Cached GPU telemetry; latest update is stale.";
            }
            sample.Notes.Add("Memory and storage use GiB; data rates use MiB/s; GPU memory uses MiB. Link speeds use decimal Mbps. Drive rows are logical volume capacities; disk activity is the PhysicalDisk aggregate.");
            return sample;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low, High;
        public ulong Value { get { return ((ulong)High << 32) | Low; } }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private void ReadCpu(TelemetrySample sample)
    {
        FileTime idle, kernel, user;
        if (!GetSystemTimes(out idle, out kernel, out user))
        {
            cpuBaselineReady = false;
            sample.Notes.Add("CPU usage unavailable (Win32 error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ").");
            return;
        }
        if (cpuBaselineReady && idle.Value >= previousIdle && kernel.Value >= previousKernel && user.Value >= previousUser)
        {
            ulong deltaIdle = idle.Value - previousIdle;
            ulong deltaTotal = (kernel.Value - previousKernel) + (user.Value - previousUser);
            if (deltaTotal > 0 && deltaTotal >= deltaIdle)
                sample.CpuPercent = Clamp(100.0 * (deltaTotal - deltaIdle) / deltaTotal, 0, 100);
            else sample.Notes.Add("CPU interval is too short or its baseline changed.");
        }
        else sample.Notes.Add("CPU usage is warming up for the first elapsed interval.");
        previousIdle = idle.Value; previousKernel = kernel.Value; previousUser = user.Value;
        cpuBaselineReady = true;
        if (Environment.ProcessorCount > 64)
            sample.Notes.Add("CPU usage covers the caller's processor group on systems with more than 64 logical processors.");
    }

    private void ReadMemory(TelemetrySample sample)
    {
        var status = new MemoryStatus();
        status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0 || status.AvailPhys > status.TotalPhys)
        {
            sample.Notes.Add("Physical memory usage is unavailable.");
            return;
        }
        ulong used = status.TotalPhys - status.AvailPhys;
        sample.MemoryTotalGB = status.TotalPhys / GB;
        sample.MemoryUsedGB = used / GB;
        sample.MemoryPercent = Clamp(100.0 * used / status.TotalPhys, 0, 100);
    }

    private void ReadDiskActivity(TelemetrySample sample)
    {
        sample.DiskReadMBps = Scale(diskRead.Read(), 1.0 / MB);
        sample.DiskWriteMBps = Scale(diskWrite.Read(), 1.0 / MB);
        double? idle = diskIdle.Read();
        sample.DiskActivePercent = idle.HasValue ? (double?)Clamp(100.0 - idle.Value, 0, 100) : null;
        sample.DiskLatencyMs = Scale(diskLatency.Read(), 1000);
        var problems = new List<string>();
        foreach (CounterReader reader in new CounterReader[] { diskRead, diskWrite, diskIdle, diskLatency })
            if (!String.IsNullOrEmpty(reader.Status) && !problems.Contains(reader.Status)) problems.Add(reader.Status);
        foreach (string problem in problems) sample.Notes.Add(problem);
    }

    private sealed class NetworkBaseline
    {
        public long Received, Sent, Tick;
    }

    private void ReadNetworks(TelemetrySample sample)
    {
        var seen = new HashSet<string>();
        int usableRates = 0;
        double receivedTotal = 0, sentTotal = 0;
        bool hasVirtual = false, hadFailure = false;
        NetworkInterface[] interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { sample.Notes.Add("Network interfaces are unavailable."); return; }
        foreach (NetworkInterface adapter in interfaces)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            seen.Add(adapter.Id);
            var row = new NetworkReading { Name = adapter.Name, Description = adapter.Description, Type = adapter.NetworkInterfaceType.ToString() };
            string description = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            row.IsVirtual = description.Contains("virtual") || description.Contains("hyper-v") || description.Contains("vmware") ||
                description.Contains("vbox") || description.Contains("vpn") || description.Contains("wireguard") || description.Contains("wsl") || description.Contains("tap-");
            hasVirtual |= row.IsVirtual;
            try
            {
                if (adapter.Speed > 0) row.LinkMbps = adapter.Speed / 1000000.0;
                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                long now = Stopwatch.GetTimestamp();
                NetworkBaseline previous;
                if (networkBaseline.TryGetValue(adapter.Id, out previous))
                {
                    double elapsed = (now - previous.Tick) / (double)Stopwatch.Frequency;
                    long received = statistics.BytesReceived - previous.Received, sent = statistics.BytesSent - previous.Sent;
                    if (elapsed > 0 && received >= 0 && sent >= 0)
                    {
                        row.ReceiveMBps = received / elapsed / MB;
                        row.SendMBps = sent / elapsed / MB;
                        receivedTotal += row.ReceiveMBps.Value; sentTotal += row.SendMBps.Value;
                        usableRates++;
                        row.Status = "Live";
                    }
                    else row.Status = "Byte counter baseline reset.";
                }
                else row.Status = "Warming up for first elapsed interval.";
                networkBaseline[adapter.Id] = new NetworkBaseline { Received = statistics.BytesReceived, Sent = statistics.BytesSent, Tick = now };
            }
            catch
            {
                row.Status = "Network statistics unavailable.";
                networkBaseline.Remove(adapter.Id);
                hadFailure = true;
            }
            sample.Networks.Add(row);
        }
        var expired = new List<string>();
        foreach (string id in networkBaseline.Keys) if (!seen.Contains(id)) expired.Add(id);
        foreach (string id in expired) networkBaseline.Remove(id);
        if (usableRates > 0) { sample.NetworkReceiveMBps = receivedTotal; sample.NetworkSendMBps = sentTotal; }
        else sample.Notes.Add(sample.Networks.Count == 0 ? "No active network adapters are available." : "Network rates are warming up or unavailable.");
        if (hadFailure) sample.Notes.Add("Network totals are incomplete because an adapter's statistics could not be read.");
        if (hasVirtual || sample.Networks.Count > 1)
            sample.Notes.Add("Network totals sum active adapters; virtual adapters can count the same traffic more than once. Individual adapter rows are available.");
    }

    private void QueueCacheRefresh()
    {
        DateTime now = DateTime.UtcNow;
        bool queueGpu = false, queueStorage = false;
        lock (cacheLock)
        {
            if (!gpuBusy && (now - gpuAttemptUtc).TotalSeconds >= 5) { gpuBusy = true; gpuAttemptUtc = now; queueGpu = true; }
            if (!storageBusy && (now - storageAttemptUtc).TotalSeconds >= 5) { storageBusy = true; storageAttemptUtc = now; queueStorage = true; }
        }
        if (queueGpu) ThreadPool.QueueUserWorkItem(delegate { RefreshGpu(); });
        if (queueStorage) ThreadPool.QueueUserWorkItem(delegate { RefreshStorage(); });
    }

    private void RefreshStorage()
    {
        var rows = new List<StorageReading>();
        string note = null;
        string captured = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        try
        {
            if (disposed) return;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;
                var row = new StorageReading { Name = drive.Name, Type = drive.DriveType.ToString(), Timestamp = captured };
                try
                {
                    if (!drive.IsReady) continue;
                    long total = drive.TotalSize, free = drive.TotalFreeSpace;
                    row.Label = drive.VolumeLabel; row.Format = drive.DriveFormat;
                    if (total > 0 && free >= 0 && free <= total)
                    {
                        row.TotalGB = total / GB; row.FreeGB = free / GB; row.UsedGB = (total - free) / GB;
                        row.UsedPercent = Clamp(100.0 * (total - free) / total, 0, 100);
                        row.Status = "Ready";
                    }
                    else row.Status = "Capacity unavailable.";
                }
                catch { row.Status = "Capacity unavailable."; }
                rows.Add(row);
            }
            if (rows.Count == 0) note = "No ready local storage volumes are available.";
        }
        catch { note = "Storage capacities are unavailable."; }
        finally
        {
            lock (cacheLock)
            {
                if (!disposed) { cachedStorage = rows; storageStatus = note; storageCapturedUtc = DateTime.UtcNow; }
                storageBusy = false;
            }
        }
    }

    private void RefreshGpu()
    {
        GpuReading result = null;
        string failure = null;
        try
        {
            if (disposed) return;
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\nvidia-smi.exe");
            if (!File.Exists(executable)) { failure = "NVIDIA telemetry unavailable: nvidia-smi is not installed at the system path."; return; }
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw --format=csv,noheader,nounits",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(2000))
                {
                    try { process.Kill(); } catch { }
                    failure = "NVIDIA telemetry unavailable: query exceeded its two-second timeout.";
                    return;
                }
                if (process.ExitCode != 0) { failure = "NVIDIA telemetry unavailable (query exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ")."; return; }
                if (!output.Wait(250)) { failure = "NVIDIA telemetry unavailable: output was incomplete."; return; }
                // Drain errors without displaying driver output or process paths.
                error.Wait(100);
                string[] lines = output.Result.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) { failure = "NVIDIA telemetry unavailable: the query returned no adapters."; return; }
                List<string> fields = ParseCsvLine(lines[0]);
                if (fields.Count < 6) { failure = "NVIDIA telemetry unavailable: query format was not recognized."; return; }
                result = new GpuReading
                {
                    Name = fields[0].Trim(), Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    UtilizationPercent = Number(fields[1]), MemoryUsedMB = Scale(Number(fields[2]), 1048576.0 / MB),
                    MemoryTotalMB = Scale(Number(fields[3]), 1048576.0 / MB), TemperatureC = Number(fields[4]), PowerWatts = Number(fields[5]),
                    Status = lines.Length > 1 ? "Live telemetry for the first NVIDIA adapter." : "Live NVIDIA telemetry"
                };
                if (result.UtilizationPercent.HasValue) result.UtilizationPercent = Clamp(result.UtilizationPercent.Value, 0, 100);
                if (result.MemoryUsedMB.HasValue && result.MemoryTotalMB.HasValue && result.MemoryTotalMB.Value > 0)
                    result.MemoryPercent = Clamp(100 * result.MemoryUsedMB.Value / result.MemoryTotalMB.Value, 0, 100);
                if (!result.UtilizationPercent.HasValue || !result.TemperatureC.HasValue || !result.PowerWatts.HasValue)
                    result.Status = "Some NVIDIA metrics are unavailable; available readings are live.";
            }
        }
        catch { failure = "NVIDIA telemetry is unavailable; the local query could not be completed."; }
        finally
        {
            lock (cacheLock)
            {
                if (!disposed)
                {
                    if (result != null) cachedGpu = result;
                    else if (cachedGpu != null && cachedGpu.Timestamp != null)
                    {
                        cachedGpu = CloneGpu(cachedGpu); cachedGpu.IsStale = true; cachedGpu.Status = failure ?? "Latest GPU refresh was unavailable.";
                    }
                    else cachedGpu = new GpuReading { Name = "NVIDIA GPU", Status = failure ?? "GPU telemetry is unavailable." };
                }
                gpuBusy = false;
            }
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];
            if (current == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (current == ',' && !quoted) { fields.Add(value.ToString()); value.Length = 0; }
            else value.Append(current);
        }
        fields.Add(value.ToString());
        return fields;
    }

    private static double? Number(string value)
    {
        double number;
        return Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
            !Double.IsNaN(number) && !Double.IsInfinity(number) && number >= 0 ? (double?)number : null;
    }
    private static double? Scale(double? value, double factor) { return value.HasValue ? (double?)(value.Value * factor) : null; }
    private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }

    private static StorageReading CloneStorage(StorageReading row)
    {
        return new StorageReading { Name = row.Name, Label = row.Label, Type = row.Type, Format = row.Format, Status = row.Status, Timestamp = row.Timestamp,
            TotalGB = row.TotalGB, UsedGB = row.UsedGB, FreeGB = row.FreeGB, UsedPercent = row.UsedPercent };
    }
    private static GpuReading CloneGpu(GpuReading row)
    {
        return new GpuReading { Name = row.Name, Timestamp = row.Timestamp, Status = row.Status, IsStale = row.IsStale,
            UtilizationPercent = row.UtilizationPercent, MemoryUsedMB = row.MemoryUsedMB, MemoryTotalMB = row.MemoryTotalMB,
            MemoryPercent = row.MemoryPercent, TemperatureC = row.TemperatureC, PowerWatts = row.PowerWatts };
    }

    private sealed class CounterReader : IDisposable
    {
        private readonly string englishCounter;
        private PerformanceCounter counter;
        private bool primed;
        private DateTime retryUtc = DateTime.MinValue;
        public string Status;
        public CounterReader(string counterName) { englishCounter = counterName; }
        public double? Read()
        {
            if (DateTime.UtcNow < retryUtc) return null;
            try
            {
                if (counter == null)
                    counter = new PerformanceCounter(LocalizedName("PhysicalDisk"), LocalizedName(englishCounter), "_Total", true);
                double value = counter.NextValue();
                if (!primed) { primed = true; Status = "Disk activity counters are warming up for the first elapsed interval."; return null; }
                if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0) { Status = "A disk counter returned an unavailable interval."; return null; }
                Status = null;
                return value;
            }
            catch
            {
                Dispose();
                retryUtc = DateTime.UtcNow.AddSeconds(30);
                Status = "One or more disk counters are unavailable; permissions, localization, or counter registration may prevent reading them.";
                return null;
            }
        }
        public void Dispose() { if (counter != null) { counter.Dispose(); counter = null; } primed = false; }
    }

    [DllImport("pdh.dll", EntryPoint = "PdhLookupPerfNameByIndexW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhLookupPerfNameByIndex(string machine, uint nameIndex, StringBuilder buffer, ref uint length);
    private static string LocalizedName(string english)
    {
        try
        {
            string[] names = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib\009", "Counter", null) as string[];
            if (names != null)
            {
                for (int i = 0; i + 1 < names.Length; i += 2)
                {
                    uint index;
                    if (!String.Equals(names[i + 1], english, StringComparison.OrdinalIgnoreCase) || !UInt32.TryParse(names[i], out index)) continue;
                    uint size = 4096;
                    var buffer = new StringBuilder((int)size);
                    if (PdhLookupPerfNameByIndex(null, index, buffer, ref size) == 0) return buffer.ToString();
                    break;
                }
            }
        }
        catch { }
        return english;
    }

    public void Dispose()
    {
        lock (sampleLock)
        {
            if (disposed) return;
            disposed = true;
            diskRead.Dispose(); diskWrite.Dispose(); diskIdle.Dispose(); diskLatency.Dispose();
            networkBaseline.Clear();
        }
    }
}
