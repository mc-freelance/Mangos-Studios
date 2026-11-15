"""Regression checks for cross-provider TLS safeguards and persisted CA paths."""

import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


def source(relative):
    return (ROOT / relative).read_text(encoding="utf-8")


class MySqlTlsSecurityTests(unittest.TestCase):
    def test_new_profiles_default_to_verified_tls(self):
        domain = source("src/MangosDatabaseStudio.Domain/Connections/ConnectionProfile.cs")
        model = source("src/MangosDatabaseStudio.App/Models/ConnectionModels.cs")
        self.assertIn('SslMode { get; set; } = "VerifyFull"', domain)
        self.assertIn('SslMode { get; set; } = "VerifyFull"', model)
        self.assertIn("AcknowledgeInsecureTls", model)

    def test_saved_tls_policy_includes_ca_and_migrates_existing_database(self):
        schema = source("src/MangosDatabaseStudio.App/Services/ApplicationDatabase.cs")
        store = source("src/MangosDatabaseStudio.App/Services/SqliteConnectionProfileStore.cs")
        self.assertIn('"connection_profiles",\n                "ssl_ca"', schema)
        self.assertIn('ssl_ca TEXT NOT NULL DEFAULT', schema)
        self.assertIn("ssl_ca = $sslCa", store)
        self.assertIn('SslCa = reader.GetString(8)', store)

    def test_managed_connection_rejects_unrecognized_modes(self):
        factory = source("src/MangosDatabaseStudio.Infrastructure.MySql/Internal/MySqlConnectionFactory.cs")
        policy = source("src/MangosDatabaseStudio.Domain/Connections/ConnectionTlsSecurity.cs")
        self.assertIn("ConnectionTlsSecurity.ValidateProfile(profile)", factory)
        self.assertIn("builder.SslCa", factory)
        self.assertIn("Unsupported TLS mode", policy)
        self.assertIn("Path.IsPathFullyQualified", policy)

    def test_native_clients_must_use_same_certificate_verification_policy(self):
        defaults = source("src/MangosDatabaseStudio.Infrastructure.MySql/Internal/MySqlClientDefaultsFile.cs")
        self.assertIn("ssl-mode=", defaults)
        self.assertIn("VERIFY_IDENTITY", defaults)
        self.assertIn("ssl-verify-server-cert", defaults)
        self.assertIn("ssl-ca=", defaults)
        self.assertIn("IdentifyClientDialectAsync", defaults)
        self.assertIn("UnixCreateMode", defaults)
        for file in ("SqlImportService.cs", "SqlExportService.cs"):
            self.assertIn("MySqlClientDefaultsFile.CreateAsync(profile, clientExecutable,",
                          source("src/MangosDatabaseStudio.Infrastructure.MySql/" + file))

    def test_weak_modes_need_explicit_acknowledgement(self):
        controller = source("src/MangosDatabaseStudio.App/Controllers/ConnectionsController.cs")
        self.assertEqual(controller.count("ValidateTlsSettings(model);"), 2)
        self.assertIn("!model.AcknowledgeInsecureTls", controller)
        index = source("src/MangosDatabaseStudio.App/Views/Connections/Index.cshtml")
        self.assertIn("RequiresRiskAcknowledgement(profile.SslMode)", index)


if __name__ == "__main__":
    unittest.main()
