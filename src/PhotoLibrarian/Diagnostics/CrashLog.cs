using System;
using System.IO;
using System.Text;

namespace PhotoLibrarian.Diagnostics;

public static class CrashLog
{
    private static readonly object Sync = new();

    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoLibrarian",
            "logs");

    public static string Write(string source, Exception exception, string? albumPath = null)
    {
        var timestamp = DateTime.Now;
        var report = new StringBuilder();
        report.AppendLine("PhotoLibrarian // FATAL DIAGNOSTIC");
        report.AppendLine($"Time: {timestamp:O}");
        report.AppendLine($"Source: {source}");
        report.AppendLine($"Album: {albumPath ?? "(none)"}");
        report.AppendLine($"Process: {Environment.ProcessId}");
        report.AppendLine($"OS: {Environment.OSVersion}");
        report.AppendLine($".NET: {Environment.Version}");
        report.AppendLine($"Application: {typeof(CrashLog).Assembly.GetName().Version}");
        report.AppendLine();
        report.AppendLine(exception.ToString());

        // Prefer portable album diagnostics; fallback must work even if album
        // storage was disconnected, deleted or is on an unwritable volume.
        var destinations = new[]
        {
            !string.IsNullOrWhiteSpace(albumPath)
                ? Path.Combine(albumPath, ".album", "logs")
                : null,
            LogDirectory
        };
        foreach (var destination in destinations)
        {
            if (string.IsNullOrWhiteSpace(destination)) continue;
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(destination);
                    var path = Path.Combine(destination,
                        $"crash-{timestamp:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
                    File.WriteAllText(path, report.ToString());
                    foreach (var old in Directory.EnumerateFiles(destination, "crash-*.log")
                                 .OrderByDescending(File.GetLastWriteTimeUtc).Skip(20))
                    {
                        try { File.Delete(old); } catch { }
                    }
                    return path;
                }
            }
            catch { /* Try fallback directory. Never crash the crash handler. */ }
        }
        return string.Empty;
    }

    public static string Write(string source, object? exceptionObject, string? albumPath = null)
    {
        if (exceptionObject is Exception exception)
            return Write(source, exception, albumPath);

        return Write(
            source,
            new Exception(exceptionObject?.ToString() ?? "Unknown unhandled exception"),
            albumPath);
    }
}
