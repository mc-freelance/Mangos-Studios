"""Protect local login throttling, audit storage and middleware wiring."""
from pathlib import Path
import re
import sqlite3
import unittest

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'MangosDatabaseStudio.App'


class AuthenticationSecurityTests(unittest.TestCase):
    def test_authentication_guard_and_policy(self):
        source = (APP / 'Services' / 'LoginAttemptProtector.cs').read_text()
        for requirement in ('AuthenticateAsync(', 'WriteAuditAsync(', 'SaveFailureAsync(',
                            'GetDelay(int failures)', 'Math.Min(300',
                            'TimeSpan.FromDays(90)', 'OFFSET 5000', 'finally', '_gate.Release()'):
            self.assertIn(requirement, source)
        self.assertNotIn('password, $', source)

    def test_sqlite_authentication_tables_are_valid(self):
        source = (APP / 'Services' / 'ApplicationDatabase.cs').read_text()
        connection = sqlite3.connect(':memory:')
        for table in ('authentication_throttle', 'authentication_audit'):
            match = re.search(r'CREATE TABLE IF NOT EXISTS '+table+r'\s*\(.*?\);',source,re.S)
            self.assertIsNotNone(match, table)
            connection.executescript(match.group(0))
        connection.execute("INSERT INTO authentication_audit(username,outcome,occurred_at_utc) VALUES ('admin','failure','2026-10-30T00:00:00Z')")
        connection.execute("INSERT INTO authentication_throttle(username,failure_count,last_failure_utc,blocked_until_utc) VALUES ('ADMIN',3,'2026-10-30T00:00:00Z','2026-10-30T00:00:02Z')")
        self.assertEqual(connection.execute('SELECT failure_count FROM authentication_throttle WHERE username = ?',('admin',)).fetchone()[0],3)
        self.assertEqual(connection.execute('SELECT outcome FROM authentication_audit').fetchone()[0],'failure')
        self.assertGreaterEqual(int(re.search(r'CurrentSchemaVersion = (\d+)',source).group(1)),8)

    def test_login_endpoint_and_pipeline_rate_limited(self):
        controller = (APP / 'Controllers' / 'AccountController.cs').read_text()
        program = (APP / 'Program.cs').read_text()
        self.assertIn('EnableRateLimiting("local-login")', controller)
        self.assertIn('_loginAttempts.AuthenticateAsync', controller)
        self.assertIn('AddFixedWindowLimiter("local-login"', program)
        self.assertIn('app.UseRateLimiter();', program)
        self.assertLess(program.index('app.UseRouting();'), program.index('app.UseRateLimiter();'))
        self.assertLess(program.index('app.UseRateLimiter();'), program.index('app.UseAuthorization();'))
        self.assertIn('AddSingleton<LoginAttemptProtector>()', program)

    def test_does_not_log_credentials(self):
        source = (APP / 'Services' / 'LoginAttemptProtector.cs').read_text()
        self.assertNotRegex(source, r'Log\w+\([^\n]+password')
        self.assertNotIn('parameters.AddWithValue("$password"', source.lower())


if __name__ == '__main__':
    unittest.main()
