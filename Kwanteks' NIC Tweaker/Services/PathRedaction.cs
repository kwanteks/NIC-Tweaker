namespace Kwanteks.NicTweaker.Services;

/// <summary>Strips local profile paths from strings that may be logged or shared.</summary>
public static class PathRedaction
{
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var result = text;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            result = result.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        // OneDrive / redirected Desktop sometimes appear without the profile prefix.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrEmpty(desktop) &&
            !desktop.StartsWith(profile ?? "\0", StringComparison.OrdinalIgnoreCase))
            result = result.Replace(desktop, "%DESKTOP%", StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
