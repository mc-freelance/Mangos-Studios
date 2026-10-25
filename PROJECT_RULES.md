# Project Rules

These rules apply to source code, documentation, database operations, visual changes, and contributions to MaNGOS Database Studio.

## 1. License and source ownership

- Preserve existing copyright notices and add the approved **AGPL-3.0-or-later** notice at the start of every new `.cs` file; leave exactly one empty line after the header.
- Do not add XML documentation (`///` or `/** ... */` documentation blocks). Use concise, accurate `//` comments for classes, public methods, and non-obvious private logic. Explain *why* and document expectations or risks, not obvious language syntax.
- Obtain permission before importing code or assets under incompatible licenses. Keep third-party license and attribution notices intact.
- Public contributions must not include personal data, local credentials, runtime databases, key files, private server information, or copied game assets whose redistribution rights are unclear.

## 2. C# style and maintainability

- Continue targeting **.NET 10 (`net10.0`) and C# 14**, unless a separate approved change updates the tooling and target framework.
- Open braces for all control statements on their **own line**, including `if`, `else if`, `else`, `for`, `foreach`, `while`, `do`, `switch`, `using`, and `try`/`catch`/`finally`.
- Always include braces for control blocks; avoid compact single-line conditional statements.
- Add a blank line after the **closing brace of a completed outer control block** before starting the next statement, where it improves separation. Do **not** insert blank lines between a nested block's final brace and its enclosing closing brace.
- Declare variables separately, keep lines readable, and never compress multiple unrelated statements or Razor elements into one line.
- Keep filenames, namespaces, names, nullable annotations, and async patterns consistent with the surrounding project.
- Document newly introduced public functions and security-sensitive routines with accurate `//` comments, not boilerplate summaries.

Preferred style:

```csharp
if (condition)
{
    foreach (var item in items)
    {
        Process(item);
    }
}

if (anotherCondition)
{
    HandleNextStep();
}
```

## 3. User interface and accessibility

- Match existing viewer cards, action buttons, form styles, tables, pagination, and spacing; do not introduce an unfinished or inconsistent layout.
- Check desktop and narrow-window responsiveness, several application zoom levels, and popup/backdrop alignment.
- Keep JavaScript and CSS legible; comment unusual DOM synchronization, zoom compensation, or security boundaries when the intent is not apparent.
- Preserve the current state of searches and pagination where the viewer's navigation design requires it.

## 4. Security and database integrity

- Enforce authorization **on the server**, not only by hiding UI controls.
- Use parameterized SQL values; quote identifiers through the project's safe identifier helpers, and do not trust arbitrary table or column names.
- Do not weaken the desktop session gate, loopback-only binding, encryption, audit/change history, schema edit restrictions, or defensive input validation.
- Make schema and data changes compatible with the supported MaNGOS expansion, and document rollback behavior for reversible mutations.
- **Hardware Device / Device Identity remains WIP and disabled by default;** changes to it require explicit review and must not silently enable privileged device access.

## 5. Testing and review

- Build the affected solution with .NET 10; run existing tests and add tests for new logic where practicable.
- Perform manual checks for data modification, permission boundaries, and user-facing pages if there is no automated coverage.
- Confirm that no secrets, runtime files, generated artifacts, or unexpected binary files are staged.
- Include concise comments or documentation for new functions, complex operations, and noteworthy behavior changes.
- Run `python scripts/check_style.py` and verify Roslyn formatting with `dotnet format whitespace MangosDatabaseStudio.sln --verify-no-changes --no-restore` after restoring dependencies. See [.github/workflows/code-style.yml](.github/workflows/code-style.yml).
- Follow the branch, commit, and PR process in [CONTRIBUTING.md](CONTRIBUTING.md).
