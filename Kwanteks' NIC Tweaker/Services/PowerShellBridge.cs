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

        // A stuck cmdlet must not freeze the UI forever.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start PowerShell.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.StandardInput.WriteAsync(script.AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            _log.Error("PowerShell timed out after 5 minutes.");
            throw new TimeoutException("PowerShell did not finish within 5 minutes.");
        }

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
