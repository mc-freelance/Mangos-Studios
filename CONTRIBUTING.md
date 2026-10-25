# Contributing to MaNGOS Database Studio

Thank you for contributing. Review [PROJECT_RULES.md](PROJECT_RULES.md) and [REQUIREMENTS.md](REQUIREMENTS.md) before beginning development.

## Before opening an issue or pull request

Search open issues/PRs for related work. For security flaws, do not post reproduction secrets or exploit details publicly; contact the maintainers privately through a channel they designate. Keep contributions limited to code and data you have the rights to share.

## Branches and scope

1. Fork the repository (if required), then branch from the repository's current default branch.
2. Use a descriptive branch name such as `fix/sidebar-resize`, `feat/gossip-search`, `docs/build-guide`, or `refactor/schema-metadata`.
3. Keep each pull request focused on a reviewable change. Avoid combining broad formatting passes with behavioral changes.
4. Update docs and tests when user-facing behavior, build requirements, interfaces, or database operations change.

## Commit rules

Use **Conventional Commits** in the imperative mood:

```text
fix: keep modal backdrops aligned with desktop zoom
feat: add search filters to pool viewer
docs: document Linux runtime requirements
refactor: extract parameterized query builder
```

Common prefixes include `feat`, `fix`, `docs`, `refactor`, `test`, `build`, `chore`, and `security`. Keep the subject short and specific; describe *why* in a commit body when the intent is not obvious. Avoid unrelated whitespace changes, generated binaries, machine-local settings, or secrets.

## Pull request process

1. Open a PR against the **default branch** (do not assume a permanent branch name).
2. Fill in the PR template: summary, issue reference, behavior and database impact, security considerations, validation, and screenshots for visual changes.
3. Run `dotnet restore`, `dotnet build -c Release`, and `dotnet test` on the .NET 10 solution; note any missing tests or platform-specific limitations. Run `python scripts/check_style.py` and Roslyn format verification (`dotnet format whitespace MangosDatabaseStudio.sln --verify-no-changes --no-restore`) before opening the PR.
4. Run `node --check` on any modified standalone `.js` files if Node.js is available.
5. Test native desktop behavior on the affected platform. For UI work, include desktop/narrow-view checks, modal overlays, and application zoom.
6. For SQL and schema changes, explain permissions, affected tables/expansions, and recovery/rollback strategy.
7. Request review and address feedback through follow-up commits. Do not force-push a shared branch while another contributor is reviewing without coordination.

A PR should not be merged until a maintainer approves it and required project checks pass. Maintainers may ask that unusually large changes be split into smaller PRs.

## Licensing and attribution

By contributing, you confirm you have the right to submit your work under **AGPL-3.0-or-later** as indicated in [LICENSE](LICENSE). Preserve upstream notices where third-party material is permitted. Do not paste code, source assets, or datasets with uncertain redistribution rights.
