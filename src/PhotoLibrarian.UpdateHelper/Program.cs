using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PhotoLibrarian.UpdateHelper;

internal sealed record UpdateJob(
    string InstallerPath,
    long InstallerSize,
    string Sha256,
    int ProcessId,
    string AppExecutable,
    string? AlbumPath,
    string Version);

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length != 1)
            return;
        try
        {
            var jobPath = Path.GetFullPath(args[0]);
            var job = JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(jobPath))
                ?? throw new InvalidDataException("Update job is empty.");
            Application.Run(new UpdateWindow(job, jobPath));
        }
        catch (Exception exception)
        {
            WriteLog("Unable to open update job: " + exception);
        }
    }

    internal static void WriteLog(string message)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoLibrarian", "logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "update.log"),
                $"{DateTime.Now:O}  {message}{Environment.NewLine}");
        }
        catch { /* diagnostics never interrupt an update */ }
    }
}

internal sealed class UpdateWindow : Form
{
    private readonly UpdateJob _job;
    private readonly string _jobPath;
    private readonly Label _stage;
    private readonly Label _detail;
    private readonly ProgressBar _progress;
    private readonly Button _close;
    private readonly Color _accent = Color.FromArgb(72, 223, 235);

    public UpdateWindow(UpdateJob job, string jobPath)
    {
        _job = job;
        _jobPath = jobPath;
        Text = "PhotoLibrarian // Update";
        Width = 530;
        Height = 280;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(9, 18, 26);
        ForeColor = Color.FromArgb(222, 240, 245);
        Font = new Font("Segoe UI", 10f);
        ControlBox = false;

        var accentLine = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = _accent };
        Controls.Add(accentLine);
        var heading = new Label
        {
            Left = 24, Top = 27, Width = 470, Height = 30,
            Font = new Font("Consolas", 16f, FontStyle.Bold),
            Text = "PHOTOLIBRARIAN // UPDATE", ForeColor = _accent
        };
        Controls.Add(heading);
        var brand = new Label
        {
            Left = 24, Top = 62, Width = 460, Height = 20,
            Font = new Font("Consolas", 9f),
            Text = "TRIMENOR  /  INSTALLATION MANAGER",
            ForeColor = Color.FromArgb(125, 165, 180)
        };
        Controls.Add(brand);
        _stage = new Label
        {
            Left = 24, Top = 101, Width = 470, Height = 30,
            Text = "Preparing secure update..."
        };
        Controls.Add(_stage);
        _detail = new Label
        {
            Left = 24, Top = 134, Width = 470, Height = 38,
            Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(155, 183, 196)
        };
        Controls.Add(_detail);
        _progress = new ProgressBar
        {
            Left = 24, Top = 181, Width = 470, Height = 8,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28
        };
        Controls.Add(_progress);
        _close = new Button
        {
            Left = 375, Top = 203, Width = 119, Height = 32,
            Text = "CLOSE", Enabled = false,
            BackColor = Color.FromArgb(19, 48, 60),
            ForeColor = _accent, FlatStyle = FlatStyle.Flat
        };
        _close.FlatAppearance.BorderColor = _accent;
        _close.Click += (_, _) => Close();
        Controls.Add(_close);

        Shown += async (_, _) => await RunUpdateAsync();
    }

    private void Report(string stage, string detail)
    {
        _stage.Text = stage;
        _detail.Text = detail;
        Program.WriteLog(stage + " - " + detail);
    }

    private async Task RunUpdateAsync()
    {
        try
        {
            ValidateJob();
            Report("WAITING FOR PHOTOLIBRARIAN", "Closing the previous application before installation...");
            await WaitForApplicationExitAsync();

            Report("VERIFYING INSTALLER", "Rechecking the exact SHA-256 of the downloaded installer...");
            await VerifyInstallerAsync();

            Report("INSTALLING UPDATE", "The Windows administrator confirmation may appear.");
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = _job.InstallerPath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- " +
                            "/CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /RESTARTEXITCODE=3010"
            }) ?? throw new InvalidOperationException("The installer could not start.");

            await installer.WaitForExitAsync();
            if (installer.ExitCode == 3010)
                throw new InvalidOperationException(
                    "Installation completed but Windows needs a restart. " +
                    "Restart Windows before opening the new version.");
            if (installer.ExitCode != 0)
                throw new InvalidOperationException($"Installer exited with code {installer.ExitCode}.");

            Report("UPDATE COMPLETE", "Starting PhotoLibrarian " + _job.Version + "...");
            LaunchApplication();
            Close();
        }
        catch (Exception exception)
        {
            Report("UPDATE FAILED", exception.Message);
            Program.WriteLog(exception.ToString());
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            _close.Enabled = true;
            ControlBox = true;
        }
    }

    private void ValidateJob()
    {
        if (!Regex.IsMatch(_job.Sha256, "^[a-fA-F0-9]{64}$") ||
            !Regex.IsMatch(_job.Version, "^[0-9]+\\.[0-9]+\\.[0-9]+$") ||
            _job.InstallerSize is <= 0 or > 734003200 ||
            _job.ProcessId <= 0)
            throw new InvalidDataException("Invalid update job parameters.");

        var expectedFileName = "PhotoLibrarian-" + _job.Version + "-Setup-x64.exe";
        if (!string.Equals(Path.GetFileName(_job.InstallerPath), expectedFileName,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected installer file name.");
        if (!string.Equals(Path.GetFileName(_job.AppExecutable), "PhotoLibrarian.exe",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected target executable.");
        if (!File.Exists(_job.InstallerPath) || !File.Exists(_job.AppExecutable))
            throw new FileNotFoundException("Installer or application executable is missing.");
        if (!Path.GetFullPath(_jobPath).StartsWith(
                Path.GetDirectoryName(Path.GetFullPath(_job.InstallerPath))! +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update manifest is outside the update directory.");
    }

    private async Task WaitForApplicationExitAsync()
    {
        try
        {
            using var process = Process.GetProcessById(_job.ProcessId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            // App already exited.
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("PhotoLibrarian did not close within 45 seconds. Update canceled.");
        }
    }

    private async Task VerifyInstallerAsync()
    {
        var info = new FileInfo(_job.InstallerPath);
        if (info.Length != _job.InstallerSize)
            throw new InvalidDataException("Installer length changed after download.");
        await using var file = new FileStream(
            _job.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(await sha.ComputeHashAsync(file));
        if (!string.Equals(actual, _job.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer SHA-256 verification failed.");
    }

    private void LaunchApplication()
    {
        if (!File.Exists(_job.AppExecutable))
            throw new FileNotFoundException("The updated PhotoLibrarian executable is missing.");

        var start = new ProcessStartInfo
        {
            FileName = _job.AppExecutable,
            WorkingDirectory = Path.GetDirectoryName(_job.AppExecutable)!,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(_job.AlbumPath))
        {
            start.ArgumentList.Add("--album");
            start.ArgumentList.Add(_job.AlbumPath);
        }
        Process.Start(start) ?? throw new InvalidOperationException("PhotoLibrarian did not start.");
    }
}
