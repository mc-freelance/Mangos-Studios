"""Regression checks for CSP-safe markup and the private desktop security middleware."""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "MangosDatabaseStudio.App"


class DesktopSecurityHeadersTests(unittest.TestCase):
    def test_response_headers_cover_desktop_gate_and_startup(self):
        middleware = (APP / "Desktop" / "DesktopSecurityHeaders.cs").read_text()
        program = (APP / "Program.cs").read_text()
        for header in ("Content-Security-Policy", "X-Content-Type-Options", "X-Frame-Options",
                       "Referrer-Policy", "Permissions-Policy"):
            self.assertIn('headers["' + header + '"]', middleware)
        self.assertIn("RandomNumberGenerator.GetBytes(24)", middleware)
        self.assertIn("context.Response.OnStarting", middleware)
        self.assertLess(program.index("app.Use(DesktopSecurityHeaders.InvokeAsync)"),
                        program.index("if (!sessionGate.IsAuthorized(context))"))
        self.assertIn("DesktopSecurityHeaders.GetNonce(context)", program)
        self.assertIn('"script-src-attr \'none\'"', middleware)
        self.assertNotIn('"script-src \'unsafe-inline\'"', middleware)
        self.assertNotIn('"script-src \'unsafe-eval\'"', middleware)
        for directive in ("frame-ancestors 'none'", "object-src 'none'", "base-uri 'self'",
                          "form-action 'self'", "connect-src 'self'"):
            self.assertIn(directive, middleware)

    def test_startup_page_uses_fresh_nonce_on_both_inline_elements(self):
        startup = (APP / "Desktop" / "DesktopStartupPage.cs").read_text()
        self.assertEqual(2, startup.count("__MDS_STARTUP_NONCE__"))
        self.assertIn('<style nonce=""__MDS_STARTUP_NONCE__"">', startup)
        self.assertIn('<script nonce=""__MDS_STARTUP_NONCE__"">', startup)

    def test_no_executable_inline_scripts_or_inline_event_handlers_in_views(self):
        views = list((APP / "Views").rglob("*.cshtml"))
        self.assertGreater(len(views), 40)
        for view in views:
            content = view.read_text()
            with self.subTest(view=str(view.relative_to(APP))):
                self.assertNotRegex(content, r'\bon(?:click|change|input|load|error|submit|keydown|keyup)\s*=')
                for tag in re.finditer(r'<script\b([^>]*)>', content):
                    attrs = tag.group(1)
                    if re.search(r'\btype\s*=\s*["\']application/json["\']', attrs):
                        continue
                    self.assertRegex(attrs, r'\bsrc\s*=')
                    match = re.search(r'\bsrc="~/js/([\w-]+\.js)"', attrs)
                    if match:
                        self.assertTrue((APP / "wwwroot/js" / match.group(1)).is_file())

    def test_filter_submission_uses_shared_script(self):
        site = (APP / "wwwroot/js/site.js").read_text()
        self.assertIn('document.addEventListener("change", event =>', site)
        self.assertIn('control.matches("[data-auto-submit-on-change]")', site)
        self.assertIn('control.form?.submit()', site)
        markup = ''.join(p.read_text() for p in (APP / "Views").rglob("*.cshtml"))
        self.assertGreater(markup.count("data-auto-submit-on-change"), 15)


if __name__ == "__main__":
    unittest.main()
