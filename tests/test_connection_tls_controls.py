"""Guard the connection editor's conditional TLS controls and compact warnings."""

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8")


class ConnectionTlsControlsTests(unittest.TestCase):
    def test_editor_has_explicit_control_targets(self):
        editor = source("src/MangosDatabaseStudio.App/Views/Connections/Edit.cshtml")
        for name in (
            "data-connection-tls-editor",
            "data-tls-mode",
            "data-tls-ca-group",
            "data-tls-ca-input",
            "data-tls-risk-group",
            "data-tls-risk-message",
            "data-tls-risk-acknowledgment",
            "~/js/connection-tls.js",
        ):
            self.assertIn(name, editor)
        self.assertIn('option value="None"', editor)
        self.assertIn('option value="Disabled"', editor)

    def test_browser_hides_ca_for_both_no_tls_modes(self):
        code = source("src/MangosDatabaseStudio.App/wwwroot/js/connection-tls.js")
        self.assertIn('mode === "Disabled" || mode === "None"', code)
        self.assertIn("caGroup.hidden = noTls", code)
        self.assertIn("caInput.disabled = noTls", code)
        self.assertIn("caInput.required = needsCa", code)
        self.assertIn("riskGroup.hidden = verified", code)

    def test_server_does_not_require_or_store_ca_for_no_tls(self):
        rules = source("src/MangosDatabaseStudio.Domain/Connections/ConnectionTlsSecurity.cs")
        model = source("src/MangosDatabaseStudio.App/Models/ConnectionModels.cs")
        self.assertIn('mode != "Disabled" && !string.IsNullOrEmpty(caPath)', rules)
        self.assertIn('ConnectionTlsSecurity.IsTlsDisabled(SslMode) ? string.Empty', model)
        controller = source("src/MangosDatabaseStudio.App/Controllers/ConnectionsController.cs")
        self.assertIn('!model.AcknowledgeInsecureTls', controller)
        self.assertIn('ModelState.Remove(nameof(model.SslCa))', controller)

    def test_connection_cards_use_short_security_message(self):
        view = source("src/MangosDatabaseStudio.App/Views/Connections/Index.cshtml")
        self.assertIn("No TLS — local use only", view)
        self.assertIn("Hostname not verified", view)
        self.assertIn("Certificate not verified", view)
        self.assertIn('class="connection-security-note"', view)


if __name__ == "__main__":
    unittest.main()
