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
        var path = Path.Combine(
            LogDirectory,
            $"crash-{timestamp:yyyyMMdd-HHmmss-fff}.log");

        try
        {
            Directory.CreateDirectory(LogDirectory);

            var builder = new StringBuilder();
            builder.AppendLine("PhotoLibrarian crash report");
            builder.AppendLine($"Time: {timestamp:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"Album: {albumPath ?? "(none)"}");
            builder.AppendLine($"Process: {Environment.ProcessId}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($".NET: {Environment.Version}");
            builder.AppendLine();
            builder.AppendLine(exception.ToString());

            lock (Sync)
                File.WriteAllText(path, builder.ToString());

            return path;
        }
        catch
        {
            return path;
        }
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
