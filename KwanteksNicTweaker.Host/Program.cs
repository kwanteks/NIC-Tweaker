using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace Kwanteks.NicTweaker.Host;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Kwanteks' NIC Tweaker", "app");
            var exePath = Path.Combine(appDir, "KwanteksNicTweaker.exe");
            var marker = Path.Combine(appDir, ".payload-version");
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            var needExtract = !File.Exists(exePath)
                              || !File.Exists(marker)
                              || !string.Equals(File.ReadAllText(marker).Trim(), version, StringComparison.Ordinal);

            if (needExtract)
                ExtractPayload(appDir, version);

            if (!File.Exists(exePath))
                throw new FileNotFoundException("App payload missing after extract.", exePath);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = appDir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Kwanteks' NIC Tweaker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ExtractPayload(string appDir, string version)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded payload.zip not found. Rebuild the host packager.");

        if (Directory.Exists(appDir))
            Directory.Delete(appDir, recursive: true);
        Directory.CreateDirectory(appDir);

        var tmpZip = Path.Combine(Path.GetTempPath(), $"kwanteks-payload-{Guid.NewGuid():N}.zip");
        try
        {
            using (var src = asm.GetManifestResourceStream(name)!)
            using (var dst = File.Create(tmpZip))
                src.CopyTo(dst);

            ZipFile.ExtractToDirectory(tmpZip, appDir);
            File.WriteAllText(Path.Combine(appDir, ".payload-version"), version);
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }
}
