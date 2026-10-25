# Build and Runtime Requirements

This file lists the dependencies of the supplied source tree, not a promise of compatibility with every distro, CPU, or MaNGOS database schema.

## Required for building

- **64-bit Windows or Linux** with a graphical desktop session for running the application.
- **.NET SDK 10.x**, selected by `global.json` (`10.0.100` with `latestFeature` roll-forward). Every project targets **`net10.0`**, with C# **14.0** in `Directory.Build.props`. The roll-forward policy stays within SDK 10; other projects can continue using an installed .NET 9 SDK independently.
- Internet access during the first `dotnet restore` to obtain NuGet dependencies, or a fully configured offline NuGet cache.
- No Node.js, npm, Java, PHP, or external browser is required to build the core C# solution. **Node.js is optional** for checking JavaScript syntax.

The main desktop host uses **Photino.NET 4.0.16**, **Microsoft.Data.Sqlite.Core 10.0.13**, **SQLitePCLRaw.bundle_e_sqlcipher 2.1.11**, **System.Management 10.0.13**, and **MySqlConnector 2.6.1** in its project references. `dotnet restore` obtains transitive dependencies.

> **Framework policy:** This repository targets .NET 10 LTS. Do not upgrade the SDK, target framework, or C# language version without a reviewed change that also updates CI and documentation.

## Runtime requirements

| Component | Needed for | Notes |
| --- | --- | --- |
| Windows x64 or Linux x64 desktop | Application window | Photino supplies the native desktop shell; graphical session is necessary. |
| GTK 3 | Linux desktop window | Install from your distribution's package manager. |
| WebKitGTK compatible with Photino | Linux embedded web content | Package names vary by distribution/release; follow Photino's runtime dependencies. |
| Windows WebView2 runtime | Windows embedded web content | Install/update the runtime if not already present. |
| MySQL 8 or MariaDB 10.6+ server | Database features | An accessible server with appropriate database privileges is needed for editor operations. |
| `mysql` or `mariadb` command-line client | SQL imports | Executable must be on `PATH` or configured in application settings. |
| `mysqldump` or `mariadb-dump` | SQL exports | Executable must be on `PATH` or configured in application settings. |
| Polkit / `pkexec` | Experimental Linux Device Identity helper only | **Optional**, disabled by default; not necessary for ordinary operation. |

MySQL/MariaDB is **not** needed just to compile or launch the UI, but database features require a reachable server. Use a least-privilege database account; administrative DDL operations need separate privileges.

## Building from the repository root

```bash
dotnet --version
dotnet restore MangosDatabaseStudio.sln
dotnet build MangosDatabaseStudio.sln -c Release --no-restore
dotnet test MangosDatabaseStudio.sln -c Release --no-build --no-restore
```

The solution does not contain a conventional VSTest test assembly. `dotnet test` can complete without executing application tests; run the separate Field Data integration-check console project explicitly as shown below, and document manual checks for database operations and views.

```bash
dotnet run --project tests/MangosDatabaseStudio.FieldDataChecks/MangosDatabaseStudio.FieldDataChecks.csproj --configuration Release
```

## Validating source style locally

The optional CI formatting tools are **Python 3.12+** and **Node.js 22+**. They are not needed to compile or run the desktop application. From the repository root:

```bash
python scripts/check_style.py
python -m unittest discover -s tests -p 'test_style_checker.py' -v
dotnet restore MangosDatabaseStudio.sln
dotnet format whitespace MangosDatabaseStudio.sln --verify-no-changes --no-restore
```

To validate JavaScript syntax, run `node --check` on each `.js` file under `src/MangosDatabaseStudio.App/wwwroot/js`. GitHub Actions performs this automatically. Windows contributors can use `py -3` for the Python commands.

For style requirements, see [PROJECT_RULES.md](PROJECT_RULES.md); for automated checks, see [.github/workflows/code-style.yml](.github/workflows/code-style.yml) and [.github/workflows/build.yml](.github/workflows/build.yml).

## Running

Linux:

```bash
./scripts/linux/run-linux.sh
```

Windows:

```bat
scripts\windows\run-windows.bat
```

On Linux, ensure `DISPLAY` or `WAYLAND_DISPLAY` is set in a graphical session. Do **not** launch the main desktop app with `sudo`.

## Publishing and packaging

Linux:

```bash
./scripts/linux/publish-linux.sh --self-contained
./scripts/linux/package-app.sh --platform linux
```

Windows:

```bat
scripts\windows\publish-windows.bat --self-contained
scripts\windows\package-app.bat
```

Published binaries are desktop applications, not a separate HTTP server. If you use a framework-dependent publish, the corresponding **.NET 10 runtime** must also be installed on the target system. Self-contained publish bundles that runtime, but native desktop libraries may still be needed.

Application data is created under `App_Data/`. **Never include populated `App_Data` when committing or distributing source/packages.** For the optional firmware helper consult [DEVICE_IDENTITY.md](DEVICE_IDENTITY.md).

## Troubleshooting

- **SDK mismatch:** Verify `dotnet --list-sdks`, `global.json`, and that SDK 10.x is selected.
- **Native window won't start on Linux:** Check GTK 3, WebKitGTK, and a graphical session.
- **Database tools not found:** Install the matching MySQL/MariaDB CLI client tools and configure their executable paths.
- **Import/export permission issues:** Confirm your server account's database privileges and filesystem permissions.
- **Windows window remains blank:** Verify the WebView2 runtime and native desktop prerequisites.

### Git-based application versioning

Use annotated release tags such as `v.0.2.5-beta` or `v1.2.0` (both formats are supported). Run `git fetch --tags` before building if the tag was created remotely. Builds embed the latest reachable tag and short Git commit hash in the desktop application's assembly metadata. **Git is not needed to run the published application.** In an untagged source checkout the build displays `v0.0.0`; without Git metadata, the commit identifier is `unknown`.

Release packaging commands read the Git tag automatically, including prerelease suffixes. To package an untagged source tree, supply `--version 0.2.5-beta` explicitly. Details and examples appear in [README.md](README.md#application-versioning).
