// npc-trainer-editor user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const getModal = id => {
        const element = document.getElementById(id);
        if (!element || !window.bootstrap?.Modal)
        {
            return null;
        }

        return window.bootstrap.Modal.getOrCreateInstance(element);
    };

    const readSpell = button => ({
        SpellId: button.dataset.spellId || "",
        SpellCost: button.dataset.spellCost || "",
        RequiredSpell: button.dataset.requiredSpell || "",
        RequiredSkill: button.dataset.requiredSkill || "",
        RequiredSkillValue: button.dataset.requiredSkillValue || "",
        RequiredLevel: button.dataset.requiredLevel || "",
        ConditionId: button.dataset.conditionId || ""
    });

    const setFields = (root, selectorAttribute, values) => {
        Object.entries(values).forEach(([field, value]) => {
            const input = root?.querySelector(`[${selectorAttribute}="${field}"]`);
            if (input)
            {
                input.value = value;
            }
        });
    };

    const viewer = document.querySelector(".npc-trainer-viewer");
    const spellSearchUrl = viewer?.dataset.npcTrainerSpellSearchUrl || "";
    const database = viewer?.dataset.npcTrainerDatabase || "";
    let activeSpellInput = null;
    let spellSearchTimer = 0;
    let spellRequestToken = 0;
    const spellState = { search: "", page: 0, totalPages: 0, totalCount: 0, loading: false };

    const spellPopover = document.createElement("div");
    spellPopover.className = "npc-vendor-item-picker-popover";
    spellPopover.hidden = true;
    spellPopover.innerHTML = `
        <div class="npc-vendor-item-picker-search">
            <i class="bi bi-search"></i>
            <input type="search" autocomplete="off" placeholder="Search spell name or ID" aria-label="Search spell_template" />
        </div>
        <div class="npc-vendor-item-picker-results" role="listbox" aria-label="Spells" tabindex="-1"></div>
        <div class="npc-vendor-item-picker-status" aria-live="polite">Browse spell_template.</div>`;
    document.body.appendChild(spellPopover);
    const spellSearch = spellPopover.querySelector("input[type='search']");
    const spellResults = spellPopover.querySelector(".npc-vendor-item-picker-results");
    const spellStatus = spellPopover.querySelector(".npc-vendor-item-picker-status");

    const positionSpellPopover = () => {
        const button = activeSpellInput?.parentElement?.querySelector("[data-npc-trainer-spell-browse]");
        if (!button || spellPopover.hidden)
        {
            return;
        }

        const rect = button.getBoundingClientRect();
        const padding = 12;
        const width = Math.min(Math.max(rect.width, 360), window.innerWidth - padding * 2);
        const left = Math.min(Math.max(padding, rect.left), Math.max(padding, window.innerWidth - width - padding));
        const below = window.innerHeight - rect.bottom - padding;
        const above = rect.top - padding;
        const openAbove = below < 300 && above > below;
        spellPopover.style.width = `${width}px`;
        spellPopover.style.left = `${left}px`;
        spellPopover.style.top = openAbove ? "auto" : `${rect.bottom + 6}px`;
        spellPopover.style.bottom = openAbove ? `${window.innerHeight - rect.top + 6}px` : "auto";
    };

    const closeSpellPicker = () => {
        activeSpellInput = null;
        spellPopover.hidden = true;
        spellRequestToken += 1;
    };

    const renderSpellOption = spell => {
        const option = document.createElement("button");
        option.type = "button";
        option.className = "npc-vendor-item-picker-option";
        option.dataset.spellId = String(spell.id);
        option.dataset.spellName = String(spell.name || "");
        const name = document.createElement("strong");
        name.textContent = String(spell.name || `Spell ${spell.id}`);
        const id = document.createElement("span");
        id.textContent = `ID: ${spell.id}`;
        option.append(name, id);
        return option;
    };

    const loadSpells = async (reset = false) => {
        if (!activeSpellInput || !spellSearchUrl || spellState.loading)
        {
            return;
        }

        if (reset)
        {
            spellState.page = 0;
            spellState.totalPages = 0;
            spellState.totalCount = 0;
            spellResults.replaceChildren();
        }
        else if (spellState.totalPages > 0 && spellState.page >= spellState.totalPages)
        {
            return;
        }

        const page = spellState.page + 1;
        const token = ++spellRequestToken;
        spellState.loading = true;
        spellPopover.classList.add("is-loading");
        spellStatus.textContent = page === 1 ? "Loading spells..." : "Loading more spells...";
        try
        {
            const url = new URL(spellSearchUrl, window.location.origin);
            url.searchParams.set("database", database);
            url.searchParams.set("search", spellState.search);
            url.searchParams.set("page", String(page));
            url.searchParams.set("pageSize", "100");
            const response = await fetch(url, { headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (!response.ok)
            {
                let message = "Unable to load spells.";
                try
                {
                    message = (await response.json())?.error || message;
                }
                catch
                {
                }

                throw new Error(message);
            }

            const payload = await response.json();
            if (token !== spellRequestToken || !activeSpellInput)
            {
                return;
            }

            const items = Array.isArray(payload.items) ? payload.items : [];
            items.forEach(spell => spellResults.appendChild(renderSpellOption(spell)));
            spellState.page = Number(payload.page || page);
            spellState.totalPages = Number(payload.totalPages || 0);
            spellState.totalCount = Number(payload.totalCount || items.length);
            spellStatus.textContent = spellState.totalCount === 0
                ? "No spells match this search."
                : (spellState.totalPages > 0 && spellState.page >= spellState.totalPages
                    ? `${spellState.totalCount.toLocaleString()} spells · end of list`
                    : `${spellState.totalCount.toLocaleString()} spells · scroll for more`);
        }
        catch (error)
        {
            if (token === spellRequestToken)
            {
                spellStatus.textContent = error?.message || "Unable to load spells.";
            }
        }
        finally
        {
            if (token === spellRequestToken)
            {
                spellState.loading = false;
                spellPopover.classList.remove("is-loading");
            }
        }
    };

    const openSpellPicker = input => {
        if (!input || !spellSearchUrl)
        {
            return;
        }

        activeSpellInput = input;
        spellPopover.hidden = false;
        spellSearch.value = "";
        spellState.search = "";
        spellResults.replaceChildren();
        positionSpellPopover();
        loadSpells(true);
        window.setTimeout(() => spellSearch.focus(), 0);
    };

    const enhanceSpellInput = input => {
        if (!spellSearchUrl || !input || input.dataset.spellPickerEnhanced === "true")
        {
            return;
        }

        input.dataset.spellPickerEnhanced = "true";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-outline-secondary btn-sm mt-1 w-100";
        button.dataset.npcTrainerSpellBrowse = "true";
        button.innerHTML = '<i class="bi bi-search"></i> Browse spells';
        button.addEventListener("click", () => openSpellPicker(input));
        input.insertAdjacentElement("afterend", button);
    };

    const enhanceSpellInputs = root => {
        root?.querySelectorAll('[data-field="SpellId"], [data-replacement-field="SpellId"]').forEach(enhanceSpellInput);
    };

    spellResults.addEventListener("click", event => {
        const option = event.target.closest(".npc-vendor-item-picker-option");
        if (!option || !activeSpellInput)
        {
            return;
        }

        activeSpellInput.value = option.dataset.spellId || "";
        activeSpellInput.title = `${option.dataset.spellName || "Spell"} · ${option.dataset.spellId || ""}`;
        activeSpellInput.dispatchEvent(new Event("input", { bubbles: true }));
        closeSpellPicker();
    });
    spellSearch.addEventListener("input", () => {
        window.clearTimeout(spellSearchTimer);
        spellSearchTimer = window.setTimeout(() => {
            spellState.search = spellSearch.value.trim();
            loadSpells(true);
        }, 220);
    });
    spellResults.addEventListener("scroll", () => {
        if (spellResults.scrollTop + spellResults.clientHeight >= spellResults.scrollHeight - 80)
        {
            loadSpells(false);
        }
    });
    document.addEventListener("click", event => {
        if (!spellPopover.hidden && !spellPopover.contains(event.target) && !event.target.closest("[data-npc-trainer-spell-browse]"))
        {
            closeSpellPicker();
        }
    });
    document.addEventListener("keydown", event =>
    {
        if (event.key === "Escape" && !spellPopover.hidden)
        {
            closeSpellPicker();
        }
    });
    window.addEventListener("resize", positionSpellPopover);
    window.addEventListener("scroll", positionSpellPopover, true);

    const addForm = document.querySelector("[data-npc-trainer-add-form]");
    const addRows = addForm?.querySelector("[data-npc-trainer-add-rows]");
    const addModal = getModal("npcTrainerAddModal");
    const reindexRows = () => {
        if (!addRows)
        {
            return;
        }

        const rows = Array.from(addRows.querySelectorAll("[data-npc-trainer-add-row-item]"));
        rows.forEach((row, index) => {
            const number = row.querySelector("[data-row-number]");
            if (number)
            {
                number.textContent = String(index + 1);
            }

            row.querySelectorAll("[data-field]").forEach(input => {
                input.name = `Spells[${index}].${input.dataset.field}`;
            });
            const remove = row.querySelector("[data-npc-trainer-remove-row]");
            if (remove)
            {
                remove.disabled = rows.length === 1;
            }
        });
    };

    document.querySelectorAll("[data-npc-trainer-add-open]").forEach(button => {
        button.addEventListener("click", () => {
            addModal?.show();
        });
    });

    addForm?.querySelector("[data-npc-trainer-add-row]")?.addEventListener("click", () => {
        const first = addRows?.querySelector("[data-npc-trainer-add-row-item]");
        if (!first || !addRows)
        {
            return;
        }

        const clone = first.cloneNode(true);
        clone.querySelectorAll("input[data-field]").forEach(input => {
            input.value = input.dataset.field === "SpellId" ? "" : "0";
        });
        addRows.appendChild(clone);
        enhanceSpellInputs(clone);
        reindexRows();
        clone.querySelector('[data-field="SpellId"]')?.focus();
    });

    addRows?.addEventListener("click", event => {
        const button = event.target.closest("[data-npc-trainer-remove-row]");
        if (!button || button.disabled)
        {
            return;
        }

        button.closest("[data-npc-trainer-add-row-item]")?.remove();
        reindexRows();
    });

    const editForm = document.querySelector("[data-npc-trainer-edit-form]");
    const editModal = getModal("npcTrainerEditModal");
    document.querySelectorAll("[data-npc-trainer-edit-open]").forEach(button => {
        if (button.disabled || !editForm)
        {
            return;
        }

        button.addEventListener("click", () => {
            const values = readSpell(button);
            setFields(editForm, "data-original-field", values);
            setFields(editForm, "data-replacement-field", values);
            const title = editForm.querySelector("[data-npc-trainer-edit-title]");
            if (title)
            {
                title.textContent = `${button.dataset.spellName || "Trainer spell"} · Spell ${values.SpellId}`;
            }

            editModal?.show();
        });
    });

    const deleteForm = document.querySelector("[data-npc-trainer-delete-form]");
    const deleteModal = getModal("npcTrainerDeleteModal");
    document.querySelectorAll("[data-npc-trainer-delete-open]").forEach(button => {
        if (button.disabled || !deleteForm)
        {
            return;
        }

        button.addEventListener("click", () => {
            const values = readSpell(button);
            setFields(deleteForm, "data-delete-field", values);
            const title = deleteForm.querySelector("[data-npc-trainer-delete-title]");
            if (title)
            {
                title.textContent = `${button.dataset.spellName || "Trainer spell"} (Spell ${values.SpellId})`;
            }

            deleteModal?.show();
        });
    });

    enhanceSpellInputs(document);
    reindexRows();
})();
