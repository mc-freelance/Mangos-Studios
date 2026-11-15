"""Ensure imports and exports cannot execute untrusted PATH-resolved clients."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src/MangosDatabaseStudio.App"
INFRA = ROOT / "src/MangosDatabaseStudio.Infrastructure.MySql"


class NativeClientSecurityTests(unittest.TestCase):
    def test_no_path_automatic_discovery(self):
        program = (APP / "Program.cs").read_text()
        self.assertNotIn("FindExecutable(", program)
        self.assertNotIn("ConfigureDatabaseClientPaths(", program)
        self.assertNotIn('GetEnvironmentVariable("PATH")', program)
        options = (INFRA / "MySqlOptions.cs").read_text()
        settings = (APP / "appsettings.json").read_text()
        self.assertIn('ImportClientPath { get; set; } = string.Empty;', options)
        self.assertIn('ExportClientPath { get; set; } = string.Empty;', options)
        self.assertIn('"ImportClientPath": ""', settings)
        self.assertIn('"ExportClientPath": ""', settings)

    def test_explicit_validation_at_both_execution_boundaries(self):
        imp = (INFRA / "SqlImportService.cs").read_text()
        exp = (INFRA / "SqlExportService.cs").read_text()
        self.assertIn("TrustedDatabaseClientExecutable.Resolve(_options.ImportClientPath, isExport: false)", imp)
        self.assertIn("TrustedDatabaseClientExecutable.Resolve(_options.ExportClientPath, isExport: true)", exp)
        self.assertIn("FileName = clientExecutable,", imp)
        self.assertIn("FileName = clientExecutable,", exp)
        self.assertNotIn("FileName = _options.ImportClientPath", imp)
        self.assertNotIn("FileName = _options.ExportClientPath", exp)

    def test_trust_policy_checks_permissions_and_links(self):
        policy = (INFRA / "TrustedDatabaseClientExecutable.cs").read_text()
        for safeguard in (
            "Path.IsPathFullyQualified", "ResolveLinkTarget", "VerifyParentDirectories",
            "FileAttributes.ReparsePoint", "File.GetUnixFileMode", "UnixFileMode.OtherWrite",
            "UnixFileMode.GroupWrite", "GetAccessRules", "WindowsIdentity.GetCurrent",
        ):
            with self.subTest(safeguard=safeguard):
                self.assertIn(safeguard, policy)


if __name__ == "__main__":
    unittest.main()
