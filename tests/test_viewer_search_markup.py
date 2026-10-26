"""Guard shared World Viewer search inputs against inaccessible markup."""

from pathlib import Path
import re
import unittest


PROJECT_ROOT = Path(__file__).resolve().parents[1]
VIEW_ROOT = PROJECT_ROOT / "src/MangosDatabaseStudio.App/Views/WorldEditor"
MAIN_VIEWERS = {
    "Events": "eventSearch",
    "Gossip": "gossipSearch",
    "Loot": "lootSearch",
    "Menus": "gossipSearch",
    "NpcTrainers": "trainerSearch",
    "NpcVendors": "vendorSearch",
    "Pools": "poolSearch",
    "Quests": "questSearch",
}
FILTERED_VIEWERS = {"Gossip", "Loot", "Pools"}


class ViewerSearchMarkupTests(unittest.TestCase):
    def test_primary_search_has_independent_labels(self):
        """The search input must not share a wrapping label with a select."""
        for name, search_id in MAIN_VIEWERS.items():
            with self.subTest(viewer=name):
                page = (VIEW_ROOT / f"{name}.cshtml").read_text()
                self.assertIn('<div class="viewer-search-field">', page)
                self.assertIn(f'<label for="{search_id}">', page)
                self.assertIn(
                    f'<label class="input-group-text" for="{search_id}" aria-hidden="true">',
                    page,
                )
                self.assertRegex(page, rf'<input\s+id="{search_id}"\s+type="search"')
                self.assertNotIn('<label class="viewer-search-field"', page)

    def test_combined_search_and_filter_are_separate_controls(self):
        """A native select must not be nested in another control's label."""
        for name in FILTERED_VIEWERS:
            with self.subTest(viewer=name):
                page = (VIEW_ROOT / f"{name}.cshtml").read_text()
                self.assertRegex(page, r'<div class="viewer-search-field">[\s\S]*?<select')
                self.assertNotRegex(page, r'<label class="viewer-search-field"[^>]*>')

    def test_spawn_name_search_is_labeled(self):
        """The spawn finder uses the same accessible search pattern."""
        page = (VIEW_ROOT / "DoubleSpawns.cshtml").read_text()
        self.assertIn('<label for="doubleSpawnNameSearch">Name search</label>', page)
        self.assertIn('id="doubleSpawnNameSearch" type="search"', page)
        self.assertIn('for="doubleSpawnNameSearch" aria-hidden="true"', page)

    def test_css_keeps_new_labels_styled(self):
        """Explicit labels should retain the original viewer-control appearance."""
        premium = (PROJECT_ROOT / "src/MangosDatabaseStudio.App/wwwroot/css/themes/premium.css").read_text()
        base = (PROJECT_ROOT / "src/MangosDatabaseStudio.App/wwwroot/css/site.css").read_text()
        self.assertIn('.viewer-search-field > label {', premium)
        self.assertIn('.double-spawn-filter-field > label {', base)


if __name__ == "__main__":
    unittest.main()
