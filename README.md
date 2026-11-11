# Kwanteks' NIC Tweaker

WinUI 3 desktop app for tuning Windows NIC / TCP / RSS settings. Remake of st1icky’s network adapter tweaker.

Requires **Windows 10 1809+ (x64)** and runs **elevated** (UAC).

## Install

1. Download `KwanteksNICTweaker-Setup-<version>.exe` from Releases.
2. Accept the disclaimer, finish the wizard.
3. Launch from the Start Menu (**Kwanteks' NIC Tweaker**). UAC will prompt once.

Silent install:

```text
KwanteksNICTweaker-Setup-1.0.0.exe /VERYSILENT /NORESTART
```

Optional: `/DIR="D:\Apps\Kwanteks NIC Tweaker"` `/NOICONS`

## Uninstall

Use **Apps → Installed apps**, or the installer uninstaller. You will be asked whether to keep restore points, exports and settings under `%ProgramData%\KwanteksNICTweaker` (recommended: Keep). Silent uninstall (`/VERYSILENT`) keeps that data.

## Data location

All writable data lives under:

```text
%ProgramData%\KwanteksNICTweaker\
  settings.json
  Logs\            app.log, crash.log
  Backups\         registry tweak snapshots
  RestorePoints\   (reserved for custom restore-point files)
  Exports\         exported configs (optional)
  Presets\         intel.json / realtek.json / user presets
  DriverBackup\    exported NIC drivers
```

The app does **not** write into Program Files. Use **About → Open folder** in the app to open this path.

## Restore points

- **Create Restore Point** uses Windows System Restore when System Protection is enabled.
- Registry **Backup** snapshots (Backup → Backup now / Restore snapshot…) are stored under `Backups\` and restored from the app menu — not by double-clicking files.
- Snapshots and **Revert last** cover **registry values only**. RSS, interface (`NetIPInterface`) and global offload settings are set through Windows cmdlets and are not part of a snapshot, so use a restore point when you change those.
- Exported configs (`.nttweak`) use **Export Config / Import Config**; import always shows a preview (also `--import "file.nttweak"`).

## Build from source

Prerequisites: .NET 8 SDK, Windows App SDK workload / Visual Studio WinUI bits, [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
.\build.ps1
```

Produces:

- `publish\app\` — self-contained unpackaged app
- `dist\KwanteksNICTweaker-Setup-<version>.exe` — installer
- `dist\KwanteksNICTweaker-symbols-<version>.zip` — PDBs

Publish only: `.\build.ps1 -SkipInstaller`  
Optional signing: `.\build.ps1 -Sign <cert-thumbprint>`

Tests only (no installer, and no Visual Studio AppxPackage tooling needed — tests never read `.pri`):

```powershell
dotnet test "Kwanteks' NIC Tweaker.Tests\Kwanteks' NIC Tweaker.Tests.csproj" -c Release -p:Platform=x64 -p:AppxGeneratePriEnabled=false
```

## Notes

- Unpackaged (`WindowsPackageType=None`), self-contained, `win-x64`, `requireAdministrator`.
- Build needs the Windows App SDK / Visual Studio AppxPackage build tools for the WinUI PRI step; the csproj points at VS 2022/18 install paths when the plain .NET SDK cannot find them.
- Bundled `intel.json` / `realtek.json` disable common vendor power-saving features (may increase power draw). Unsupported keywords are skipped per adapter. Edit copies under `%ProgramData%\KwanteksNICTweaker\Presets` if you customize them (vendor files are refreshed from the install bundle on launch).
