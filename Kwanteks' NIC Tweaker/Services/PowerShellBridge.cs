using System.Diagnostics;
using System.Text;

namespace Kwanteks.NicTweaker.Services;

public sealed class PowerShellBridge : IPowerShellBridge
{
    private readonly ILoggingService _log;

    public PowerShellBridge(ILoggingService log) => _log = log;

    public async Task<string> RunAsync(string script, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start PowerShell.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);

        var err = stderr.ToString().Trim();
        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(err) ? stdout.ToString().Trim() : err;
            _log.Error($"PowerShell failed ({process.ExitCode}): {message}");
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? $"PowerShell exited with code {process.ExitCode}"
                : message);
        }

        if (!string.IsNullOrWhiteSpace(err))
            _log.Warn(err);

        return stdout.ToString().Trim();
    }
}
