# Build and Runtime Requirements

This file lists the dependencies of the supplied source tree, not a promise of compatibility with every distro, CPU, or MaNGOS database schema.

## Required for building

- **64-bit Windows or Linux** with a graphical desktop session for running the application.
- **.NET SDK 9.0**, selected by `global.json` (`9.0.100` with `latestFeature` roll-forward). The project targets **`net9.0`** and pins C# **9.0** in `Directory.Build.props`; do not silently upgrade its target framework or language version.
- Internet access during the first `dotnet restore` to obtain NuGet dependencies, or a fully configured offline NuGet cache.
- No Node.js, npm, Java, PHP, or external browser is required to build the core C# solution. **Node.js is optional** for checking JavaScript syntax.

The main desktop host uses **Photino.NET 4.0.16**, **Microsoft.Data.Sqlite.Core 9.0.18**, **SQLitePCLRaw.bundle_e_sqlcipher 2.1.11**, **System.Management 9.0.18**, and **MySqlConnector 2.6.1** in its project references. `dotnet restore` obtains transitive dependencies.

> **Maintenance note:** .NET 9 is a short-term support release. Assess its support and security status before shipping public binaries; an upgrade should be reviewed and tested separately rather than hidden in a documentation-only change.

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

The solution has no dedicated test project in the supplied snapshot; `dotnet test` may complete without executing application tests. For meaningful contributions, add automated tests and document manual checks for affected database operations and views.

## Validating source style locally

The optional CI formatting tools are **Python 3.12+** and **Node.js 22+**. They are not needed to compile or run the desktop application. From the repository root:

```bash
python scripts/check_style.py
python -m unittest discover -s tests -p 'test_style_checker.py' -v
dotnet restore MangosDatabaseStudio.sln
dotnet format whitespace MangosDatabaseStudio.sln --verify-no-changes --no-restore
```

To validate JavaScript syntax, run `node --check` on each `.js` file under `src/MangosDatabaseStudio.App/wwwroot/js`. GitHub Actions performs this automatically. Windows contributors can use `py -3` for the Python commands.

See [CI.md](CI.md) for the detailed style rules, workflow design, and troubleshooting notes.

## Running

Linux:

```bash
./scripts/run-linux.sh
```

Windows:

```bat
scripts\run-windows.bat
```

On Linux, ensure `DISPLAY` or `WAYLAND_DISPLAY` is set in a graphical session. Do **not** launch the main desktop app with `sudo`.

## Publishing and packaging

Linux:

```bash
./scripts/publish-linux.sh --self-contained
./scripts/package-app.sh --version 0.1.0 --platform linux
```

Windows:

```bat
scripts\publish-windows.bat --self-contained
scripts\package-app.bat --version 0.1.0
```

Published binaries are desktop applications, not a separate HTTP server. If you use a framework-dependent publish, the corresponding **.NET 9 runtime** must also be installed on the target system. Self-contained publish bundles that runtime, but native desktop libraries may still be needed.

Application data is created under `App_Data/`. **Never include populated `App_Data` when committing or distributing source/packages.** For the optional firmware helper consult [DEVICE_IDENTITY.md](DEVICE_IDENTITY.md).

## Troubleshooting

- **SDK mismatch:** Verify `dotnet --list-sdks`, `global.json`, and that SDK 9.0 is selected.
- **Native window won't start on Linux:** Check GTK 3, WebKitGTK, and a graphical session.
- **Database tools not found:** Install the matching MySQL/MariaDB CLI client tools and configure their executable paths.
- **Import/export permission issues:** Confirm your server account's database privileges and filesystem permissions.
- **Windows window remains blank:** Verify the WebView2 runtime and native desktop prerequisites.
