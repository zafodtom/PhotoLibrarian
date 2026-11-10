using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Security.Cryptography;
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

        if (args.Length != 7)
            return;
        try
        {
            var job = new UpdateJob(
                args[0],
                long.Parse(args[1], CultureInfo.InvariantCulture),
                args[2],
                int.Parse(args[3], CultureInfo.InvariantCulture),
                args[4],
                string.IsNullOrWhiteSpace(args[5]) ? null : args[5],
                args[6]);
            Application.Run(new UpdateWindow(job));
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
    private readonly Label _stage;
    private readonly Label _detail;
    private readonly ProgressBar _progress;
    private readonly Button _close;
    private readonly Color _accent = Color.FromArgb(83, 215, 232);

    public UpdateWindow(UpdateJob job)
    {
        _job = job;
        Text = "PhotoLibrarian // Update Sequence";
        Width = 570;
        Height = 322;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(7, 11, 16);
        ForeColor = Color.FromArgb(230, 247, 250);
        Font = new Font("Segoe UI", 10f);
        ControlBox = false;

        // Match the BootWindow: inset framed panel, cyan side-bar, technical
        // Consolas typography and the same dark archive palette.
        var frame = new Panel
        {
            Left = 18, Top = 18, Width = 518, Height = 253,
            BackColor = Color.FromArgb(10, 18, 26)
        };
        frame.Paint += (_, e) =>
        {
            using var border = new Pen(Color.FromArgb(52, 115, 138), 1f);
            e.Graphics.DrawRectangle(border, 0, 0, frame.Width - 1, frame.Height - 1);
        };
        Controls.Add(frame);
        frame.Controls.Add(new Panel
        {
            Left = 17, Top = 18, Height = 44, Width = 4, BackColor = _accent
        });
        var heading = new Label
        {
            Left = 32, Top = 17, Width = 448, Height = 29,
            Font = new Font("Consolas", 16f, FontStyle.Bold),
            Text = "PHOTO // LIBRARIAN", ForeColor = Color.FromArgb(230, 247, 250)
        };
        frame.Controls.Add(heading);
        frame.Controls.Add(new Label
        {
            Left = 34, Top = 47, Width = 448, Height = 21,
            Font = new Font("Consolas", 9f),
            Text = "VISUAL ARCHIVE // UPDATE SEQUENCE",
            ForeColor = Color.FromArgb(52, 115, 138)
        });
        _stage = new Label
        {
            Left = 30, Top = 91, Width = 448, Height = 26,
            Font = new Font("Consolas", 11f, FontStyle.Bold),
            ForeColor = _accent,
            Text = "INITIALIZING UPDATE..."
        };
        frame.Controls.Add(_stage);
        _detail = new Label
        {
            Left = 30, Top = 125, Width = 448, Height = 41,
            Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(155, 183, 196)
        };
        frame.Controls.Add(_detail);
        _progress = new ProgressBar
        {
            Left = 30, Top = 182, Width = 448, Height = 7,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28
        };
        frame.Controls.Add(_progress);
        frame.Controls.Add(new Label
        {
            Left = 30, Top = 209, Width = 255, Height = 17,
            Font = new Font("Consolas", 8f),
            ForeColor = Color.FromArgb(52, 115, 138),
            Text = "SYS  /  UPDATE CONTROL"
        });
        _close = new Button
        {
            Left = 366, Top = 201, Width = 112, Height = 32,
            Text = "CLOSE", Enabled = false,
            BackColor = Color.FromArgb(19, 48, 60),
            ForeColor = _accent, FlatStyle = FlatStyle.Flat,
            Font = new Font("Consolas", 10f, FontStyle.Bold)
        };
        _close.FlatAppearance.BorderColor = Color.FromArgb(52, 115, 138);
        _close.Click += (_, _) => Close();
        frame.Controls.Add(_close);

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
            // Keep a read-only handle open throughout installation to prevent
            // replacing/deleting the verified installer between hashing and launch.
            await using var verifiedInstaller = await VerifyInstallerAsync();

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

    private async Task<FileStream> VerifyInstallerAsync()
    {
        var info = new FileInfo(_job.InstallerPath);
        if (info.Length != _job.InstallerSize)
            throw new InvalidDataException("Installer length changed after download.");
        var file = new FileStream(
            _job.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            using var sha = SHA256.Create();
            var actual = Convert.ToHexString(await sha.ComputeHashAsync(file));
            if (!string.Equals(actual, _job.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer SHA-256 verification failed.");
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
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
        if (Process.Start(start) is null)
            throw new InvalidOperationException("PhotoLibrarian did not start.");
    }
}
