**English** | [Tiếng Việt](BUILDING.vi.md)

# Building STG Phantom VPN

This guide is for anyone who wants to build the app from source. The app is WPF on .NET Framework 4.8 and runs on Windows 7 SP1 and later.

## Requirements

| Component | Notes |
|---|---|
| Windows 10/11 or Linux (x64) | On Linux, `net48` is compiled through `EnableWindowsTargeting`. |
| [.NET SDK 8](https://dotnet.microsoft.com/download) or newer | Required. Visual Studio is not needed. |
| [Inno Setup 6](https://jrsoftware.org/isdl.php) | Optional; only for building the `.exe` installer. |
| Wine with 32-bit support | Only on Linux, and only for the installer. Debian/Ubuntu: `wine wine64 wine32:i386`. |

## Building on Windows

Open PowerShell in the root of the source tree:

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1              # tests, build, portable zip
powershell -ExecutionPolicy Bypass -File build\build.ps1 -Installer   # also the installer (needs Inno Setup 6)
powershell -ExecutionPolicy Bypass -File build\build.ps1 -BundleOpenVpn -OutDir D:\out
```

- `-Installer`: `ISCC.exe` is found through the `ISCC` environment variable, the Inno Setup 6 registry entry or the default install folders.
- `-BundleOpenVpn`: puts the official OpenVPN installers (SHA-256 checked) into `redist\` of the zip, with the GPL source in `source\`.
- `-OutDir <folder>`: changes the output folder. `-SkipTests`: skips the tests (not recommended).

Instead of the script you can open `src\STGPhantomVPN\STGPhantomVPN.csproj` in Visual Studio 2022, or build directly:

```powershell
dotnet build src\STGPhantomVPN\STGPhantomVPN.csproj -c Release
```

The result is in `src\STGPhantomVPN\bin\Release\net48\`.

## Building on Linux

```bash
build/build.sh                    # tests, build, portable zip
build/build.sh --installer        # also the installer; Inno Setup 6.7.4 is downloaded (SHA-256 checked) and run through Wine
build/build.sh --bundle-openvpn   # zip with the OpenVPN installers and the GPL source
OUT_DIR=/some/path build/build.sh # change the output folder
```

## Running the tests

```bash
dotnet run --project tests/STGPhantomVPN.Tests -c Release
```

Both build scripts run the tests first and stop if any test fails.

## Output

By default everything is written to the `out/` folder in the source tree:

- `STGPhantomVPN-Windows-<version>.zip`: the portable build, with a `.sha256` file.
- `STGPhantomVPN-Setup-<version>.exe`: the installer (with `-Installer` / `--installer`), with a `.sha256` file.

## Changing the version

The version lives in `<Version>` in `src/STGPhantomVPN/STGPhantomVPN.csproj`; both build scripts read it from there. When you change it, update the same value in:

- `<FileVersion>` and `<InformationalVersion>` in the same csproj,
- `src/STGPhantomVPN/app.manifest`,
- `AppVersion.Current` in `src/STGPhantomVPN/Core/VpnGateClient.cs`,
- `#define AppVersion` in `installer/STGPhantomVPN.iss`,
- the title line and file names in `README.md` and `README.vi.md`, and the first line of `licenses/THIRD-PARTY-NOTICES.txt`.

The tests fail if these values do not match.

## Notes

- Builds are not code-signed, so Windows SmartScreen warns on first run. Compare the SHA-256 with the `.sha256` file, then choose **More info → Run anyway**.
- The installer and the default zip do not contain OpenVPN. On first use the app downloads the official OpenVPN installer from openvpn.net and only installs it when its SHA-256 matches the value pinned in the source (`Core/PinnedDownloads.cs`).
- File times in the zip and installer built on Linux are set to `RELEASE_DATE` in `build/build.sh` (or `SOURCE_DATE_EPOCH`), so rebuilding the same source gives identical files. The zip made by `build.ps1` uses the current time.
