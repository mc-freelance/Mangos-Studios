// field-data user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const form = document.querySelector("[data-field-data-definition]");
    if (!form)
    {
        return;
    }

    const selection = form.querySelector("[data-field-data-selection]");
    const handling = form.querySelector("[data-field-data-handling]");
    const preserve = form.querySelector("[data-field-data-preserve]");
    const preserveRow = form.querySelector("[data-field-data-preserve-row]");
    const tableInput = form.querySelector("[data-field-data-table]");
    const columnInput = form.querySelector("[data-field-data-column]");
    const columnStatus = form.querySelector("[data-field-data-column-status]");

    const refreshHandling = () => {
        if (!selection || !handling)
        {
            return;
        }

        const multiple = selection.value === "Multiple";
        Array.from(handling.options).forEach(option => {
            if (option.value === "Direct")
            {
                option.disabled = multiple;
            }
            else
            {
                option.disabled = !multiple;
            }
        });

        if (!multiple)
        {
            handling.value = "Direct";
        }
        else if (handling.value === "Direct")
        {
            handling.value = "BitwiseOr";
        }

        const canPreserve = multiple && handling.value === "BitwiseOr";
        if (preserve)
        {
            preserve.disabled = !canPreserve;
            if (!canPreserve)
            {
                preserve.checked = false;
            }
        }

        if (preserveRow)
        {
            preserveRow.classList.toggle("is-disabled", !canPreserve);
        }
    };

    const setColumnPlaceholder = (message) => {
        if (!(columnInput instanceof HTMLSelectElement))
        {
            return;
        }

        columnInput.replaceChildren();
        const option = document.createElement("option");
        option.value = "";
        option.textContent = message;
        columnInput.appendChild(option);
        columnInput.value = "";
    };

    const loadColumns = async (preserveSelection) => {
        if (!(tableInput instanceof HTMLSelectElement) || !(columnInput instanceof HTMLSelectElement) || !columnStatus)
        {
            return;
        }

        const table = tableInput.value.trim();
        const previousColumn = preserveSelection ? columnInput.value.trim() : "";

        if (!table)
        {
            setColumnPlaceholder("Select a table first...");
            columnInput.disabled = true;
            columnStatus.textContent = "Choose a table to load its available columns.";
            return;
        }

        setColumnPlaceholder("Loading columns...");
        columnInput.disabled = true;
        columnStatus.textContent = `Loading columns from ${table}…`;

        try
        {
            const columnUrl = form.dataset.fieldDataColumnUrl || "/FieldData/ColumnOptions";
            const separator = columnUrl.includes("?") ? "&" : "?";
            const response = await fetch(`${columnUrl}${separator}table=${encodeURIComponent(table)}`, {
                headers: { "Accept": "application/json" },
                credentials: "same-origin"
            });
            if (!response.ok)
            {
                throw new Error("Column lookup failed.");
            }

            const columns = await response.json();
            setColumnPlaceholder(columns.length ? "Select a column..." : "No columns available");

            let previousFound = false;
            columns.forEach(column => {
                const option = document.createElement("option");
                option.value = column.name;
                option.textContent = column.type ? `${column.name} (${column.type})` : column.name;
                if (column.comment)
                {
                    option.title = column.comment;
                }

                if (previousColumn && column.name.toLowerCase() === previousColumn.toLowerCase())
                {
                    previousFound = true;
                }

                columnInput.appendChild(option);
            });

            if (previousColumn)
            {
                if (!previousFound)
                {
                    const saved = document.createElement("option");
                    saved.value = previousColumn;
                    saved.textContent = `${previousColumn} (saved target)`;
                    columnInput.appendChild(saved);
                }

                columnInput.value = previousColumn;
            }

            columnInput.disabled = columns.length === 0 && !previousColumn;
            columnStatus.textContent = columns.length
                ? `${columns.length.toLocaleString()} columns available from ${table}.`
                : "No columns were returned for this table.";
        }
        catch
        {
            setColumnPlaceholder("Column lookup unavailable");
            if (previousColumn)
            {
                const saved = document.createElement("option");
                saved.value = previousColumn;
                saved.textContent = `${previousColumn} (saved target)`;
                columnInput.appendChild(saved);
                columnInput.value = previousColumn;
                columnInput.disabled = false;
            }

            columnStatus.textContent = "Column lookup is unavailable for the active database connection.";
        }
    };

    selection?.addEventListener("change", refreshHandling);
    handling?.addEventListener("change", refreshHandling);

    if (tableInput instanceof HTMLSelectElement && columnInput instanceof HTMLSelectElement)
    {
        tableInput.addEventListener("change", () => loadColumns(false));
        if (tableInput.value.trim())
        {
            loadColumns(true);
        }
        else
        {
            columnInput.disabled = true;
        }
    }

    refreshHandling();
})();
