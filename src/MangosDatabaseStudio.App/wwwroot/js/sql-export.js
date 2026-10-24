// sql-export user-interface behavior; keep DOM updates scoped to the active view.
(function () {
    "use strict";

    function initialize() {
        const selector = document.querySelector("[data-export-table-selector]");
        if (!selector)
        {
            return;
        }

        const tableRows = Array.from(selector.querySelectorAll("[data-export-table]"));
        const checkboxes = tableRows
            .map(row => row.querySelector('input[type="checkbox"][name="SelectedTables"]'))
            .filter(Boolean);
        const count = selector.querySelector("[data-export-selected-count]");
        const filter = selector.querySelector("[data-export-table-filter]");
        const selectAll = selector.querySelector("[data-export-select-all]");
        const clear = selector.querySelector("[data-export-clear]");
        const submit = document.querySelector("[data-export-submit]");

        const updateCount = () => {
            const selectedCount = checkboxes.filter(input => input.checked).length;
            if (count)
            {
                count.textContent = String(selectedCount);
            }

            if (submit)
            {
                submit.disabled = selectedCount === 0;
            }
        };

        const setSelection = selected => {
            checkboxes.forEach(checkbox => { checkbox.checked = selected; });
            updateCount();
        };

        checkboxes.forEach(input => input.addEventListener("change", updateCount));
        selectAll?.addEventListener("click", () => setSelection(true));
        clear?.addEventListener("click", () => setSelection(false));
        filter?.addEventListener("input", () => {
            const query = String(filter.value || "").trim().toLowerCase();
            tableRows.forEach(row => {
                row.hidden = query.length > 0 && !String(row.dataset.exportTableName || "").includes(query);
            });
        });

        updateCount();
    }

    if (document.readyState === "loading")
    {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    }
    else
    {
        initialize();
    }
})();
