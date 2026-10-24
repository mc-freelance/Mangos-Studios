"""Regression tests for the repository-specific style checker."""

from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))

import check_style  # noqa: E402


class StyleCheckerTests(unittest.TestCase):
    def inspect(self, source: str, language: str = "cs") -> list[str]:
        return check_style.inspect_controls(Path("example." + language), source, language)

    def test_accepts_nested_blocks_without_blank_line_between_closing_braces(self) -> None:
        source = """if (outer)
{
    if (inner)
    {
        Run();
    }
}

Continue();
"""
        self.assertEqual([], self.inspect(source))

    def test_rejects_opening_brace_on_the_control_line(self) -> None:
        source = """if (ready) {
    Run();
}
"""
        self.assertTrue(any("opening brace" in item for item in self.inspect(source)))

    def test_rejects_missing_control_block_braces(self) -> None:
        source = """if (ready)
    Run();
"""
        self.assertTrue(any("requires a brace" in item for item in self.inspect(source)))

    def test_rejects_missing_blank_line_after_finished_block(self) -> None:
        source = """if (ready)
{
    Run();
}
Continue();
"""
        self.assertTrue(any("blank line" in item for item in self.inspect(source)))

    def test_allows_else_and_catch_to_follow_without_blank_line(self) -> None:
        source = """if (ready)
{
    Run();
}
else
{
    Skip();
}

try
{
    Run();
}
catch (Exception)
{
    Skip();
}
"""
        self.assertEqual([], self.inspect(source))

    def test_allows_filtered_catches_and_do_while(self) -> None:
        source = """try
{
    Run();
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    Log();
}

do
{
    Iterate();
}
while (changed);
"""
        self.assertEqual([], self.inspect(source))

    def test_ignores_strings_and_comments_with_braces(self) -> None:
        source = """var text = \"if (false) { Run(); }\";
// if (false) Run();
if (enabled)
{
    Execute();
}
"""
        self.assertEqual([], self.inspect(source))

    def test_handles_javascript_around_template_strings(self) -> None:
        source = """const template = `if (broken) {}`;
if (enabled)
{
    Execute();
}
"""
        self.assertEqual([], self.inspect(source, "js"))

    def test_ignores_csharp_raw_and_interpolated_raw_strings(self) -> None:
        source = '''var sql = """if (broken) { DoNotRun(); }""";
var command = $$"""SELECT { "if (broken) { DoNotRun(); }" }""";
if (enabled)
{
    Run();
}
'''
        self.assertEqual([], self.inspect(source))

    def test_detects_javascript_inline_else(self) -> None:
        source = """if (enabled)
{
    Execute();
}
else Skip();
"""
        self.assertTrue(any("else requires a brace" in item for item in self.inspect(source, "js")))

    def test_header_is_required_for_csharp(self) -> None:
        from tempfile import TemporaryDirectory

        with TemporaryDirectory() as tmp:
            path = Path(tmp) / "Example.cs"
            path.write_text("class Example {}\n", encoding="utf-8")
            self.assertTrue(any("AGPL header" in item for item in check_style.inspect_file(path, path)))


if __name__ == "__main__":
    unittest.main()
