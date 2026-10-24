// world-lookup-picker user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const pickers = Array.from(document.querySelectorAll("[data-world-lookup-picker]"));
    if (pickers.length === 0)
    {
        return;
    }

    let activePicker = null;
    let searchTimer = 0;
    let requestToken = 0;
    const state = { search: "", page: 0, totalPages: 0, totalCount: 0, loading: false };

    const popover = document.createElement("div");
    popover.className = "npc-vendor-item-picker-popover world-lookup-picker-popover";
    popover.hidden = true;
    popover.innerHTML = `
        <div class="npc-vendor-item-picker-search">
            <i class="bi bi-search"></i>
            <input type="search" autocomplete="off" aria-label="Search available records" />
        </div>
        <div class="npc-vendor-item-picker-results" role="listbox" tabindex="-1"></div>
        <div class="npc-vendor-item-picker-status" aria-live="polite"></div>`;
    document.body.appendChild(popover);

    const searchInput = popover.querySelector("input[type='search']");
    const results = popover.querySelector(".npc-vendor-item-picker-results");
    const status = popover.querySelector(".npc-vendor-item-picker-status");

    const valueInput = picker => picker?.querySelector("[data-world-lookup-value]") || null;
    const toggle = picker => picker?.querySelector("[data-world-lookup-toggle]") || null;
    const label = picker => picker?.querySelector("[data-world-lookup-label]") || null;
    const pluralLabel = picker => picker?.dataset.worldLookupPlural || "records";
    const singularLabel = picker => picker?.dataset.worldLookupSingular || "Record";

    const setValue = (picker, id, name, item = null) => {
        if (!picker)
        {
            return;
        }

        const normalizedId = String(id ?? "").trim();
        const normalizedName = String(name ?? "").trim();
        const input = valueInput(picker);
        const button = toggle(picker);
        const text = label(picker);
        if (input)
        {
            input.value = normalizedId;
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
        }

        if (text)
        {
            text.textContent = normalizedId
                ? `${normalizedName || singularLabel(picker)} (ID: ${normalizedId})`
                : (picker.dataset.worldLookupPlaceholder || `Select a ${singularLabel(picker).toLowerCase()}...`);
        }

        button?.classList.toggle("is-placeholder", !normalizedId);
        button?.classList.remove("is-invalid");
        if (button)
        {
            button.title = normalizedId ? `${normalizedName || singularLabel(picker)} · ${normalizedId}` : (picker.dataset.worldLookupPlaceholder || "Select a record");
        }

        picker.dataset.selectedName = normalizedName;
        picker.dispatchEvent(new CustomEvent("worldlookupchange", {
            bubbles: true,
            detail: { id: normalizedId, name: normalizedName, item }
        }));
    };

    const reset = picker => setValue(picker, "", "");

    const position = () => {
        const button = toggle(activePicker);
        if (!button || popover.hidden)
        {
            return;
        }

        const rect = button.getBoundingClientRect();
        const padding = 12;
        const width = Math.min(Math.max(rect.width, 360), window.innerWidth - padding * 2);
        const left = Math.min(Math.max(padding, rect.left), Math.max(padding, window.innerWidth - width - padding));
        const spaceBelow = window.innerHeight - rect.bottom - padding;
        const spaceAbove = rect.top - padding;
        const openAbove = spaceBelow < 300 && spaceAbove > spaceBelow;
        popover.style.width = `${width}px`;
        popover.style.left = `${left}px`;
        popover.style.top = openAbove ? "auto" : `${rect.bottom + 6}px`;
        popover.style.bottom = openAbove ? `${window.innerHeight - rect.top + 6}px` : "auto";
    };

    const close = () => {
        toggle(activePicker)?.setAttribute("aria-expanded", "false");
        activePicker = null;
        popover.hidden = true;
        popover.classList.remove("is-loading");
        requestToken += 1;
    };

    const renderOption = item => {
        const option = document.createElement("button");
        option.type = "button";
        option.className = "npc-vendor-item-picker-option";
        option.setAttribute("role", "option");
        option.dataset.lookupId = String(item.id ?? "");
        option.dataset.lookupName = String(item.name || "");
        option.dataset.lookupItem = JSON.stringify(item);
        const name = document.createElement("strong");
        name.textContent = String(item.name || `${singularLabel(activePicker)} ${item.id}`);
        const id = document.createElement("span");
        id.textContent = `ID: ${item.id}`;
        option.append(name, id);
        return option;
    };

    const load = async (resetResults = false) => {
        if (!activePicker || state.loading)
        {
            return;
        }

        const urlValue = activePicker.dataset.worldLookupUrl || "";
        if (!urlValue)
        {
            return;
        }

        if (resetResults)
        {
            state.page = 0;
            state.totalPages = 0;
            state.totalCount = 0;
            results.replaceChildren();
        }
        else if (state.totalPages > 0 && state.page >= state.totalPages)
        {
            return;
        }

        const nextPage = state.page + 1;
        const token = ++requestToken;
        state.loading = true;
        popover.classList.add("is-loading");
        status.textContent = nextPage === 1 ? `Loading ${pluralLabel(activePicker)}...` : `Loading more ${pluralLabel(activePicker)}...`;

        try
        {
            const url = new URL(urlValue, window.location.origin);
            const database = activePicker.dataset.worldLookupDatabase || "";
            if (database)
            {
                url.searchParams.set("database", database);
            }

            url.searchParams.set("search", state.search);
            url.searchParams.set("page", String(nextPage));
            url.searchParams.set("pageSize", activePicker.dataset.worldLookupPageSize || "100");

            const response = await fetch(url, { headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (!response.ok)
            {
                let message = `Unable to load ${pluralLabel(activePicker)}.`;
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
            if (token !== requestToken || !activePicker)
            {
                return;
            }

            const items = Array.isArray(payload.items) ? payload.items : [];
            items.forEach(item => results.appendChild(renderOption(item)));
            state.page = Number(payload.page || nextPage);
            state.totalPages = Number(payload.totalPages || 0);
            state.totalCount = Number(payload.totalCount || items.length);
            if (state.totalCount === 0)
            {
                status.textContent = activePicker.dataset.worldLookupEmpty || `No ${pluralLabel(activePicker)} match this search.`;
            }
            else if (state.totalPages > 0 && state.page >= state.totalPages)
            {
                status.textContent = `${state.totalCount.toLocaleString()} ${pluralLabel(activePicker)} · end of list`;
            }
            else
            {
                status.textContent = `${state.totalCount.toLocaleString()} ${pluralLabel(activePicker)} · scroll for more`;
            }
        }
        catch (error)
        {
            if (token === requestToken)
            {
                status.textContent = error?.message || `Unable to load ${pluralLabel(activePicker)}.`;
            }
        }
        finally
        {
            if (token === requestToken)
            {
                state.loading = false;
                popover.classList.remove("is-loading");
            }
        }
    };

    const open = picker => {
        if (!picker?.dataset.worldLookupUrl)
        {
            return;
        }

        if (activePicker && activePicker !== picker)
        {
            toggle(activePicker)?.setAttribute("aria-expanded", "false");
        }

        activePicker = picker;
        toggle(picker)?.setAttribute("aria-expanded", "true");
        popover.hidden = false;
        searchInput.placeholder = picker.dataset.worldLookupSearchPlaceholder || `Search ${pluralLabel(picker)} by name or ID`;
        searchInput.setAttribute("aria-label", searchInput.placeholder);
        searchInput.value = "";
        state.search = "";
        results.replaceChildren();
        status.textContent = `Loading ${pluralLabel(picker)}...`;
        position();
        load(true);
        window.setTimeout(() => searchInput.focus(), 0);
    };

    pickers.forEach(picker => {
        toggle(picker)?.addEventListener("click", () => open(picker));
        const initialId = picker.dataset.worldLookupInitialId || valueInput(picker)?.value || "";
        const initialName = picker.dataset.worldLookupInitialName || "";
        if (initialId)
        {
            setValue(picker, initialId, initialName);
        }
        else
        {
            reset(picker);
        }
    });

    results.addEventListener("click", event => {
        const option = event.target.closest(".npc-vendor-item-picker-option");
        if (!option || !activePicker)
        {
            return;
        }

        let item = null;
        try
        {
            item = JSON.parse(option.dataset.lookupItem || "null");
        }
        catch
        {
        }

        setValue(activePicker, option.dataset.lookupId || "", option.dataset.lookupName || "", item);
        close();
    });

    searchInput.addEventListener("input", () => {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            state.search = searchInput.value.trim();
            load(true);
        }, 220);
    });
    results.addEventListener("scroll", () => {
        if (results.scrollTop + results.clientHeight >= results.scrollHeight - 80)
        {
            load(false);
        }
    });
    document.addEventListener("click", event => {
        if (!popover.hidden && !popover.contains(event.target) && !event.target.closest("[data-world-lookup-toggle]"))
        {
            close();
        }
    });
    document.addEventListener("keydown", event =>
    {
        if (event.key === "Escape" && !popover.hidden)
        {
            close();
        }
    });
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);

    document.querySelectorAll("form").forEach(form => {
        form.addEventListener("submit", event => {
            const required = Array.from(form.querySelectorAll('[data-world-lookup-picker][data-world-lookup-required="true"]'));
            const invalid = required.find(picker => {
                const input = valueInput(picker);
                return input && !input.disabled && !String(input.value || "").trim();
            });
            required.forEach(picker => toggle(picker)?.classList.remove("is-invalid"));
            if (!invalid)
            {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            toggle(invalid)?.classList.add("is-invalid");
            open(invalid);
        });
    });

    window.WorldLookupPicker = { setValue, reset, open, close };
})();
