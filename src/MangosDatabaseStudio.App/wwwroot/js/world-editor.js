// world-editor user-interface behavior; keep DOM updates scoped to the active view.
(function (root, factory) {
    "use strict";

    const api = factory();

    if (typeof module === "object" && module.exports)
    {
        module.exports = api;
    }

    if (root?.document)
    {
        root.MangosWorldEditor = api;
        const start = () => api.initialize(root.document);
        if (root.document.readyState === "loading")
        {
            root.document.addEventListener("DOMContentLoaded", start, { once: true });
        }
        else
        {
            start();
        }
    }
})(typeof window !== "undefined" ? window : null, function () {
    "use strict";

    const numericPattern = /^[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?$/;

    function initialize(documentRoot) {
        if (!documentRoot?.querySelectorAll)
        {
            return 0;
        }

        let initialized = 0;
        documentRoot.querySelectorAll("form[data-sql-preview]").forEach(form => {
            if (initializeForm(form))
            {
                initialized++;
            }
        });
        return initialized;
    }

    function initializeForm(form) {
        if (!form || form.dataset.sqlPreviewInitialized === "true")
        {
            return false;
        }

        const output = form.querySelector("#sql-preview");
        const fields = Array.from(form.querySelectorAll(".sql-field[data-column]"));
        const nullToggles = Array.from(form.querySelectorAll("[data-null-for]"));
        const modeButtons = Array.from(form.querySelectorAll("[data-sql-mode]"));
        const bitMaskEditors = Array.from(form.querySelectorAll("[data-bitmask-editor]"));
        const multiValueEditors = Array.from(form.querySelectorAll("[data-multivalue-editor]"));

        if (!output || fields.length === 0)
        {
            showInitializationError(form, output, "The SQL preview could not find the editor fields.");
            return false;
        }

        const state = {
            form,
            output,
            fields,
            nullToggles,
            modeButtons,
            bitMaskEditors,
            multiValueEditors,
            changeSqlInput: form.querySelector("[data-change-sql]"),
            database: form.dataset.database || "",
            table: form.dataset.table || "",
            isCreate: readBooleanData(form.dataset.create),
            sqlMode: "diff"
        };

        form.dataset.sqlPreviewInitialized = "true";

        initializeBitMaskEditors(bitMaskEditors);
        initializeMultiValueEditors(multiValueEditors);

        modeButtons.forEach(button => {
            button.addEventListener("click", () => {
                state.sqlMode = button.dataset.sqlMode === "full" ? "full" : "diff";
                modeButtons.forEach(item => item.classList.toggle("active", item === button));
                render(state);
            });
        });

        form.addEventListener("input", event => {
            const target = event.target;
            if (!target?.matches)
            {
                return;
            }

            if (target.matches("[data-bitmask-search]"))
            {
                const editor = target.closest("[data-bitmask-editor]");
                if (editor)
                {
                    filterBitMaskOptions(editor, target.value);
                }

                return;
            }

            if (target.matches("[data-bitmask-raw]"))
            {
                const editor = target.closest("[data-bitmask-editor]");
                if (editor)
                {
                    updateBitMaskDraftFromRaw(editor);
                }

                return;
            }

            if (target.matches("[data-multivalue-search]"))
            {
                const editor = target.closest("[data-multivalue-editor]");
                if (editor)
                {
                    filterMultiValueOptions(editor, target.value);
                }

                return;
            }

            if (target.matches("[data-multivalue-raw]"))
            {
                const editor = target.closest("[data-multivalue-editor]");
                if (editor)
                {
                    updateMultiValueDraftFromRaw(editor);
                }

                return;
            }

            if (target.matches(".sql-field"))
            {
                render(state);
            }
        });

        form.addEventListener("change", event => {
            const target = event.target;
            if (!target?.matches)
            {
                return;
            }

            if (target.matches("[data-bitmask-option]"))
            {
                const editor = target.closest("[data-bitmask-editor]");
                if (editor)
                {
                    updateBitMaskDraftFromSelections(editor);
                }

                return;
            }

            if (target.matches("[data-multivalue-option]"))
            {
                const editor = target.closest("[data-multivalue-editor]");
                if (editor)
                {
                    updateMultiValueDraftFromSelections(editor);
                }

                return;
            }

            if (!target.matches(".sql-field, [data-null-for]"))
            {
                return;
            }

            if (target.matches("[data-null-for]"))
            {
                applyNullState(state, target);
            }

            render(state);
        });

        let lastSubmitter = null;

        form.addEventListener("click", event => {
            const target = event.target?.closest?.(
                "[data-bitmask-open], [data-bitmask-cancel], [data-bitmask-apply], [data-bitmask-clear-known], [data-multivalue-open], [data-multivalue-cancel], [data-multivalue-apply], [data-multivalue-clear], button[type='submit'], input[type='submit']");

            if (!target)
            {
                return;
            }

            const editor = target.closest?.("[data-bitmask-editor]");
            if (editor && target.matches("[data-bitmask-open]"))
            {
                openBitMaskEditor(editor);
                return;
            }

            if (editor && target.matches("[data-bitmask-cancel]"))
            {
                closeBitMaskEditor(editor, true);
                return;
            }

            if (editor && target.matches("[data-bitmask-clear-known]"))
            {
                clearKnownBitMaskFlags(editor);
                return;
            }

            if (editor && target.matches("[data-bitmask-apply]"))
            {
                if (applyBitMaskEditor(editor))
                {
                    render(state);
                }

                return;
            }

            const multiEditor = target.closest?.("[data-multivalue-editor]");
            if (multiEditor && target.matches("[data-multivalue-open]"))
            {
                openMultiValueEditor(multiEditor);
                return;
            }

            if (multiEditor && target.matches("[data-multivalue-cancel]"))
            {
                closeMultiValueEditor(multiEditor, true);
                return;
            }

            if (multiEditor && target.matches("[data-multivalue-clear]"))
            {
                clearMultiValueSelections(multiEditor);
                return;
            }

            if (multiEditor && target.matches("[data-multivalue-apply]"))
            {
                if (applyMultiValueEditor(multiEditor))
                {
                    render(state);
                }

                return;
            }

            if (target.matches("button[type='submit'], input[type='submit']"))
            {
                lastSubmitter = target;
            }
        });

        form.addEventListener("keydown", event => {
            const target = event.target;
            const editor = target?.closest?.("[data-bitmask-editor]");
            const multiEditor = target?.closest?.("[data-multivalue-editor]");

            if (event.key === "Enter" && editor && target?.matches?.("[data-bitmask-search], [data-bitmask-raw]"))
            {
                event.preventDefault();
                if (target.matches("[data-bitmask-raw]") && applyBitMaskEditor(editor))
                {
                    render(state);
                }

                return;
            }

            if (event.key === "Enter" && multiEditor && target?.matches?.("[data-multivalue-search], [data-multivalue-raw]"))
            {
                event.preventDefault();
                if (target.matches("[data-multivalue-raw]") && applyMultiValueEditor(multiEditor))
                {
                    render(state);
                }

                return;
            }

            if (event.key !== "Escape")
            {
                return;
            }

            const bitMaskModal = form.querySelector("[data-bitmask-modal]:not([hidden])");
            const openBitMaskEditor = bitMaskModal?.closest?.("[data-bitmask-editor]");
            if (openBitMaskEditor)
            {
                event.preventDefault();
                closeBitMaskEditor(openBitMaskEditor, true);
                return;
            }

            const multiModal = form.querySelector("[data-multivalue-modal]:not([hidden])");
            const openMultiEditor = multiModal?.closest?.("[data-multivalue-editor]");
            if (!openMultiEditor)
            {
                return;
            }

            event.preventDefault();
            closeMultiValueEditor(openMultiEditor, true);
        });

        form.addEventListener("submit", event => {
            // SubmitEvent.submitter is not available in some older WebKitGTK
            // application builds can lose the submitter during navigation, so retain the last clicked button.
            const submitter = event.submitter || lastSubmitter;
            lastSubmitter = null;

            // Back and Cancel deliberately leave the record editor without committing the current row.
            if (submitter?.hasAttribute("formnovalidate") ||
                submitter?.hasAttribute("data-editor-navigation"))
            {
                return;
            }

            if (form.dataset.sqlPreviewValid !== "false")
            {
                return;
            }

            event.preventDefault();

            const firstInvalid =
                form.querySelector(
                    "[data-field-card].has-validation-error .sql-field");

            firstInvalid?.focus();

            setQueryState(
                form,
                "Correct the highlighted field values before saving.");
        });

        nullToggles.forEach(toggle => applyNullState(state, toggle));
        render(state);
        return true;
    }

    function applyNullState(state, toggle) {
        const column = toggle.dataset.nullFor || "";
        const field = state.fields.find(item => item.dataset.column === column);
        if (!field)
        {
            return;
        }

        const writable = readBooleanData(field.dataset.writable);
        const useNull = Boolean(toggle.checked);
        field.readOnly = !writable || useNull;
        field.setAttribute("aria-disabled", String(useNull));
        field.closest("[data-field-card]")?.classList.toggle("is-null", useNull);

        const bitMaskEditor = field.closest("[data-bitmask-editor]");
        bitMaskEditor?.querySelectorAll("[data-bitmask-option]").forEach(option => {
            option.disabled = !writable || useNull;
        });
        const bitMaskRaw = bitMaskEditor?.querySelector("[data-bitmask-raw]");
        if (bitMaskRaw)
        {
            bitMaskRaw.disabled = !writable || useNull;
        }

        const bitMaskOpen = bitMaskEditor?.querySelector("[data-bitmask-open]");
        if (bitMaskOpen)
        {
            bitMaskOpen.disabled = !writable || useNull;
        }

        bitMaskEditor?.classList.toggle("is-disabled", useNull);

        if (useNull && bitMaskEditor?.querySelector("[data-bitmask-modal]:not([hidden])"))
        {
            closeBitMaskEditor(bitMaskEditor, true);
        }

        const multiValueEditor = field.closest("[data-multivalue-editor]");
        multiValueEditor?.querySelectorAll("[data-multivalue-option]").forEach(option => {
            option.disabled = !writable || useNull;
        });
        const multiValueRaw = multiValueEditor?.querySelector("[data-multivalue-raw]");
        if (multiValueRaw)
        {
            multiValueRaw.disabled = !writable || useNull;
        }

        const multiValueOpen = multiValueEditor?.querySelector("[data-multivalue-open]");
        if (multiValueOpen)
        {
            multiValueOpen.disabled = !writable || useNull;
        }

        multiValueEditor?.classList.toggle("is-disabled", useNull);
        if (useNull && multiValueEditor?.querySelector("[data-multivalue-modal]:not([hidden])"))
        {
            closeMultiValueEditor(multiValueEditor, true);
        }
    }

    function initializeBitMaskEditors(editors) {
        editors.forEach(editor => {
            updateBitMaskCommittedSummary(editor);
            resetBitMaskDraft(editor);
            filterBitMaskOptions(editor, "");
        });
    }

    function getBitMaskField(editor) {
        return editor?.querySelector?.(".sql-field[data-bitmask-value]") || null;
    }

    function getBitMaskCatalogMask(editor) {
        let knownMask = 0n;
        editor?.querySelectorAll?.("[data-bitmask-option]:not([data-bitmask-unmapped='true'])").forEach(option => {
            const bit = parseUnsignedBigInt(option.value);
            if (bit !== null && bit > 0n)
            {
                knownMask |= bit;
            }
        });
        return knownMask;
    }

    function getBitMaskKnownMask(editor) {
        let knownMask = 0n;
        editor?.querySelectorAll?.("[data-bitmask-option]").forEach(option => {
            const bit = parseUnsignedBigInt(option.value);
            if (bit !== null && bit > 0n)
            {
                knownMask |= bit;
            }
        });
        return knownMask;
    }


    function syncUnmappedBitMaskOptions(editor, value) {
        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value);
        if (parsed === null)
        {
            return 0n;
        }

        editor?.querySelectorAll?.("[data-bitmask-option-row][data-bitmask-unmapped='true']").forEach(row => row.remove());

        const optionsHost = editor?.querySelector?.("[data-bitmask-options]");
        if (!optionsHost)
        {
            return 0n;
        }

        const unmappedBits = parsed & ~getBitMaskCatalogMask(editor);
        const preserveUnknown = readBooleanData(editor?.dataset?.bitmaskPreserveUnknown, true);
        if (!preserveUnknown)
        {
            return unmappedBits;
        }

        let remaining = unmappedBits;
        let position = 0;

        while (remaining > 0n && position < 64)
        {
            const bit = 1n << BigInt(position);
            if ((remaining & bit) === bit)
            {
                const row = editor.ownerDocument.createElement("label");
                row.className = "record-bitmask-option record-bitmask-option-unmapped is-selected";
                row.dataset.bitmaskOptionRow = "";
                row.dataset.bitmaskUnmapped = "true";
                row.dataset.bitmaskSearchText = `unmapped bit ${position} ${bit.toString(10)} ${formatBitMaskHex(bit)}`;
                row.title = "This bit is set in the stored database value but is not mapped by Studio's current field catalog.";

                const input = editor.ownerDocument.createElement("input");
                input.type = "checkbox";
                input.value = bit.toString(10);
                input.checked = true;
                input.dataset.bitmaskOption = "";
                input.dataset.bitmaskUnmapped = "true";

                const copy = editor.ownerDocument.createElement("span");
                const title = editor.ownerDocument.createElement("strong");
                title.textContent = `Unmapped bit ${position}`;
                const meta = editor.ownerDocument.createElement("small");
                meta.textContent = `${bit.toString(10)} · ${formatBitMaskHex(bit)}`;
                const description = editor.ownerDocument.createElement("em");
                description.textContent = "Present in the stored field value; no Studio label is defined for this bit.";

                copy.append(title, meta, description);
                row.append(input, copy);
                optionsHost.append(row);
                remaining &= ~bit;
            }

            position++;
        }

        return unmappedBits;
    }

    function syncBitMaskOptionsFromValue(editor, value) {
        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value);
        if (parsed === null)
        {
            return 0;
        }

        let selectedCount = 0;
        editor.querySelectorAll("[data-bitmask-option]").forEach(option => {
            const bit = parseUnsignedBigInt(option.value);
            const selected = bit !== null && bit > 0n && (parsed & bit) === bit;
            option.checked = selected;
            option.closest("[data-bitmask-option-row]")?.classList.toggle("is-selected", selected);
            if (selected)
            {
                selectedCount++;
            }
        });
        return selectedCount;
    }

    function getSelectedBitMaskLabels(editor, value) {
        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value);
        if (parsed === null)
        {
            return [];
        }

        const labels = [];
        editor.querySelectorAll("[data-bitmask-option]").forEach(option => {
            const bit = parseUnsignedBigInt(option.value);
            if (bit === null || bit <= 0n || (parsed & bit) !== bit)
            {
                return;
            }

            const label = option.closest("[data-bitmask-option-row]")?.querySelector("strong")?.textContent?.trim();
            if (label)
            {
                labels.push(label);
            }
        });
        return labels;
    }

    function formatBitMaskSelectionSummary(labels, unknownBits = 0n) {
        const hasUnknownBits = typeof unknownBits === "bigint" && unknownBits > 0n;
        if (!labels?.length)
        {
            return hasUnknownBits ? "Custom bits" : "None";
        }

        const visible = labels.slice(0, 2).join(", ");
        let summary = labels.length > 2 ? `${visible} + ${labels.length - 2} more` : visible;
        if (hasUnknownBits)
        {
            summary += " + custom";
        }

        return summary;
    }

    function setBitMaskValidation(editor, message = "") {
        const validation = editor?.querySelector?.("[data-bitmask-validation]");
        const raw = editor?.querySelector?.("[data-bitmask-raw]");
        const hasError = Boolean(message);
        if (validation)
        {
            validation.textContent = message || "";
            validation.classList.toggle("d-none", !hasError);
        }

        raw?.classList.toggle("is-invalid", hasError);
        return !hasError;
    }

    function updateBitMaskDraftSummary(editor, value) {
        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value);
        if (parsed === null)
        {
            return false;
        }

        const unknownBits = syncUnmappedBitMaskOptions(editor, parsed);
        const selectedCount = syncBitMaskOptionsFromValue(editor, parsed);
        const decimalTarget = editor.querySelector("[data-bitmask-draft-decimal]");
        const hexTarget = editor.querySelector("[data-bitmask-draft-hex]");
        const countTarget = editor.querySelector("[data-bitmask-draft-count]");
        const unknownNotice = editor.querySelector("[data-bitmask-unknown-notice]");
        const unknownValue = editor.querySelector("[data-bitmask-unknown-value]");

        if (decimalTarget)
        {
            decimalTarget.textContent = parsed.toString(10);
        }

        if (hexTarget)
        {
            hexTarget.textContent = formatBitMaskHex(parsed);
        }

        if (countTarget)
        {
            countTarget.textContent = String(selectedCount);
        }

        if (unknownValue)
        {
            unknownValue.textContent = formatBitMaskHex(unknownBits);
        }

        unknownNotice?.classList.toggle("d-none", unknownBits === 0n);
        return true;
    }

    function updateBitMaskCommittedSummary(editor, value = null) {
        const field = getBitMaskField(editor);
        if (!field)
        {
            return false;
        }

        const parsed = typeof value === "bigint"
            ? value
            : parseUnsignedBigInt(value ?? field.value);
        if (parsed === null)
        {
            return false;
        }

        const unknownBits = syncUnmappedBitMaskOptions(editor, parsed);
        const labels = getSelectedBitMaskLabels(editor, parsed);
        const decimalTarget = editor.querySelector("[data-bitmask-decimal]");
        const hexTarget = editor.querySelector("[data-bitmask-hex]");
        const countTarget = editor.querySelector("[data-bitmask-count]");
        const summaryTarget = editor.querySelector("[data-bitmask-summary-text]");

        if (decimalTarget)
        {
            decimalTarget.textContent = parsed.toString(10);
        }

        if (hexTarget)
        {
            hexTarget.textContent = formatBitMaskHex(parsed);
        }

        if (countTarget)
        {
            countTarget.textContent = String(labels.length);
        }

        if (summaryTarget)
        {
            summaryTarget.textContent = formatBitMaskSelectionSummary(labels, 0n);
        }

        editor.dataset.bitmaskUnknown = unknownBits.toString(10);
        return true;
    }

    function resetBitMaskDraft(editor) {
        const field = getBitMaskField(editor);
        if (!field)
        {
            return false;
        }

        const value = parseUnsignedBigInt(field.value) ?? 0n;
        const raw = editor.querySelector("[data-bitmask-raw]");
        const search = editor.querySelector("[data-bitmask-search]");
        if (raw)
        {
            raw.value = value.toString(10);
        }

        if (search)
        {
            search.value = "";
        }

        setBitMaskValidation(editor);
        updateBitMaskDraftSummary(editor, value);
        filterBitMaskOptions(editor, "");
        return true;
    }

    function openBitMaskEditor(editor) {
        const modal = editor?.querySelector?.("[data-bitmask-modal]");
        const trigger = editor?.querySelector?.("[data-bitmask-open]");
        if (!modal || trigger?.disabled)
        {
            return false;
        }

        const documentRoot = editor.ownerDocument;
        documentRoot?.querySelectorAll?.("[data-bitmask-modal]:not([hidden])").forEach(openModal => {
            const openEditor = openModal.closest("[data-bitmask-editor]");
            if (openEditor && openEditor !== editor)
            {
                closeBitMaskEditor(openEditor, true, false);
            }
        });
        documentRoot?.querySelectorAll?.("[data-multivalue-modal]:not([hidden])").forEach(openModal => {
            const openEditor = openModal.closest("[data-multivalue-editor]");
            if (openEditor)
            {
                closeMultiValueEditor(openEditor, true, false);
            }
        });

        resetBitMaskDraft(editor);
        modal.hidden = false;
        documentRoot?.body?.classList.add("bitmask-modal-open");

        const search = editor.querySelector("[data-bitmask-search]");
        const focusTarget = search || editor.querySelector("[data-bitmask-raw]") || editor.querySelector("[data-bitmask-apply]");
        if (typeof focusTarget?.focus === "function")
        {
            globalThis.setTimeout?.(() => focusTarget.focus(), 0);
        }

        return true;
    }

    function closeBitMaskEditor(editor, resetDraft = true, restoreFocus = true) {
        const modal = editor?.querySelector?.("[data-bitmask-modal]");
        if (!modal || modal.hidden)
        {
            return false;
        }

        modal.hidden = true;
        if (resetDraft)
        {
            resetBitMaskDraft(editor);
        }

        const documentRoot = editor.ownerDocument;
        if (!documentRoot?.querySelector?.("[data-bitmask-modal]:not([hidden]), [data-multivalue-modal]:not([hidden])"))
        {
            documentRoot?.body?.classList.remove("bitmask-modal-open");
        }

        if (restoreFocus)
        {
            editor.querySelector("[data-bitmask-open]")?.focus?.();
        }

        return true;
    }

    function updateBitMaskDraftFromRaw(editor) {
        const raw = editor?.querySelector?.("[data-bitmask-raw]");
        if (!raw)
        {
            return false;
        }

        const value = parseUnsignedBigInt(raw.value);
        if (value === null || value > 18446744073709551615n)
        {
            setBitMaskValidation(editor, "Enter an unsigned decimal value from 0 through 18446744073709551615.");
            return false;
        }

        setBitMaskValidation(editor);
        return updateBitMaskDraftSummary(editor, value);
    }

    function updateBitMaskDraftFromSelections(editor) {
        const raw = editor?.querySelector?.("[data-bitmask-raw]");
        const field = getBitMaskField(editor);
        if (!raw || !field)
        {
            return false;
        }

        const selectedValues = Array.from(editor.querySelectorAll("[data-bitmask-option]:checked"))
            .map(option => option.value);
        const value = parseUnsignedBigInt(combineBitMaskValue("0", selectedValues)) ?? 0n;

        raw.value = value.toString(10);
        setBitMaskValidation(editor);
        return updateBitMaskDraftSummary(editor, value);
    }

    function clearKnownBitMaskFlags(editor) {
        const raw = editor?.querySelector?.("[data-bitmask-raw]");
        const field = getBitMaskField(editor);
        if (!raw || !field)
        {
            return false;
        }

        editor.querySelectorAll("[data-bitmask-option]").forEach(option => {
            option.checked = false;
            option.closest("[data-bitmask-option-row]")?.classList.remove("is-selected");
        });
        raw.value = "0";
        setBitMaskValidation(editor);
        return updateBitMaskDraftSummary(editor, 0n);
    }

    function applyBitMaskEditor(editor) {
        const field = getBitMaskField(editor);
        const raw = editor?.querySelector?.("[data-bitmask-raw]");
        if (!field || !raw)
        {
            return false;
        }

        const value = parseUnsignedBigInt(raw.value);
        if (value === null || value > 18446744073709551615n)
        {
            setBitMaskValidation(editor, "Enter an unsigned decimal value from 0 through 18446744073709551615.");
            raw.focus?.();
            return false;
        }

        field.value = value.toString(10);
        updateBitMaskCommittedSummary(editor, value);
        setBitMaskValidation(editor);
        closeBitMaskEditor(editor, false, true);
        return true;
    }

    function filterBitMaskOptions(editor, query) {
        const normalized = String(query ?? "").trim().toLowerCase();
        let visibleCount = 0;

        editor?.querySelectorAll?.("[data-bitmask-option-row]").forEach(row => {
            const searchText = (row.dataset.bitmaskSearchText || row.textContent || "").toLowerCase();
            const visible = !normalized || searchText.includes(normalized);
            row.hidden = !visible;
            if (visible)
            {
                visibleCount++;
            }
        });

        const empty = editor?.querySelector?.("[data-bitmask-empty]");
        if (empty)
        {
            empty.hidden = visibleCount > 0;
        }

        return visibleCount;
    }

    function parseUnsignedBigInt(value) {
        const text = String(value ?? "").trim();
        if (!/^\d+$/.test(text))
        {
            return null;
        }

        try
        {
            const parsed = BigInt(text);
            return parsed >= 0n ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    function formatBitMaskHex(value) {
        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value);
        if (parsed === null || parsed < 0n)
        {
            return "0x0";
        }

        return `0x${parsed.toString(16).toUpperCase()}`;
    }

    function combineBitMaskValue(unknownValue, selectedValues) {
        const unknownBits = parseUnsignedBigInt(unknownValue) ?? 0n;
        let value = unknownBits;

        (selectedValues || []).forEach(item => {
            const bit = parseUnsignedBigInt(item);
            if (bit !== null && bit > 0n)
            {
                value |= bit;
            }
        });

        return value.toString(10);
    }

    function initializeMultiValueEditors(editors) {
        editors.forEach(editor => {
            updateMultiValueCommittedSummary(editor);
            resetMultiValueDraft(editor);
            filterMultiValueOptions(editor, "");
        });
    }

    function getMultiValueField(editor) {
        return editor?.querySelector?.(".sql-field[data-multivalue-value]") || null;
    }

    function setMultiValueValidation(editor, message = "") {
        const validation = editor?.querySelector?.("[data-multivalue-validation]");
        const raw = editor?.querySelector?.("[data-multivalue-raw]");
        const hasError = Boolean(message);
        if (validation)
        {
            validation.textContent = message || "";
            validation.classList.toggle("d-none", !hasError);
        }

        raw?.classList.toggle("is-invalid", hasError);
        return !hasError;
    }

    function getSelectedMultiValueSum(editor) {
        let total = 0n;
        let count = 0;
        editor?.querySelectorAll?.("[data-multivalue-option]:checked").forEach(option => {
            const value = parseUnsignedBigInt(option.value);
            if (value !== null)
            {
                total += value;
                count++;
            }
        });
        return { total, count };
    }

    function updateMultiValueDraftSummary(editor, value = null) {
        const raw = editor?.querySelector?.("[data-multivalue-raw]");
        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value ?? raw?.value);
        if (parsed === null)
        {
            return false;
        }

        const selectedCount = editor?.querySelectorAll?.("[data-multivalue-option]:checked").length || 0;
        const valueTarget = editor?.querySelector?.("[data-multivalue-draft-value]");
        const countTarget = editor?.querySelector?.("[data-multivalue-draft-count]");
        if (valueTarget)
        {
            valueTarget.textContent = parsed.toString(10);
        }

        if (countTarget)
        {
            countTarget.textContent = String(selectedCount);
        }

        return true;
    }

    function updateMultiValueCommittedSummary(editor, value = null) {
        const field = getMultiValueField(editor);
        if (!field)
        {
            return false;
        }

        const parsed = typeof value === "bigint" ? value : parseUnsignedBigInt(value ?? field.value);
        if (parsed === null)
        {
            return false;
        }

        const valueTarget = editor.querySelector("[data-multivalue-decimal]");
        const countTarget = editor.querySelector("[data-multivalue-count]");
        if (valueTarget)
        {
            valueTarget.textContent = parsed.toString(10);
        }

        if (countTarget)
        {
            countTarget.textContent = String(editor.querySelectorAll("[data-multivalue-option]:checked").length);
        }

        return true;
    }

    function resetMultiValueDraft(editor) {
        const field = getMultiValueField(editor);
        if (!field)
        {
            return false;
        }

        const value = parseUnsignedBigInt(field.value) ?? 0n;
        const raw = editor.querySelector("[data-multivalue-raw]");
        const search = editor.querySelector("[data-multivalue-search]");
        editor.querySelectorAll("[data-multivalue-option]").forEach(option => {
            option.checked = false;
            option.closest("[data-multivalue-option-row]")?.classList.remove("is-selected");
        });

        // Additive values do not always have a unique decomposition. Only infer a
        // checkbox when the stored value exactly matches one catalog entry; otherwise
        // keep the raw value authoritative until the user selects a new combination.
        const exact = Array.from(editor.querySelectorAll("[data-multivalue-option]")).filter(option => {
            const optionValue = parseUnsignedBigInt(option.value);
            return optionValue !== null && optionValue === value;
        });
        if (exact.length === 1)
        {
            exact[0].checked = true;
            exact[0].closest("[data-multivalue-option-row]")?.classList.add("is-selected");
        }

        if (raw)
        {
            raw.value = value.toString(10);
        }

        if (search)
        {
            search.value = "";
        }

        setMultiValueValidation(editor);
        updateMultiValueDraftSummary(editor, value);
        filterMultiValueOptions(editor, "");
        return true;
    }

    function openMultiValueEditor(editor) {
        const modal = editor?.querySelector?.("[data-multivalue-modal]");
        const trigger = editor?.querySelector?.("[data-multivalue-open]");
        if (!modal || trigger?.disabled)
        {
            return false;
        }

        const documentRoot = editor.ownerDocument;
        documentRoot?.querySelectorAll?.("[data-multivalue-modal]:not([hidden])").forEach(openModal => {
            const openEditor = openModal.closest("[data-multivalue-editor]");
            if (openEditor && openEditor !== editor)
            {
                closeMultiValueEditor(openEditor, true, false);
            }
        });
        documentRoot?.querySelectorAll?.("[data-bitmask-modal]:not([hidden])").forEach(openModal => {
            const openEditor = openModal.closest("[data-bitmask-editor]");
            if (openEditor)
            {
                closeBitMaskEditor(openEditor, true, false);
            }
        });
        resetMultiValueDraft(editor);
        modal.hidden = false;
        documentRoot?.body?.classList.add("bitmask-modal-open");
        const focusTarget = editor.querySelector("[data-multivalue-search]") || editor.querySelector("[data-multivalue-raw]");
        globalThis.setTimeout?.(() => focusTarget?.focus?.(), 0);
        return true;
    }

    function closeMultiValueEditor(editor, resetDraft = true, restoreFocus = true) {
        const modal = editor?.querySelector?.("[data-multivalue-modal]");
        if (!modal || modal.hidden)
        {
            return false;
        }

        modal.hidden = true;
        if (resetDraft)
        {
            resetMultiValueDraft(editor);
        }

        const documentRoot = editor.ownerDocument;
        if (!documentRoot?.querySelector?.("[data-bitmask-modal]:not([hidden]), [data-multivalue-modal]:not([hidden])"))
        {
            documentRoot?.body?.classList.remove("bitmask-modal-open");
        }

        if (restoreFocus)
        {
            editor.querySelector("[data-multivalue-open]")?.focus?.();
        }

        return true;
    }

    function updateMultiValueDraftFromRaw(editor) {
        const raw = editor?.querySelector?.("[data-multivalue-raw]");
        if (!raw)
        {
            return false;
        }

        const value = parseUnsignedBigInt(raw.value);
        if (value === null || value > 18446744073709551615n)
        {
            setMultiValueValidation(editor, "Enter an unsigned decimal value from 0 through 18446744073709551615.");
            return false;
        }

        setMultiValueValidation(editor);
        return updateMultiValueDraftSummary(editor, value);
    }

    function updateMultiValueDraftFromSelections(editor) {
        const raw = editor?.querySelector?.("[data-multivalue-raw]");
        if (!raw)
        {
            return false;
        }

        editor.querySelectorAll("[data-multivalue-option]").forEach(option => {
            option.closest("[data-multivalue-option-row]")?.classList.toggle("is-selected", option.checked);
        });
        const selected = getSelectedMultiValueSum(editor);
        if (selected.total > 18446744073709551615n)
        {
            setMultiValueValidation(editor, "The selected values exceed the unsigned 64-bit range.");
            return false;
        }

        raw.value = selected.total.toString(10);
        setMultiValueValidation(editor);
        return updateMultiValueDraftSummary(editor, selected.total);
    }

    function clearMultiValueSelections(editor) {
        const raw = editor?.querySelector?.("[data-multivalue-raw]");
        if (!raw)
        {
            return false;
        }

        editor.querySelectorAll("[data-multivalue-option]").forEach(option => {
            option.checked = false;
            option.closest("[data-multivalue-option-row]")?.classList.remove("is-selected");
        });
        raw.value = "0";
        setMultiValueValidation(editor);
        return updateMultiValueDraftSummary(editor, 0n);
    }

    function applyMultiValueEditor(editor) {
        const field = getMultiValueField(editor);
        const raw = editor?.querySelector?.("[data-multivalue-raw]");
        if (!field || !raw)
        {
            return false;
        }

        const value = parseUnsignedBigInt(raw.value);
        if (value === null || value > 18446744073709551615n)
        {
            setMultiValueValidation(editor, "Enter an unsigned decimal value from 0 through 18446744073709551615.");
            raw.focus?.();
            return false;
        }

        field.value = value.toString(10);
        updateMultiValueCommittedSummary(editor, value);
        setMultiValueValidation(editor);
        closeMultiValueEditor(editor, false, true);
        return true;
    }

    function filterMultiValueOptions(editor, query) {
        const normalized = String(query ?? "").trim().toLowerCase();
        let visibleCount = 0;
        editor?.querySelectorAll?.("[data-multivalue-option-row]").forEach(row => {
            const searchText = (row.dataset.multivalueSearchText || row.textContent || "").toLowerCase();
            const visible = !normalized || searchText.includes(normalized);
            row.hidden = !visible;
            if (visible)
            {
                visibleCount++;
            }
        });
        const empty = editor?.querySelector?.("[data-multivalue-empty]");
        if (empty)
        {
            empty.hidden = visibleCount > 0;
        }

        return visibleCount;
    }

    function render(state) {
        try
        {
            const values = collectValues(state);
            const changedValues = values.filter(item => item.changed && item.writable);
            const writableValues = values.filter(item => item.writable);
            const createValues = values.filter(item => item.writable);

            // WHERE must use the original primary-key values.
            // This matters when an editable part of a composite key changes.
            const keys = values
                .filter(item => item.primary)
                .map(item => ({
                    ...item,
                    value: item.original,
                    useNull: item.originalNull,
                    changed: false
                }));
            const formattedKeys = keys.map(formatItem);
            const selectedValues = state.isCreate
                ? createValues
                : state.sqlMode === "full" ? writableValues : changedValues;
            const storedValues = state.isCreate ? createValues : changedValues;

            const formatted = selectedValues.map(formatItem);
            const storedFormatted = storedValues.map(formatItem);
            const validationErrors = formatted.flatMap(item => item.errors);
            const storedErrors = storedFormatted.flatMap(item => item.errors);
            state.form.dataset.sqlPreviewValid = String(validationErrors.length === 0);
            const changeCount = state.isCreate
                ? createValues.filter(item => item.useNull || item.value !== "").length
                : changedValues.length;

            values.forEach(item => {
                item.element.closest("[data-field-card]")?.classList.toggle("is-changed", item.changed);
                item.element.closest("[data-field-card]")?.classList.toggle("has-validation-error", false);
                updatePreview(state.form, item);
            });

            formatted.forEach(item => {
                item.source.element.closest("[data-field-card]")?.classList.toggle("has-validation-error", item.errors.length > 0);
            });

            updateRecordTitle(state.form, values);
            updateStatus(state, changeCount, validationErrors);

            let directSql;
            let directState;
            if (state.isCreate)
            {
                directSql = buildInsert(state.database, state.table, formatted);
                directState = validationErrors.length > 0
                    ? `${validationErrors.length} field validation issue${validationErrors.length === 1 ? "" : "s"}.`
                    : `${createValues.length} writable fields included.`;
            }
            else if (state.sqlMode === "diff" && changedValues.length === 0)
            {
                directSql = "-- No field changes detected.\n-- Change a value or select Full query to preview every writable field.";
                directState = "No field changes detected.";
            }
            else
            {
                directSql = buildUpdate(state.database, state.table, formatted, formattedKeys);
                directState = validationErrors.length > 0
                    ? `${validationErrors.length} field validation issue${validationErrors.length === 1 ? "" : "s"}.`
                    : state.sqlMode === "full"
                        ? `${selectedValues.length} writable fields included in the full query.`
                        : `${selectedValues.length} changed field${selectedValues.length === 1 ? "" : "s"} included.`;
            }

            let storedSql = "";
            if (storedErrors.length === 0)
            {
                if (state.isCreate)
                {
                    storedSql = buildStoredInsert(state.table, storedFormatted);
                }
                else if (changedValues.length > 0)
                {
                    storedSql = buildStoredUpdate(state.table, storedFormatted, formattedKeys);
                }
            }

            if (state.changeSqlInput)
            {
                state.changeSqlInput.value = storedSql;
            }

            state.output.textContent = directSql;
            setQueryState(state.form, directState);
        }
        catch (error)
        {
            showInitializationError(state.form, state.output, error instanceof Error ? error.message : "Unknown preview error.");
        }
    }

    function collectValues(state) {
        return state.fields.map(field => {
            const column = field.dataset.column || "";
            const nullToggle = state.nullToggles.find(toggle => toggle.dataset.nullFor === column);
            const useNull = Boolean(nullToggle?.checked);
            const originalNull = readBooleanData(field.dataset.originalNull);
            const writable = readBooleanData(field.dataset.writable);
            const dataType = (field.dataset.dataType || "").toLowerCase();
            const value = normalizeFieldValue(field.value, dataType);
            const original = normalizeFieldValue(field.dataset.original || "", dataType);
            const changed = writable && (useNull !== originalNull || (!useNull && value !== original));

            return {
                name: column,
                label: field.dataset.label || column,
                value,
                original,
                originalNull,
                primary: readBooleanData(field.dataset.primary),
                numeric: readBooleanData(field.dataset.numeric),
                nullable: readBooleanData(field.dataset.nullable),
                binary: readBooleanData(field.dataset.binary),
                unsigned: (field.dataset.columnType || "").toLowerCase().includes("unsigned"),
                dataType,
                writable,
                useNull,
                changed,
                element: field
            };
        });
    }

    function formatItem(item) {
        const errors = [];
        let sql;

        if (item.useNull)
        {
            sql = "NULL";
        }
        else if (item.binary)
        {
            if (item.value !== "" && !isBase64(item.value))
            {
                errors.push(`${item.label} must contain valid Base64 data.`);
            }

            sql = item.value === "" ? "X''" : `FROM_BASE64(${quoteValue(item.value)})`;
        }
        else if (item.numeric)
        {
            const trimmed = item.value.trim();
            if (!numericPattern.test(trimmed))
            {
                errors.push(`${item.label} must contain a valid number.`);
                sql = `/* INVALID NUMBER */ ${quoteValue(item.value)}`;
            }
            else if (item.unsigned && trimmed.startsWith("-"))
            {
                errors.push(`${item.label} cannot be negative because it is unsigned.`);
                sql = `/* INVALID UNSIGNED VALUE */ ${trimmed}`;
            }
            else
            {
                sql = trimmed;
            }
        }
        else if (isTemporalType(item.dataType))
        {
            if (item.value === "")
            {
                errors.push(`${item.label} is empty. Select NULL to store a database NULL value.`);
                sql = "/* EMPTY TEMPORAL VALUE */ ''";
            }
            else if (!isValidTemporalValue(item.value, item.dataType))
            {
                errors.push(`${item.label} does not contain a valid MySQL ${item.dataType} value.`);
                sql = `/* INVALID TEMPORAL VALUE */ ${quoteValue(item.value)}`;
            }
            else
            {
                sql = quoteValue(toMySqlTemporalValue(item.value, item.dataType));
            }
        }
        else
        {
            sql = quoteValue(item.value);
        }

        return { source: item, sql, errors };
    }

    function buildInsert(database, table, values) {
        return buildInsertForTarget(qualifiedName(database, table), values);
    }

    function buildStoredInsert(table, values) {
        return buildInsertForTarget(quoteIdentifier(table), values);
    }

    function buildInsertForTarget(target, values) {
        if (values.length === 0)
        {
            return `INSERT INTO ${target} () VALUES ();`;
        }

        return `INSERT INTO ${target}\n` +
            `  (${values.map(item => quoteIdentifier(item.source.name)).join(", ")})\n` +
            `VALUES\n` +
            `  (${values.map(item => item.sql).join(", ")});`;
    }

    function buildUpdate(database, table, values, keys) {
        return buildUpdateForTarget(qualifiedName(database, table), values, keys);
    }

    function buildStoredUpdate(table, values, keys) {
        return buildUpdateForTarget(quoteIdentifier(table), values, keys);
    }

    function buildUpdateForTarget(target, values, keys) {
        if (values.length === 0)
        {
            return "-- This table has no writable non-identifier fields.";
        }

        if (keys.length === 0)
        {
            return "-- SQL preview blocked: this record has no primary-key identifier.";
        }

        const where = keys.map(item => item.sql === "NULL"
            ? `${quoteIdentifier(item.source.name)} IS NULL`
            : `${quoteIdentifier(item.source.name)} = ${item.sql}`)
            .join(" AND ");

        return `UPDATE ${target}\n` +
            `SET\n  ${values.map(item => `${quoteIdentifier(item.source.name)} = ${item.sql}`).join(",\n  ")}\n` +
            `WHERE ${where}\n` +
            "LIMIT 1;";
    }

    function qualifiedName(database, table) {
        return `${quoteIdentifier(database)}.${quoteIdentifier(table)}`;
    }

    function quoteIdentifier(value) {
        return "`" + String(value || "").replace(/`/g, "``") + "`";
    }

    function quoteValue(value) {
        return "'" + String(value ?? "")
            .replace(/\\/g, "\\\\")
            .replace(/'/g, "''") + "'";
    }

    function normalizeFieldValue(value, dataType) {
        const normalized = normalizeLineEndings(value);
        if (!isTemporalType(dataType))
        {
            return normalized;
        }

        const trimmed = normalized.trim();
        if (trimmed === "")
        {
            return "";
        }

        if (dataType === "date")
        {
            return normalizeDateValue(trimmed);
        }

        if (dataType === "datetime" || dataType === "timestamp")
        {
            return normalizeDateTimeValue(trimmed);
        }

        return normalizeTimeValue(trimmed);
    }

    function normalizeDateValue(value) {
        const iso = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
        if (iso)
        {
            return `${iso[1]}-${iso[2]}-${iso[3]}`;
        }

        const us = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(value);
        if (us)
        {
            return `${us[3]}-${pad2(us[1])}-${pad2(us[2])}`;
        }

        return value;
    }

    function normalizeDateTimeValue(value) {
        const iso = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{1,2}):(\d{2})(?::(\d{2}))?(?:\.(\d{1,6}))?$/.exec(value);
        if (iso)
        {
            return buildDateTime(iso[1], iso[2], iso[3], iso[4], iso[5], iso[6], iso[7]);
        }

        const us = /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:,|\s)+\s*(\d{1,2}):(\d{2})(?::(\d{2}))?\s*(AM|PM)?$/i.exec(value);
        if (us)
        {
            let hour = Number(us[4]);
            const meridiem = (us[7] || "").toUpperCase();
            if (meridiem === "AM" && hour === 12)
            {
                hour = 0;
            }

            if (meridiem === "PM" && hour < 12)
            {
                hour += 12;
            }

            return buildDateTime(us[3], us[1], us[2], String(hour), us[5], us[6], "");
        }

        return value;
    }

    function buildDateTime(year, month, day, hour, minute, second, fraction) {
        const base = `${year}-${pad2(month)}-${pad2(day)}T${pad2(hour)}:${pad2(minute)}:${pad2(second || "0")}`;
        const normalizedFraction = String(fraction || "").replace(/0+$/, "");
        return normalizedFraction ? `${base}.${normalizedFraction}` : base;
    }

    function normalizeTimeValue(value) {
        const match = /^(-?)(\d{1,3}):(\d{2})(?::(\d{2}))?(?:\.(\d{1,6}))?$/.exec(value);
        if (!match)
        {
            return value;
        }

        const fraction = String(match[5] || "").replace(/0+$/, "");
        const base = `${match[1]}${match[2].padStart(2, "0")}:${match[3]}:${pad2(match[4] || "0")}`;
        return fraction ? `${base}.${fraction}` : base;
    }

    function isTemporalType(dataType) {
        return dataType === "date" || dataType === "datetime" || dataType === "timestamp" || dataType === "time";
    }

    function isValidTemporalValue(value, dataType) {
        if (dataType === "date")
        {
            return /^(?:0000|\d{4})-(?:00|0[1-9]|1[0-2])-(?:00|0[1-9]|[12]\d|3[01])$/.test(value);
        }

        if (dataType === "datetime" || dataType === "timestamp")
        {
            return /^(?:0000|\d{4})-(?:00|0[1-9]|1[0-2])-(?:00|0[1-9]|[12]\d|3[01])T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,6})?$/.test(value);
        }

        return /^-?\d{1,3}:[0-5]\d:[0-5]\d(?:\.\d{1,6})?$/.test(value);
    }

    function toMySqlTemporalValue(value, dataType) {
        return dataType === "datetime" || dataType === "timestamp" ? value.replace("T", " ") : value;
    }

    function pad2(value) {
        return String(value).padStart(2, "0");
    }

    function isBase64(value) {
        const compact = String(value || "").replace(/\s/g, "");
        if (compact.length === 0 || compact.length % 4 !== 0)
        {
            return false;
        }

        return /^[A-Za-z0-9+/]*={0,2}$/.test(compact);
    }

    function normalizeLineEndings(value) {
        return String(value ?? "").replace(/\r\n?/g, "\n");
    }

    function readBooleanData(value, fallback = false) {
        const normalized = String(value ?? "").trim().toLowerCase();
        if (["true", "1", "yes", "on"].includes(normalized))
        {
            return true;
        }

        if (["false", "0", "no", "off", ""].includes(normalized))
        {
            return false;
        }

        // Compatibility with malformed values produced by older Razor markup such as
        // "True.ToString().ToLowerInvariant()". The view now emits exact lower-case
        // booleans, but accepting these values keeps the preview functional after a
        // partial deployment or while a stale compiled view is still loaded.
        if (normalized.startsWith("true."))
        {
            return true;
        }

        if (normalized.startsWith("false."))
        {
            return false;
        }

        return fallback;
    }

    function updatePreview(form, item) {
        form.querySelectorAll(`[data-preview-for="${escapeSelector(item.name)}"]`).forEach(target => {
            const text = item.useNull ? "NULL" : (item.value || "—");
            target.textContent = text;
            target.title = text;
        });
    }

    function updateRecordTitle(form, values) {
        const candidates = (form.dataset.titleCandidates || "name,title,description")
            .split(",")
            .map(item => item.trim().toLowerCase())
            .filter(Boolean);
        const titleField = values.find(item => candidates.includes(item.name.toLowerCase()) && !item.useNull && item.value.trim());
        if (!titleField)
        {
            return;
        }

        const title = titleField.value.length > 80 ? `${titleField.value.slice(0, 79)}…` : titleField.value;
        form.querySelectorAll("[data-record-title]").forEach(target => target.textContent = title);
    }

    function updateStatus(state, changeCount, validationErrors) {
        state.form.querySelectorAll("[data-change-count]").forEach(target => target.textContent = String(changeCount));

        const changeBadge = state.form.querySelector("[data-change-badge]");
        changeBadge?.classList.toggle("has-changes", changeCount > 0);
        changeBadge?.classList.toggle("has-errors", validationErrors.length > 0);

        const previewStatus = state.form.querySelector("[data-preview-status]");
        if (previewStatus)
        {
            previewStatus.textContent = validationErrors.length > 0
                ? "Needs attention"
                : state.isCreate ? "New entry" : changeCount > 0 ? "Modified" : "Unchanged";
        }

        const saveState = state.form.querySelector("[data-save-state]");
        if (saveState)
        {
            saveState.textContent = validationErrors.length > 0
                ? validationErrors[0]
                : state.isCreate
                    ? "Ready to create a new entry"
                    : changeCount > 0
                        ? `${changeCount} unsaved field change${changeCount === 1 ? "" : "s"}`
                        : "No unsaved changes";
        }

        state.output.classList.toggle("has-error", validationErrors.length > 0);
    }

    function setQueryState(form, text) {
        const queryState = form.querySelector("[data-query-state]");
        if (queryState)
        {
            queryState.textContent = text;
        }
    }

    function showInitializationError(form, output, message) {
        if (output)
        {
            output.textContent = `-- SQL preview unavailable: ${message}`;
            output.classList.add("has-error");
        }

        if (form?.dataset)
        {
            form.dataset.sqlPreviewValid = "false";
        }

        setQueryState(form, "SQL preview unavailable.");
    }

    function escapeSelector(value) {
        if (typeof CSS !== "undefined" && CSS.escape)
        {
            return CSS.escape(value);
        }

        return String(value).replace(/["\\]/g, "\\$&");
    }

    return {
        initialize,
        initializeForm,
        collectValues,
        formatItem,
        buildInsert,
        buildUpdate,
        buildStoredInsert,
        buildStoredUpdate,
        quoteIdentifier,
        quoteValue,
        normalizeLineEndings,
        normalizeFieldValue,
        isValidTemporalValue,
        toMySqlTemporalValue,
        isBase64,
        readBooleanData,
        parseUnsignedBigInt,
        formatBitMaskHex,
        combineBitMaskValue
    };
});
