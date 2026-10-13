using System.Collections.Concurrent;

namespace PhotoLibrarian.Diagnostics;

/// <summary>Nonblocking selection profiling; available in installed releases.</summary>
public static class SelectionPerformanceLog
{
    private static readonly ConcurrentQueue<string> Pending = new();
    private static int _draining;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoLibrarian", "logs", "selection-performance.log");

    public static void Write(string label, int count, long milliseconds)
    {
        var record = $"{DateTime.Now:O} | {label} | count={count} | ms={milliseconds}";
        System.Diagnostics.Debug.WriteLine("[PERF] " + record);
        Pending.Enqueue(record);
        StartDrain();
    }

    private static void StartDrain()
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var batch = new List<string>();
                while (Pending.TryDequeue(out var line))
                    batch.Add(line);
                if (batch.Count > 0)
                    File.AppendAllLines(FilePath, batch);
                // Keep diagnostics bounded: truncate old history when the file is large.
                if (new FileInfo(FilePath).Length > 2 * 1024 * 1024)
                {
                    var recent = File.ReadLines(FilePath).TakeLast(2000).ToArray();
                    File.WriteAllLines(FilePath, recent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[PERF] Log write failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _draining, 0);
                if (!Pending.IsEmpty) StartDrain();
            }
        });
    }
}
