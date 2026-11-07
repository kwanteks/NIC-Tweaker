namespace Kwanteks.NicTweaker.Services;

public interface IPowerShellBridge
{
    Task<string> RunAsync(string script, CancellationToken cancellationToken = default);
}
