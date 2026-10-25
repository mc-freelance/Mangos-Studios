# MaNGOS Database Studio

[![Build (Windows & Linux)](https://github.com/mc-freelance/Mangos-Studios/actions/workflows/build.yml/badge.svg)](https://github.com/mc-freelance/Mangos-Studios/actions/workflows/build.yml)
[![Code Styling & Checks](https://github.com/mc-freelance/Mangos-Studios/actions/workflows/code-style.yml/badge.svg)](https://github.com/mc-freelance/Mangos-Studios/actions/workflows/code-style.yml)

MaNGOS Database Studio is an open-source, desktop-only database workbench for **getMaNGOS** world databases and MySQL/MariaDB servers. It is written in **C# for .NET 10** and uses a native **Photino** desktop window with a private, loopback-only ASP.NET Core UI. It is **not intended to run as a publicly accessible web server**.

The application aims to let contributors inspect, manage, and prepare database changes through a consistent interface while respecting the schema differences between MaNGOS expansions.

## Features

- Connect to MySQL/MariaDB and explore database schemas and table records.
- Browse and edit world tables, with schema-aware field editors and permission checks.
- Use dedicated viewers for NPC vendors, NPC trainers, loot, pools, events, quests, and gossip menus/sources.
- Investigate overlapping or linked creature/game-object spawns.
- View and modify field-value definitions stored in an editable SQLite catalog, with expansion-aware datasets (Vanilla through Cataclysm); newer expansion data may remain incomplete.
- Review database edits in Change Storage and generate SQL contributions where supported.
- Execute SQL through a guarded console, import/export SQL, and apply database updates.
- Manage local user accounts, roles, permissions, and encrypted connection profiles.
- Use desktop themes and zoom controls.

Some viewers, workflows, and expansion mappings are still evolving; individual operations may not support every MaNGOS schema.

## Hardware Device / Device Identity disclaimer

**The Hardware Device / Device Identity feature is a work in progress (WIP), is OFF by default, and may change significantly or be removed from the project entirely.** It is not a prerequisite for normal database-editing features. No contributor should build a dependency on the feature, enable it silently, or assume it provides complete authentication or anti-abuse protection. On Linux, enabling its privileged firmware helper can prompt for administrative authorization; never run the main application as root. See [DEVICE_IDENTITY.md](DEVICE_IDENTITY.md) for its current behavior and limitations.

## Getting started

See [REQUIREMENTS.md](REQUIREMENTS.md) for the .NET SDK, operating-system libraries, database server/client tools, runtime dependencies, and build steps.

### Continuous integration

Every push and pull request runs two GitHub Actions workflows:

- **Build (Windows and Linux)** — restores and builds the complete .NET 10 solution in Release configuration on Windows and Ubuntu, then invokes any .NET tests in the solution.
- **Code style and source checks** — verifies the project-specific C#/JavaScript brace and block spacing rules, AGPL headers for C# files, the no-XML-documentation rule, source whitespace, JavaScript syntax, and Roslyn C# whitespace formatting.

See [.github/workflows/build.yml](.github/workflows/build.yml) and [.github/workflows/code-style.yml](.github/workflows/code-style.yml) for the checks, and [REQUIREMENTS.md](REQUIREMENTS.md#validating-source-style-locally) for local validation commands. Both workflows use read-only repository access; neither launches the desktop application or enables the experimental Hardware Device feature.

From the repository root, with **.NET 10 SDK** installed:

```bash
dotnet restore MangosDatabaseStudio.sln
dotnet build MangosDatabaseStudio.sln --configuration Release
```

To launch on Linux:

```bash
chmod +x scripts/linux/run-linux.sh
./scripts/linux/run-linux.sh
```

To launch on Windows:

```bat
scripts\windows\run-windows.bat
```

The desktop process uses a private local HTTP listener for its UI and requires a per-launch desktop session. Do not expose that listener on the network.

## Repository layout

```text
.
├── src/
│   ├── MangosDatabaseStudio.App/                 # Desktop host, MVC UI, and encrypted local state
│   ├── MangosDatabaseStudio.Application/         # Use-case contracts and interfaces
│   ├── MangosDatabaseStudio.Domain/              # Domain models and security permissions
│   ├── MangosDatabaseStudio.Infrastructure.MySql/ # MySQL/MariaDB access and viewers
│   ├── MangosDatabaseStudio.Schema/              # MaNGOS schema metadata
│   └── MangosDatabaseStudio.HardwareIdentityHelper/ # Experimental Linux helper
├── scripts/                                     # Run, validate, publish, and package scripts
├── packaging/linux/                             # Polkit policy and deployable helper scripts
├── .github/workflows/                           # Windows/Linux build and source style CI
├── tests/                                       # Style regression and Field Data integration checks
├── MangosDatabaseStudio.sln
├── REQUIREMENTS.md
├── PROJECT_RULES.md
├── CONTRIBUTING.md
└── scripts/check_style.py                      # Repository formatting checker
```

### Developer scripts

| Task | Linux | Windows |
| --- | --- | --- |
| Run from source | `./scripts/linux/run-linux.sh` | `scripts\windows\run-windows.bat` |
| Publish | `./scripts/linux/publish-linux.sh --self-contained` | `scripts\windows\publish-windows.bat --self-contained` |
| Create release | `./scripts/linux/package-app.sh` | `scripts\windows\package-app.bat` |
| Validate | `dotnet restore/build/test` | `scripts\windows\validate-windows.bat` |
| Optional firmware helper installation | `./scripts/linux/install-linux-hardware-helper.sh` | Not applicable |

Scripts resolve the repository root relative to their own location and may be invoked from any working directory. Installation of the optional Linux helper is **not required** to build or run the normal app.

## Application versioning

The version shown below **Sign out** is embedded when the .NET 10 application is built:

```text
Application: v0.2.5-beta (a1b2c3d4)
```

The version comes from the **nearest reachable Git tag**, supporting `v.MAJOR.MINOR.PATCH-SUFFIX` (for example `v.0.2.5-beta`) as well as `vMAJOR.MINOR.PATCH` (for example `v1.2.0`). The prerelease suffix is retained, and the eight-character hexadecimal value identifies the commit compiled into the application. Commits after a tag retain that release version but show a different commit hash. The executable does not need Git or internet access to display its version.

Create and push an annotated release tag **on the commit you intend to release**:

```bash
git tag -a v.0.2.5-beta -m "Release v.0.2.5-beta"
git push origin v.0.2.5-beta
```

If a tag was created on GitHub, run `git fetch --tags` locally before building. The Windows/Linux build workflow fetches Git history and tags automatically. When no valid tag is available, normal builds display `v0.0.0` with the current commit hash; without Git information the hash is `unknown`. For release packaging, the scripts use the latest reachable tag by default, or accept `--version` explicitly when building without tags.

```bash
./scripts/linux/package-app.sh --platform linux
```

```bat
scripts\windows\package-app.bat
```

You can use `--version 0.2.5-beta` to override Git-derived versioning when preparing an untagged package. The version is also embedded into published binaries, so copying the executable to another machine does not change the displayed version. Local uncommitted changes are not represented in the hash; commit the desired sources before creating a release.

## Data protection and local state

The application can create a local SQLCipher-encrypted SQLite database and separate key material in `src/MangosDatabaseStudio.App/App_Data/` when run from source. Connection credentials and user data belong to the local installation, **not the source repository**. The Git ignore rules exclude runtime data, encryption keys, avatars, temporary imports/exports, and build artifacts. Keep backups of encrypted data and its matching key material together; consult [PRIVACY.md](PRIVACY.md).

**Do not commit generated databases, SQL exports, saved connection profiles, authentication secrets, user avatars, or machine-specific keys.** If such data has ever been pushed to a public remote, removing it from the current commit does not remove it from Git history: assess rotation and history remediation before publishing.

## Contributing

Please read [PROJECT_RULES.md](PROJECT_RULES.md) for security and source-style rules, and [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming, commits, pull requests, review, and validation. Preserve the application's established design system in every new or modified view.

## License

The project source is offered under the **GNU Affero General Public License, version 3 or (at your option) any later version** (`AGPL-3.0-or-later`). C# source files include per-file license notices. See [LICENSE](LICENSE) and the [official GNU AGPL v3 text](https://www.gnu.org/licenses/agpl-3.0.html). Third-party packages retain their respective licenses; review those dependencies before redistribution.
