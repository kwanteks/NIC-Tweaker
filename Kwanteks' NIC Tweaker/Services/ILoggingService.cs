namespace Kwanteks.NicTweaker.Services;

public interface ILoggingService
{
    event EventHandler<string>? EntryAdded;
    IReadOnlyList<string> Entries { get; }
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Clear();
}
