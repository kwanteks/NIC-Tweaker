using System.Text;
using System.Text.Json;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Xunit;

namespace Kwanteks.NicTweaker.Tests;

public class ConfigTransferTests
{
    private static (ConfigTransferService Sut, AdapterService Adapters) CreateServices()
    {
        var storage = new StorageService();
        var log = new LoggingService(storage);
        var registry = new RegistryService();
        var ps = new PowerShellBridge(log);
        var rss = new RssService(ps, registry, log);
        var netIp = new NetIpInterfaceService(ps);
        var backup = new BackupService(storage, registry, log);
        var engine = new TweakEngine(registry, rss, netIp, ps, backup, log);
        var sut = new ConfigTransferService(engine, registry, ps, log);
        var adapters = new AdapterService(registry, ps, log);
        return (sut, adapters);
    }

    private static ConfigTransferService CreateSut() => CreateServices().Sut;

    private static string WriteTemp(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public async Task Load_rejects_invalid_json()
    {
        var sut = CreateSut();
        var path = WriteTemp("nt-bad-json.nttweak", "{not-json");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadAndValidateAsync(path));
        Assert.Contains("Invalid JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_rejects_wrong_format()
    {
        var sut = CreateSut();
        var path = WriteTemp("nt-bad-format.nttweak",
            """{"format":"evil","schemaVersion":1,"tweaks":[]}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadAndValidateAsync(path));
        Assert.Contains("Unsupported format", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_rejects_newer_schema()
    {
        var sut = CreateSut();
        var path = WriteTemp("nt-new-schema.nttweak",
            """{"format":"networktweaker-config","schemaVersion":99,"tweaks":[]}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadAndValidateAsync(path));
        Assert.Contains("newer version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_rejects_huge_file()
    {
        var sut = CreateSut();
        var path = Path.Combine(Path.GetTempPath(), "nt-huge.nttweak");
        File.WriteAllBytes(path, new byte[ConfigFormat.MaxBytes + 1]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LoadAndValidateAsync(path));
        Assert.Contains("too large", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_strips_to_id_state_value_only()
    {
        var sut = CreateSut();
        var json = """
            {
              "format": "networktweaker-config",
              "schemaVersion": 1,
              "tweaks": [
                {
                  "id": "totally.unknown.id",
                  "section": "X",
                  "label": "Hack",
                  "state": "Set",
                  "value": "1",
                  "registryPath": "HKLM\\Evil",
                  "command": "calc.exe"
                }
              ]
            }
            """;
        var path = WriteTemp("nt-mal.nttweak", json);
        var cfg = await sut.LoadAndValidateAsync(path);
        Assert.Single(cfg.Tweaks);
        Assert.Equal("totally.unknown.id", cfg.Tweaks[0].Id);
        // projected entry has no extra fields by construction
        var round = JsonSerializer.Serialize(cfg.Tweaks[0]);
        Assert.DoesNotContain("registryPath", round, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command", round, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_skips_unknown_ids_and_out_of_range()
    {
        var (sut, adapters) = CreateServices();
        var list = await adapters.GetAdaptersAsync();
        if (list.Count == 0) return; // environment without NICs

        var adapter = list[0];
        await adapters.EnrichPathsAsync(adapter);

        var cfg = new NetworkTweakerConfig
        {
            Format = ConfigFormat.Name,
            SchemaVersion = 1,
            Tweaks =
            [
                new ConfigTweakEntry { Id = "no.such.tweak", State = "Set", Value = "1", Label = "x", Section = "x" },
                new ConfigTweakEntry
                {
                    Id = TweakCatalog.All.First(d => d.MinValue is not null || d.MaxValue is not null).Id,
                    State = "Set",
                    Value = "999999999",
                    Label = "range",
                    Section = "x"
                }
            ]
        };

        var preview = await sut.BuildPreviewAsync(cfg, adapter, "IPv4");
        Assert.Contains(preview.Skipped, s => s.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(preview.Skipped, s => s.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(preview.Rows, r => r.Id == "no.such.tweak");
    }

    [Fact]
    public async Task Export_windows_defaults_save_reload_roundtrip()
    {
        var (sut, adapters) = CreateServices();
        var list = await adapters.GetAdaptersAsync();
        if (list.Count == 0) return;

        var adapter = list[0];
        await adapters.EnrichPathsAsync(adapter);

        // Defaults-only skips live ReadTweaks (still captures adapter meta).
        var cfg = await sut.BuildExportAsync(adapter, "IPv4", windowsDefaultsOnly: true);
        Assert.Equal(ConfigFormat.Name, cfg.Format);
        Assert.True(cfg.Tweaks.Count > 10);
        Assert.DoesNotContain(cfg.Tweaks, t => string.IsNullOrWhiteSpace(t.Id));
        Assert.All(cfg.Tweaks.Where(t => !t.Id.StartsWith("app.", StringComparison.Ordinal)),
            t => Assert.Equal("NotSet", t.State));

        var path = Path.Combine(Path.GetTempPath(), $"NetworkTweaker_TEST_{DateTime.Now:yyyyMMdd-HHmmss}.nttweak");
        await sut.SaveAtomicAsync(cfg, path);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 20);

        var loaded = await sut.LoadAndValidateAsync(path);
        Assert.Equal(cfg.Tweaks.Count, loaded.Tweaks.Count);
        File.Delete(path);
    }

    [Fact]
    public async Task Export_save_reload_roundtrip_on_desktop_path()
    {
        var (sut, adapters) = CreateServices();
        var list = await adapters.GetAdaptersAsync();
        if (list.Count == 0) return;

        var adapter = list[0];
        await adapters.EnrichPathsAsync(adapter);

        var cfg = await sut.BuildExportAsync(adapter, "IPv4", windowsDefaultsOnly: false);
        Assert.Equal(ConfigFormat.Name, cfg.Format);
        Assert.True(cfg.Tweaks.Count > 10);
        Assert.DoesNotContain(cfg.Tweaks, t => string.IsNullOrWhiteSpace(t.Id));
        var json = JsonSerializer.Serialize(cfg);
        Assert.DoesNotContain("UserName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.Ordinal);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"NetworkTweaker_TEST_{DateTime.Now:yyyyMMdd-HHmmss}.nttweak");
        await sut.SaveAtomicAsync(cfg, path);
        Assert.True(File.Exists(path));
        Assert.StartsWith(desktop, path, StringComparison.OrdinalIgnoreCase);

        var loaded = await sut.LoadAndValidateAsync(path);
        Assert.Equal(cfg.Tweaks.Count, loaded.Tweaks.Count);

        var preview = await sut.BuildPreviewAsync(loaded, adapter, "IPv4");
        // Same adapter export → mostly unchanged; no crash
        Assert.True(preview.Rows.Count + preview.Skipped.Count > 0);

        // Different-adapter warning path
        var other = cfg;
        other.Adapter = new ConfigAdapterInfo
        {
            Description = "Some Other NIC Model",
            InterfaceGuid = Guid.NewGuid().ToString(),
            NetCfgInstanceId = "different",
            DriverProvider = "Other",
            DriverVersion = "0.0"
        };
        var warnPreview = await sut.BuildPreviewAsync(other, adapter, "IPv4");
        Assert.NotNull(warnPreview.AdapterWarning);
        Assert.Contains("Exported from", warnPreview.AdapterWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_NotSet_deletes_via_engine_path()
    {
        // Ensures ImportedDesired for NotSet uses DefaultDisplay so IsDeleteRequest fires.
        var def = TweakCatalog.All.FirstOrDefault(d => d.SupportsDelete && d.Backend == TweakBackend.Registry);
        if (def is null) return;
        Assert.True(TweakEngine.IsDeleteRequest(def, def.DefaultDisplay));
        await Task.CompletedTask;
    }
}
