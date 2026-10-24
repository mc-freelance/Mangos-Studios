// change-storage user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const reviewForm = document.querySelector("[data-change-review-form]");
    const reviewSelectAll = reviewForm?.querySelector("[data-change-select-all]");
    const reviewSelections = () => Array.from(reviewForm?.querySelectorAll("[data-change-select]") || []);

    reviewSelectAll?.addEventListener("change", () => {
        reviewSelections().forEach(input => input.checked = reviewSelectAll.checked);
    });

    reviewForm?.addEventListener("change", event => {
        if (!event.target?.matches?.("[data-change-select]"))
        {
            return;
        }

        const inputs = reviewSelections();
        if (reviewSelectAll)
        {
            reviewSelectAll.checked = inputs.length > 0 && inputs.every(input => input.checked);
        }
    });

    reviewForm?.querySelector("[data-change-generate-contribution]")?.addEventListener("click", async event => {
        const selected = reviewSelections().filter(input => input.checked);
        if (selected.length === 0)
        {
            event.preventDefault();
            await window.StudioDialog?.alert({
                title: "Select changes first",
                message: "Select at least one approved reusable change first.",
                variant: "warning",
                icon: "bi-check2-square"
            });
            return;
        }

        const unavailable = selected.filter(input => input.dataset.changeContribution !== "true");
        if (unavailable.length > 0)
        {
            event.preventDefault();
            await window.StudioDialog?.alert({
                title: "Contribution selection unavailable",
                message: "Contribution SQL can only be generated from approved records marked as reusable SQL.",
                detail: "Remove pending, ignored, or non-reusable records from the selection and try again.",
                variant: "warning",
                icon: "bi-file-earmark-code"
            });
            return;
        }

        const databases = new Set(selected.map(input => input.dataset.changeDatabase || "").filter(Boolean));
        if (databases.size !== 1)
        {
            event.preventDefault();
            await window.StudioDialog?.alert({
                title: "Use one database",
                message: "Select approved changes from one database at a time when generating contribution SQL.",
                variant: "warning",
                icon: "bi-database-exclamation"
            });
        }
    });

    reviewForm?.querySelector("[data-change-delete-selected]")?.addEventListener("click", async event => {
        const button = event.currentTarget;
        if (button?.dataset.studioDialogApproved === "true")
        {
            delete button.dataset.studioDialogApproved;
            return;
        }

        const selected = reviewSelections().filter(input => input.checked);
        if (selected.length === 0)
        {
            event.preventDefault();
            await window.StudioDialog?.alert({
                title: "Nothing selected",
                message: "Select at least one stored change first.",
                variant: "warning",
                icon: "bi-check2-square"
            });
            return;
        }

        const pending = selected.filter(input => input.dataset.changeStatus === "pending");
        const unavailable = pending.filter(input => input.dataset.changeRevert !== "true");
        if (unavailable.length > 0)
        {
            event.preventDefault();
            await window.StudioDialog?.alert({
                title: "Automatic rollback unavailable",
                message: "One or more selected pending changes do not have automatic rollback data.",
                detail: "Approve or ignore those records first if you only want to remove their history.",
                variant: "warning",
                icon: "bi-shield-exclamation"
            });
            return;
        }

        event.preventDefault();
        const message = pending.length > 0
            ? `${pending.length} pending database change${pending.length === 1 ? "" : "s"} will be reverted before the Change Storage history is removed.`
            : "These records are already reviewed, so deleting them removes Change Storage history only. The database will not be changed.";
        const detail = pending.length > 0
            ? `You selected ${selected.length} record${selected.length === 1 ? "" : "s"}. Approved and ignored records in this selection only remove their stored history.`
            : `You selected ${selected.length} Change Storage record${selected.length === 1 ? "" : "s"}.`;

        const confirmed = await window.StudioDialog?.confirm({
            title: `Delete ${selected.length} Change Storage record${selected.length === 1 ? "" : "s"}?`,
            message,
            detail,
            variant: "danger",
            icon: "bi-trash3",
            confirmText: pending.length > 0 ? "Revert and delete" : "Delete history"
        });

        if (!confirmed)
        {
            return;
        }

        button.dataset.studioDialogApproved = "true";
        reviewForm?.requestSubmit(button);
    });

    reviewForm?.querySelectorAll("[data-change-delete-one]").forEach(button => {
        button.addEventListener("click", async event => {
            if (button.dataset.studioDialogApproved === "true")
            {
                delete button.dataset.studioDialogApproved;
                return;
            }

            const pending = button.dataset.changeStatus === "pending";
            const canRevert = button.dataset.changeRevert === "true";

            if (pending && !canRevert)
            {
                event.preventDefault();
                await window.StudioDialog?.alert({
                    title: "Automatic rollback unavailable",
                    message: "This pending record does not have automatic rollback data.",
                    detail: "Approve or ignore it first if you only want to remove the history record.",
                    variant: "warning",
                    icon: "bi-shield-exclamation"
                });
                return;
            }

            event.preventDefault();
            reviewSelections().forEach(input => input.checked = false);
            if (reviewSelectAll)
            {
                reviewSelectAll.checked = false;
            }

            const confirmed = await window.StudioDialog?.confirm({
                title: pending ? "Revert and delete this change?" : "Delete this Change Storage record?",
                message: pending
                    ? "The stored database change will be reverted first. The history record is removed only if that rollback succeeds."
                    : "This record is already reviewed. Deleting it removes Change Storage history only and does not modify the database.",
                variant: "danger",
                icon: pending ? "bi-arrow-counterclockwise" : "bi-trash3",
                confirmText: pending ? "Revert and delete" : "Delete history"
            });

            if (!confirmed)
            {
                return;
            }

            button.dataset.studioDialogApproved = "true";
            reviewForm?.requestSubmit(button);
        });
    });

    document.querySelectorAll("[data-copy-stored-sql]").forEach(button => {
        button.addEventListener("click", async () => {
            const pre = button.closest(".change-storage-sql-box")?.querySelector("pre");
            if (!pre)
            {
                return;
            }

            const copied = await copyText(pre.textContent || "");
            setTemporaryButtonState(button, copied ? "Copied" : "Copy failed", copied ? "bi-check2" : "bi-x-lg");
        });
    });

    const builder = document.querySelector("[data-change-contribution-builder]");
    if (!builder)
    {
        return;
    }

    const output = builder.querySelector("[data-contribution-output]");
    const state = builder.querySelector("[data-contribution-state]");
    const fields = Array.from(builder.querySelectorAll("[data-contrib-field]"));
    const selections = () => Array.from(builder.querySelectorAll("[data-contrib-select]"));
    const selectAll = builder.querySelector("[data-contrib-select-all]");
    const copyButton = builder.querySelector("[data-copy-contribution]");
    const downloadButton = builder.querySelector("[data-download-contribution]");
    let generatedSql = "";

    function readMetadata() {
        const metadata = {};
        fields.forEach(field => metadata[field.dataset.contribField || ""] = String(field.value || "").trim());
        const required = ["oldVersion", "oldStructure", "oldContent", "newVersion", "newStructure", "newContent", "description", "comment"];
        return { metadata, missing: required.filter(key => !metadata[key]) };
    }

    function readSelectedSql() {
        return selections()
            .filter(input => input.checked)
            .map(input => {
                const row = input.closest(".change-storage-approved-item");
                const script = row?.querySelector("[data-contrib-sql]");
                if (!script)
                {
                    return "";
                }

                try
                {
                    const value = JSON.parse(script.textContent || '""');
                    return typeof value === "string" ? value.trim() : "";
                }
                catch
                {
                    return "";
                }
            })
            .filter(Boolean);
    }

    function render() {
        if (!output || !state)
        {
            return;
        }

        const { metadata, missing } = readMetadata();
        const statements = readSelectedSql();
        generatedSql = "";
        if (copyButton)
        {
            copyButton.disabled = true;
        }

        if (downloadButton)
        {
            downloadButton.disabled = true;
        }

        builder.querySelectorAll("[data-contrib-count]").forEach(counter => {
            const key = counter.dataset.contribCount || "";
            counter.textContent = String((metadata[key] || "").length);
        });
        if (selectAll)
        {
            const items = selections();
            selectAll.checked = items.length > 0 && items.every(input => input.checked);
            selectAll.indeterminate = items.some(input => input.checked) && !selectAll.checked;
        }

        if (statements.length === 0)
        {
            output.textContent = "-- Select at least one approved change to include in the contribution.";
            state.textContent = "No approved SQL selected.";
            return;
        }

        if (missing.length > 0)
        {
            output.textContent = "-- Complete all Expected Values, New Values, DESCRIPTION, and COMMENT fields to generate the contribution.\n" +
                `-- Selected approved changes: ${statements.length}`;
            state.textContent = `${missing.length} metadata field${missing.length === 1 ? "" : "s"} still required.`;
            return;
        }

        generatedSql = buildMangosContributionScript(metadata, statements);
        output.textContent = generatedSql;
        state.textContent = `${statements.length} approved change${statements.length === 1 ? "" : "s"} included. Contribution SQL is ready.`;
        if (copyButton)
        {
            copyButton.disabled = false;
        }

        if (downloadButton)
        {
            downloadButton.disabled = false;
        }
    }

    fields.forEach(field => field.addEventListener("input", render));
    builder.addEventListener("change", event => {
        if (event.target?.matches?.("[data-contrib-select]"))
        {
            render();
        }
    });
    selectAll?.addEventListener("change", () => {
        selections().forEach(input => input.checked = selectAll.checked);
        render();
    });
    copyButton?.addEventListener("click", async () => {
        if (!generatedSql)
        {
            return;
        }

        const copied = await copyText(generatedSql);
        setTemporaryButtonState(copyButton, copied ? "Copied" : "Copy failed", copied ? "bi-check2" : "bi-x-lg");
    });

    downloadButton?.addEventListener("click", () => {
        if (!generatedSql)
        {
            return;
        }

        const { metadata } = readMetadata();
        const blob = new Blob([generatedSql + "\n"], { type: "text/sql;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = buildContributionFileName(metadata);
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    });

    render();

    function buildMangosContributionScript(metadata, statements) {
        const sqlBody = statements.length > 0
            ? statements.map(statement => indentStatement(statement, 8)).join("\n\n")
            : "        -- No approved contribution SQL selected.";

        return [
            "-- ----------------------------------------------------------------",
            "-- MaNGOS Transactional Database Update Procedure",
            "-- Generated by Mangos Database Studio Change Storage",
            "-- ----------------------------------------------------------------",
            "DROP PROCEDURE IF EXISTS `update_mangos`;",
            "",
            "DELIMITER $$",
            "",
            "CREATE DEFINER=`root`@`localhost` PROCEDURE `update_mangos`()",
            "BEGIN",
            "    DECLARE bRollback BOOL DEFAULT FALSE;",
            "    DECLARE CONTINUE HANDLER FOR SQLEXCEPTION SET bRollback = TRUE;",
            "",
            "    -- Current Database Version",
            "    SET @cCurVersion = NULL;",
            "    SET @cCurStructure = NULL;",
            "    SET @cCurContent = NULL;",
            "    SET @cCurResult = NULL;",
            "",
            "    SET @bCurrentExists := EXISTS (",
            "        SELECT 1 FROM `db_version` LIMIT 1",
            "    );",
            "",
            "    IF (@bCurrentExists = TRUE) THEN",
            "        SELECT `version`, `structure`, `content`, `description`",
            "          INTO @cCurVersion, @cCurStructure, @cCurContent, @cCurResult",
            "          FROM `db_version`",
            "         ORDER BY `version` DESC, `structure` DESC, `content` DESC",
            "         LIMIT 1;",
            "    END IF;",
            "",
            "    -- Expected Values",
            `    SET @cOldVersion = ${quoteValue(metadata.oldVersion)};`,
            `    SET @cOldStructure = ${quoteValue(metadata.oldStructure)};`,
            `    SET @cOldContent = ${quoteValue(metadata.oldContent)};`,
            "",
            "    -- New Values",
            `    SET @cNewVersion = ${quoteValue(metadata.newVersion)};`,
            `    SET @cNewStructure = ${quoteValue(metadata.newStructure)};`,
            `    SET @cNewContent = ${quoteValue(metadata.newContent)};`,
            "",
            "    -- DESCRIPTION IS 30 CHARACTERS MAX",
            `    SET @cNewDescription = ${quoteValue(metadata.description)};`,
            "",
            "    -- COMMENT IS 150 CHARACTERS MAX",
            `    SET @cNewComment = ${quoteValue(metadata.comment)};`,
            "",
            "    SET @bOldExists := EXISTS (",
            "        SELECT 1 FROM `db_version`",
            "         WHERE `version` <=> @cOldVersion",
            "           AND `structure` <=> @cOldStructure",
            "           AND `content` <=> @cOldContent",
            "    );",
            "",
            "    SET @bNewExists := EXISTS (",
            "        SELECT 1 FROM `db_version`",
            "         WHERE `version` <=> @cNewVersion",
            "           AND `structure` <=> @cNewStructure",
            "           AND `content` <=> @cNewContent",
            "    );",
            "",
            "    SET @oldResult := (",
            "        SELECT `description` FROM `db_version`",
            "         WHERE `version` <=> @cOldVersion",
            "           AND `structure` <=> @cOldStructure",
            "           AND `content` <=> @cOldContent",
            "         LIMIT 1",
            "    );",
            "",
            "    SET @newResult := (",
            "        SELECT `description` FROM `db_version`",
            "         WHERE `version` <=> @cNewVersion",
            "           AND `structure` <=> @cNewStructure",
            "           AND `content` <=> @cNewContent",
            "         LIMIT 1",
            "    );",
            "",
            "    IF (",
            "        @cCurVersion <=> @cOldVersion",
            "        AND @cCurStructure <=> @cOldStructure",
            "        AND @cCurContent <=> @cOldContent",
            "    ) THEN",
            "        START TRANSACTION;",
            "",
            "        -- ------------------------------------------------------------",
            "        -- APPROVED DATABASE STUDIO CHANGES BELOW",
            "        -- ------------------------------------------------------------",
            sqlBody,
            "        -- ------------------------------------------------------------",
            "        -- APPROVED DATABASE STUDIO CHANGES ABOVE",
            "        -- ------------------------------------------------------------",
            "",
            "        IF bRollback = TRUE THEN",
            "            ROLLBACK;",
            "            SHOW ERRORS;",
            "            SELECT '* UPDATE FAILED *' AS `===== Status =====`, @cCurResult AS `===== DB is on Version: =====`;",
            "        ELSE",
            "            COMMIT;",
            "            INSERT INTO `db_version` VALUES (@cNewVersion, @cNewStructure, @cNewContent, @cNewDescription, @cNewComment);",
            "            SET @newResult := (SELECT `description` FROM `db_version` WHERE `version` <=> @cNewVersion AND `structure` <=> @cNewStructure AND `content` <=> @cNewContent LIMIT 1);",
            "            SELECT '* UPDATE COMPLETE *' AS `===== Status =====`, @newResult AS `===== DB is now on Version =====`;",
            "        END IF;",
            "    ELSE",
            "        IF (",
            "            @cCurVersion <=> @cNewVersion",
            "            AND @cCurStructure <=> @cNewStructure",
            "            AND @cCurContent <=> @cNewContent",
            "        ) THEN",
            "            SELECT '* UPDATE SKIPPED *' AS `===== Status =====`, @cCurResult AS `===== DB is already on Version =====`;",
            "        ELSE",
            "            IF (@bCurrentExists = FALSE) THEN",
            "                SELECT '* UPDATE FAILED *' AS `===== Status =====`, 'Unable to locate DB Version Information' AS `============= Error Message =============`;",
            "            ELSE",
            "                SET @cCurOutput = CONCAT(@cCurVersion, '_', @cCurStructure, '_', @cCurContent, ' - ', @cCurResult);",
            "                IF (@bOldExists = FALSE) THEN",
            "                    SET @oldResult = CONCAT('Rel', @cOldVersion, '_', @cOldStructure, '_', @cOldContent, ' - IS NOT APPLIED');",
            "                    SELECT '* UPDATE SKIPPED *' AS `===== Status =====`, @oldResult AS `=== Expected ===`, @cCurOutput AS `===== Found Version =====`;",
            "                ELSE",
            "                    SELECT '* UPDATE SKIPPED *' AS `===== Status =====`, @oldResult AS `=== Expected ===`, @cCurOutput AS `===== Found Version =====`;",
            "                END IF;",
            "            END IF;",
            "        END IF;",
            "    END IF;",
            "END $$",
            "",
            "DELIMITER ;",
            "",
            "CALL update_mangos();",
            "DROP PROCEDURE IF EXISTS `update_mangos`;"
        ].join("\n");
    }

    function buildContributionFileName(metadata) {
        const clean = value => String(value || "")
            .trim()
            .replace(/[^a-zA-Z0-9._-]+/g, "_")
            .replace(/^_+|_+$/g, "") || "0";
        const description = clean(metadata.description).replace(/\.+$/g, "") || "update";
        return `Rel${clean(metadata.newVersion)}_${clean(metadata.newStructure)}_${clean(metadata.newContent)}_${description}.sql`;
    }

    function indentStatement(statement, spaces) {
        const indent = " ".repeat(spaces);
        return String(statement || "").trim().split(/\r?\n/).map(line => indent + line).join("\n");
    }

    function quoteValue(value) {
        return `'${String(value ?? "").replace(/\\/g, "\\\\").replace(/'/g, "''")}'`;
    }

    async function copyText(text) {
        try
        {
            if (navigator.clipboard?.writeText)
            {
                await navigator.clipboard.writeText(text);
                return true;
            }

            const textarea = document.createElement("textarea");
            textarea.value = text;
            textarea.style.position = "fixed";
            textarea.style.opacity = "0";
            document.body.appendChild(textarea);
            textarea.select();
            const copied = document.execCommand("copy");
            textarea.remove();
            return copied;
        }
        catch
        {
            return false;
        }
    }

    function setTemporaryButtonState(button, label, icon) {
        if (!button)
        {
            return;
        }

        const original = button.innerHTML;
        button.innerHTML = `<i class="bi ${icon}"></i> ${label}`;
        button.disabled = true;
        setTimeout(() => {
            button.innerHTML = original;
            button.disabled = false;
        }, 1200);
    }
})();
