#!/usr/bin/env python3
"""Check repository formatting conventions without modifying source files.

The token scanner ignores strings and comments while identifying control statements,
so braces inside SQL, JavaScript templates, or explanatory comments are not treated
as executable control-flow braces.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import re
import sys

SOURCE_EXTENSIONS = {".cs", ".cshtml", ".js", ".css", ".sh", ".bat", ".ps1", ".py", ".json"}
EXCLUDED_DIRECTORIES = {".git", ".vscode", "bin", "obj", "node_modules", ".artifacts"}
LICENSE_NOTICE = (
    "// SPDX-License-Identifier: AGPL-3.0-or-later",
    "// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.",
)
COPYRIGHT_PATTERN = re.compile(
    r"^// Copyright \(C\) 20\d{2}(?:-20\d{2})? MaNGOS Database Studio contributors$"
)
CS_CONTROLS_WITH_CONDITION = {"if", "for", "foreach", "while", "switch", "lock", "fixed"}
JS_CONTROLS_WITH_CONDITION = {"if", "for", "while", "switch"}
UNCONDITIONAL_CONTROLS = {"else", "do", "try", "finally"}
FOLLOWING_CLAUSES = {"else", "catch", "finally"}


@dataclass(frozen=True)
class Token:
    text: str
    start: int
    end: int
    line: int


def tokens_without_literals(source: str, language: str) -> list[Token]:
    """Keep code punctuation while skipping comments and quoted literals."""
    tokens: list[Token] = []
    index = 0
    line = 1
    length = len(source)

    while index < length:
        char = source[index]

        if char in " \r\t\n":
            if char == "\n":
                line += 1

            index += 1
            continue

        if source.startswith("//", index):
            next_line = source.find("\n", index)
            index = length if next_line < 0 else next_line
            continue

        if source.startswith("/*", index):
            end = source.find("*/", index + 2)
            end = length if end < 0 else end + 2
            line += source.count("\n", index, end)
            index = end
            continue

        if char == "#" and language == "cs" and source[source.rfind("\n", 0, index) + 1:index].strip() == "":
            end = source.find("\n", index)
            index = length if end < 0 else end
            continue

        # C# normal, verbatim, interpolated, and raw strings (including a $ prefix).
        if language == "cs":
            raw = re.match(r'\$*"{3,}', source[index:])
            if raw:
                quotes = '"' * len(raw.group(0).lstrip('$'))
                # Only the run of quotes terminates the literal, not interpolation braces.
                endpos = source.find(quotes, index + len(raw.group(0)))
                end = length if endpos < 0 else endpos + len(quotes)
                line += source.count("\n", index, end)
                index = end
                continue

        prefix = ""
        if language == "cs":
            for candidate in ('$@"', '@$"', '@"', '$"', '"', "'"):
                if source.startswith(candidate, index):
                    prefix = candidate
                    break
        elif language == "js":
            if char in ('"', "'", "`"):
                prefix = char

        if prefix:
            quote = prefix[-1]
            verbatim = "@" in prefix
            start = index
            index += len(prefix)

            while index < length:
                current = source[index]
                if current == "\n":
                    line += 1

                if current == "\\" and not verbatim:
                    index += 2
                    continue

                if current == quote:
                    if verbatim and source.startswith(quote * 2, index):
                        index += 2
                        continue

                    index += 1
                    break

                index += 1

            # Do not inspect braces inside literals or template-string content.
            continue

        if char.isalpha() or char == "_":
            end = index + 1
            while end < length and (source[end].isalnum() or source[end] == "_"):
                end += 1
            tokens.append(Token(source[index:end], index, end, line))
            index = end
            continue

        tokens.append(Token(char, index, index + 1, line))
        index += 1

    return tokens


def match_paren(tokens: list[Token], opening: int) -> int | None:
    depth = 0

    for position in range(opening, len(tokens)):
        if tokens[position].text == "(":
            depth += 1
        elif tokens[position].text == ")":
            depth -= 1
            if depth == 0:
                return position

    return None


def inspect_controls(path: Path, content: str, language: str) -> list[str]:
    findings: list[str] = []
    tokens = tokens_without_literals(content, language)
    lines = content.splitlines()
    controls = CS_CONTROLS_WITH_CONDITION if language == "cs" else JS_CONTROLS_WITH_CONDITION

    # Every control brace is tracked separately. Nested braces are NOT subject
    # to an extra blank line immediately before their containing close brace.
    control_opens: dict[int, str] = {}

    for index, token in enumerate(tokens):
        keyword = token.text
        if keyword not in controls | UNCONDITIONAL_CONTROLS | {"catch", "using"}:
            continue

        if index > 0 and tokens[index - 1].text == ".":
            continue

        successor = index + 1
        if successor >= len(tokens):
            continue

        if keyword == "using":
            if language != "cs" or tokens[successor].text != "(":
                continue

            # Using declarations can be chained: "await using (...) await using (...) { ... }".
            # The second declaration is not the body of the first one.
            # Defer the chain to the last using statement that owns the braces.
        elif keyword in controls:
            # Switch expressions and non-control uses of names aren't blocks.
            if tokens[successor].text != "(":
                continue
        elif keyword == "else" and tokens[successor].text == "if":
            continue

        if tokens[successor].text == "(":
            closing = match_paren(tokens, successor)
            if closing is None:
                continue

            successor = closing + 1
            if successor >= len(tokens):
                continue

            # C# catch filters: catch (...) when (...) { ... }
            if keyword == "catch" and tokens[successor].text == "when":
                if successor + 1 < len(tokens) and tokens[successor + 1].text == "(":
                    filtered = match_paren(tokens, successor + 1)
                    if filtered is None:
                        continue

                    successor = filtered + 1
                    if successor >= len(tokens):
                        continue

            # A do-while statement terminates with "while (condition);".
            if keyword == "while" and tokens[successor].text == ";":
                continue

            # A using statement may be followed by another using statement
            # without a block; the final using owns the explicit braces.
            if keyword == "using" and tokens[successor].text in {"using", "await"}:
                continue

        opener = tokens[successor]
        if opener.text != "{":
            # Expression-bodied JavaScript callbacks are not control statements;
            # actual if/for/while/etc. blocks must always have explicit braces.
            findings.append(f"{path}:{token.line}: {keyword} requires a brace-delimited block")
            continue

        control_opens[successor] = keyword
        brace_line = lines[opener.line - 1].strip()
        if opener.line == tokens[successor - 1].line or not brace_line.startswith("{") or not re.fullmatch(r"\{\s*(?://.*)?", brace_line):
            findings.append(f"{path}:{opener.line}: opening brace for {keyword} must be on its own line")

    # Build brace pairs for body layout checks. This is syntax-aware enough to
    # ignore strings and comments without enforcing a particular indentation.
    open_braces: list[int] = []
    for index, token in enumerate(tokens):
        if token.text == "{":
            open_braces.append(index)
        elif token.text == "}" and open_braces:
            opener = open_braces.pop()
            if opener not in control_opens:
                continue

            line_index = token.line
            if line_index >= len(lines):
                continue

            next_line = lines[line_index].strip()
            if not next_line:
                continue

            # Closing an enclosing block isn't a new statement. Similarly,
            # else/catch/finally/while (for do-while) belongs to the same chain.
            if next_line.startswith("}") or re.match(r"^(else|catch|finally)\b", next_line):
                continue

            if control_opens[opener] == "do" and re.match(r"^while\s*\(", next_line):
                continue

            findings.append(f"{path}:{token.line}: add one blank line after the completed {control_opens[opener]!r} block")

    return findings


def inspect_file(path: Path, relative: Path) -> list[str]:
    findings: list[str] = []
    try:
        raw = path.read_bytes()
        content = raw.decode("utf-8-sig")
    except UnicodeError:
        return [f"{relative}: source file must use UTF-8"]

    lines = content.splitlines()
    if content and not content.endswith("\n"):
        findings.append(f"{relative}: missing final newline")

    if b"\r\n" in raw:
        findings.append(f"{relative}: use LF line endings, not CRLF")

    for number, line in enumerate(lines, 1):
        if line != line.rstrip():
            findings.append(f"{relative}:{number}: trailing whitespace")

        if "\t" in line:
            findings.append(f"{relative}:{number}: use spaces instead of tab characters")

    if path.suffix == ".cs":
        header_valid = (
            len(lines) >= 4
            and COPYRIGHT_PATTERN.fullmatch(lines[0]) is not None
            and tuple(lines[1:3]) == LICENSE_NOTICE
            and lines[3] == ""
            and (len(lines) == 4 or lines[4] != "")
        )
        if not header_valid:
            findings.append(f"{relative}: expected AGPL header followed by exactly one blank line")

        for number, line in enumerate(lines, 1):
            if re.match(r"^\s*///", line) or re.match(r"^\s*/\*\*", line):
                findings.append(f"{relative}:{number}: use // comments, not XML documentation")

    if path.suffix in (".cs", ".js"):
        findings.extend(inspect_controls(relative, content, "cs" if path.suffix == ".cs" else "js"))

    return findings


def check_repository(root: Path) -> list[str]:
    findings: list[str] = []
    file_count = 0

    for path in sorted(root.rglob("*")):
        if not path.is_file() or path.suffix not in SOURCE_EXTENSIONS:
            continue

        relative = path.relative_to(root)
        if any(part in EXCLUDED_DIRECTORIES for part in relative.parts):
            continue

        file_count += 1
        findings.extend(inspect_file(path, relative))

    print(f"Checked {file_count} source files under {root}")
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    root = args.root.resolve()

    if not (root / "MangosDatabaseStudio.sln").exists():
        parser.error(f"not a MaNGOS Database Studio source root: {root}")

    failures = check_repository(root)
    for failure in failures:
        print(f"ERROR: {failure}")

    if failures:
        print(f"Style validation failed: {len(failures)} issue(s)")
        return 1

    print("Style validation passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
