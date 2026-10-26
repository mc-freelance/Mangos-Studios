# Tagged binary releases

MaNGOS Database Studio publishes **portable, self-contained .NET 10 desktop binaries** using [`.github/workflows/release.yml`](.github/workflows/release.yml). The release workflow runs **only after pushing a version tag**; normal source commits and pull requests do not upload binary releases.

## Release targets

| OS | Architecture | GitHub Release file | How to run |
| --- | --- | --- | --- |
| Windows | x64 | `MangosDatabaseStudio-VERSION-win-x64.zip` | Extract; open `MangosDatabaseStudio.exe` |
| Windows | ARM64 | `MangosDatabaseStudio-VERSION-win-arm64.zip` | Extract; open `MangosDatabaseStudio.exe` |
| Linux (Ubuntu, Arch and other compatible distributions) | x64 | `MangosDatabaseStudio-VERSION-linux-x64.tar.gz` | Extract; run `./start.sh` |
| Linux | ARM64 | `MangosDatabaseStudio-VERSION-linux-arm64.tar.gz` | Extract; run `./start.sh` |
| macOS | Intel x64 | `MangosDatabaseStudio-VERSION-osx-x64.tar.gz` | Extract; run `./start.sh` |
| macOS | Apple Silicon ARM64 | `MangosDatabaseStudio-VERSION-osx-arm64.tar.gz` | Extract; run `./start.sh` |

Each binary is built by a runner with the corresponding OS and CPU architecture, which helps select native Photino dependencies. Packages are **self-contained**, but desktop/native libraries still matter:

- **Windows**: WebView2 Runtime may be needed. No .NET SDK installation is necessary.
- **Linux**: GTK 3 and the WebKitGTK library compatible with the bundled Photino version are required. Exact package names differ across Ubuntu, Debian, Arch, and other distributions; verify availability before advertising a distribution as tested. On Linux, extract before running so file permissions are retained.
- **macOS**: This initial portable TAR.GZ is **not** a signed/notarized application bundle or DMG. Apple's Gatekeeper can warn about downloads, and usability should be tested on both Intel and Apple Silicon before recommending general release. Extract into a writable user directory; the application maintains its `App_Data` next to the published executable.

Published binaries do not install operating-system packages. MySQL/MariaDB command-line clients remain optional for SQL import/export functionality. The Hardware Device / Device Identity feature is **WIP, off by default, and may be removed**; these portable packages do not ship the optional privileged Linux hardware helper.

## Publishing a new tag

1. Merge and verify the source you want to release on `master` (or your chosen release branch).
2. Ensure the Git working tree is clean and that the current commit has passed source-formatting, Windows/Linux build, and integration tests.
3. Make a **new** version tag on the intended commit, using either `v.0.2.5-beta` or `v1.0.0` style. Avoid reusing an existing release tag.
4. Push that **tag** to GitHub (a branch push alone will not publish release assets).

```bash
git checkout master
git pull --ff-only origin master
git tag -a v.0.2.6-beta -m "Release v.0.2.6-beta"
git push origin v.0.2.6-beta
```

This example uses a future illustrative tag; use the next version you actually intend to publish. Never move a publicly released tag as a workaround for a bad build. Fix the problem in another commit and create a new tag.

The release workflow validates the tag against `HEAD`, builds and packages the six runtime identifiers, verifies their checksums, and creates/updates the GitHub Release with the assets **only if all jobs succeed**. Prerelease tags containing a hyphen (`-beta`, `-rc.1`, etc.) are published as GitHub prereleases. Each archive also includes a matching `<archive>.sha256` sidecar on the Release page.

View the workflow at [GitHub Actions](https://github.com/mc-freelance/Mangos-Studios/actions) and download releases at [GitHub Releases](https://github.com/mc-freelance/Mangos-Studios/releases).

## Build one platform locally

The release script requires Python 3.12 or newer, Git, .NET 10 SDK, and a tag on the current commit:

```bash
python scripts/release/package_release.py validate-tag --tag v.0.2.5-beta
python scripts/release/package_release.py package --tag v.0.2.5-beta --rid linux-x64
```

On Windows, use `python` and `--rid win-x64`. On macOS, use `python3` and `--rid osx-arm64` or `osx-x64` as appropriate. Do not substitute an unrelated tag: this script intentionally refuses if the tag does not point at `HEAD`.

The assets appear in `.artifacts/release/`. The script never uploads anything itself; publishing is done only by the tag-triggered workflow after all native runner jobs succeed. The standalone packager is meant for producing one target on the corresponding operating system.

## Data safety and redistribution

The packager deletes all published `App_Data` contents and recreates empty writable directories. It refuses to package any unexpected SQLite database, key, PFX, PEM, or other sensitive runtime file. Never attach your local Studio database or private encryption keys to GitHub Releases. User data is **not** migrated automatically between extracted folders; users should back up their own `App_Data/database` and `App_Data/keys` together before upgrading.

Release archives contain `LICENSE` (the complete AGPL v3 text), a build-information file, package instructions, SHA-256 checksums, and relevant project documentation. If the repository lacks `LICENSE`, the packager retrieves the canonical text from [GNU](https://www.gnu.org/licenses/agpl-3.0.txt) and **fails closed** if it cannot retrieve and validate it. Commit the official full license text to the repository to remove this external dependency.

## Before making the first public binary release

- Test native desktop startup and connection workflows on Windows x64/ARM64, Ubuntu/Arch Linux, and macOS Intel/Apple Silicon. A successful `dotnet publish` does not prove native Photino startup succeeds on every target.
- Confirm desktop dependencies, minimum OS versions, and any third-party license requirements.
- Verify the archive contains no locally created database, keys, credentials, or test data.
- For macOS distribution to a broader audience, plan proper signing/notarization and a user-writable data directory before offering `.app`/DMG packages.
- Prefer immutable published tags and require successful GitHub build/style checks before tagging.
