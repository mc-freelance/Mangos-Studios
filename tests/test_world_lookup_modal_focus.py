"""Ensure shared lookup searches remain focusable inside Bootstrap modals."""

from pathlib import Path
import unittest


PROJECT_ROOT = Path(__file__).resolve().parents[1]
PICKER_SCRIPT = PROJECT_ROOT / "src/MangosDatabaseStudio.App/wwwroot/js/world-lookup-picker.js"
VIEW_ROOT = PROJECT_ROOT / "src/MangosDatabaseStudio.App/Views/WorldEditor"


class WorldLookupModalFocusTests(unittest.TestCase):
    def test_popover_is_hosted_by_the_active_modal(self):
        """Bootstrap must recognize the lookup's search field as modal content."""
        script = PICKER_SCRIPT.read_text()
        self.assertIn('picker.closest(".modal") || document.body', script)
        self.assertIn('host.appendChild(popover);', script)
        self.assertIn('document.addEventListener("hide.bs.modal"', script)
        self.assertIn('document.body.appendChild(popover);', script)

    def test_quest_add_dialog_uses_the_shared_lookup(self):
        """The Add Existing Quest dialog must retain its searchable lookup."""
        view = (VIEW_ROOT / "Quests.cshtml").read_text()
        modal_start = view.index('id="questAddExistingModal"')
        modal_end = view.index('id="questEditModal"', modal_start)
        add_dialog = view[modal_start:modal_end]
        self.assertIn('data-world-lookup-picker', add_dialog)
        self.assertIn('data-world-lookup-search-placeholder="Search quest title or ID"', add_dialog)


if __name__ == "__main__":
    unittest.main()
