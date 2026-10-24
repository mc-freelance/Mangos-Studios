// npc-vendor-editor user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const getModal = (id) => {
        const element = document.getElementById(id);
        if (!element || !window.bootstrap?.Modal)
        {
            return null;
        }

        return window.bootstrap.Modal.getOrCreateInstance(element);
    };

    const readDatasetItem = (button) => ({
        ItemId: button.dataset.itemId || "",
        Slot: button.dataset.slot || "",
        MaxCount: button.dataset.maxCount || "",
        IncrementTime: button.dataset.incrementTime || "",
        ExtendedCost: button.dataset.extendedCost || "",
        ConditionId: button.dataset.conditionId || ""
    });

    const setFields = (root, selectorPrefix, values) => {
        Object.entries(values).forEach(([field, value]) => {
            const input = root.querySelector(`[${selectorPrefix}="${field}"]`);
            if (input)
            {
                input.value = value;
            }
        });
    };

    const viewer = document.querySelector("[data-npc-vendor-item-search-url]");
    const itemSearchUrl = viewer?.dataset.npcVendorItemSearchUrl || "";
    const database = viewer?.dataset.npcVendorDatabase || "";
    const itemPageSize = 100;
    let activePicker = null;
    let searchTimer = 0;
    let requestToken = 0;

    const pickerPopover = document.createElement("div");
    pickerPopover.className = "npc-vendor-item-picker-popover";
    pickerPopover.hidden = true;
    pickerPopover.innerHTML = `
        <div class="npc-vendor-item-picker-search">
            <i class="bi bi-search"></i>
            <input type="search" autocomplete="off" placeholder="Search item name or ID" aria-label="Search item_template" />
        </div>
        <div class="npc-vendor-item-picker-results" role="listbox" aria-label="Items" tabindex="-1"></div>
        <div class="npc-vendor-item-picker-status" aria-live="polite">Open the list to browse item_template.</div>`;
    document.body.appendChild(pickerPopover);

    const pickerSearch = pickerPopover.querySelector("input[type='search']");
    const pickerResults = pickerPopover.querySelector(".npc-vendor-item-picker-results");
    const pickerStatus = pickerPopover.querySelector(".npc-vendor-item-picker-status");

    const getPickerButton = picker => picker?.querySelector("[data-npc-vendor-item-picker-toggle]") || null;
    const getPickerInput = picker => picker?.querySelector("[data-npc-vendor-item-id]") || null;
    const getPickerLabel = picker => picker?.querySelector("[data-npc-vendor-item-picker-label]") || null;

    const setPickerValue = (picker, itemId, itemName) => {
        if (!picker)
        {
            return;
        }

        const input = getPickerInput(picker);
        const label = getPickerLabel(picker);
        const button = getPickerButton(picker);
        const normalizedId = String(itemId ?? "").trim();
        const normalizedName = String(itemName ?? "").trim();

        if (input)
        {
            input.value = normalizedId;
            input.dispatchEvent(new Event("input", { bubbles: true }));
        }

        if (label)
        {
            label.textContent = normalizedId
                ? `${normalizedName || "Item"} (ID: ${normalizedId})`
                : "Select an item...";
        }

        if (button)
        {
            button.classList.toggle("is-placeholder", !normalizedId);
            button.classList.remove("is-invalid");
            button.title = normalizedId ? `${normalizedName || "Item"} · ${normalizedId}` : "Select an item";
        }

        picker.dataset.selectedItemName = normalizedName;
    };

    const resetPicker = picker => {
        setPickerValue(picker, "", "");
        getPickerButton(picker)?.setAttribute("aria-expanded", "false");
    };

    const positionPickerPopover = () => {
        const button = getPickerButton(activePicker);
        if (!button || pickerPopover.hidden)
        {
            return;
        }

        const rect = button.getBoundingClientRect();
        const viewportPadding = 12;
        const preferredWidth = Math.max(rect.width, 360);
        const width = Math.min(preferredWidth, window.innerWidth - (viewportPadding * 2));
        const left = Math.min(
            Math.max(viewportPadding, rect.left),
            Math.max(viewportPadding, window.innerWidth - width - viewportPadding));
        const spaceBelow = window.innerHeight - rect.bottom - viewportPadding;
        const spaceAbove = rect.top - viewportPadding;
        const openAbove = spaceBelow < 300 && spaceAbove > spaceBelow;

        pickerPopover.style.width = `${width}px`;
        pickerPopover.style.left = `${left}px`;
        pickerPopover.style.top = openAbove ? "auto" : `${rect.bottom + 6}px`;
        pickerPopover.style.bottom = openAbove ? `${window.innerHeight - rect.top + 6}px` : "auto";
    };

    const closePicker = () => {
        if (activePicker)
        {
            const button = getPickerButton(activePicker);
            button?.setAttribute("aria-expanded", "false");
        }

        activePicker = null;
        pickerPopover.hidden = true;
        pickerPopover.classList.remove("is-loading");
        requestToken += 1;
    };

    const renderItemOption = item => {
        const option = document.createElement("button");
        option.type = "button";
        option.className = "npc-vendor-item-picker-option";
        option.setAttribute("role", "option");
        option.dataset.itemId = String(item.id);
        option.dataset.itemName = String(item.name || "Unnamed item");

        const name = document.createElement("strong");
        name.textContent = String(item.name || "Unnamed item");
        const id = document.createElement("span");
        id.textContent = `ID: ${item.id}`;
        option.append(name, id);
        return option;
    };

    const pickerState = {
        search: "",
        page: 0,
        totalPages: 0,
        totalCount: 0,
        loading: false
    };

    const loadPickerItems = async (reset = false) => {
        if (!activePicker || !itemSearchUrl || pickerState.loading)
        {
            return;
        }

        if (reset)
        {
            pickerState.page = 0;
            pickerState.totalPages = 0;
            pickerState.totalCount = 0;
            pickerResults.replaceChildren();
        }
        else if (pickerState.totalPages > 0 && pickerState.page >= pickerState.totalPages)
        {
            return;
        }

        const nextPage = pickerState.page + 1;
        const token = ++requestToken;
        pickerState.loading = true;
        pickerPopover.classList.add("is-loading");
        pickerStatus.textContent = nextPage === 1 ? "Loading items..." : "Loading more items...";

        try
        {
            const url = new URL(itemSearchUrl, window.location.origin);
            url.searchParams.set("database", database);
            url.searchParams.set("search", pickerState.search);
            url.searchParams.set("page", String(nextPage));
            url.searchParams.set("pageSize", String(itemPageSize));

            const response = await fetch(url, {
                method: "GET",
                headers: { "Accept": "application/json" },
                credentials: "same-origin"
            });
            if (!response.ok)
            {
                let message = "Unable to load items.";
                try
                {
                    const error = await response.json();
                    if (error?.error)
                    {
                        message = error.error;
                    }
                }
                catch
                {
                    // Keep the generic message.
                }

                throw new Error(message);
            }

            const payload = await response.json();
            if (token !== requestToken || !activePicker)
            {
                return;
            }

            const items = Array.isArray(payload.items) ? payload.items : [];
            items.forEach(item => pickerResults.appendChild(renderItemOption(item)));
            pickerState.page = Number(payload.page || nextPage);
            pickerState.totalPages = Number(payload.totalPages || 0);
            pickerState.totalCount = Number(payload.totalCount || items.length);

            if (pickerState.totalCount === 0)
            {
                pickerStatus.textContent = "No items match this search.";
            }
            else if (pickerState.totalPages > 0 && pickerState.page >= pickerState.totalPages)
            {
                pickerStatus.textContent = `${pickerState.totalCount.toLocaleString()} items · end of list`;
            }
            else
            {
                pickerStatus.textContent = `${pickerState.totalCount.toLocaleString()} items · scroll for more`;
            }
        }
        catch (error)
        {
            if (token === requestToken)
            {
                pickerStatus.textContent = error?.message || "Unable to load items.";
            }
        }
        finally
        {
            if (token === requestToken)
            {
                pickerState.loading = false;
                pickerPopover.classList.remove("is-loading");
            }
        }
    };

    const openPicker = picker => {
        if (!picker)
        {
            return;
        }

        if (activePicker === picker && !pickerPopover.hidden)
        {
            closePicker();
            return;
        }

        if (activePicker)
        {
            getPickerButton(activePicker)?.setAttribute("aria-expanded", "false");
        }

        activePicker = picker;
        const owningModal = picker.closest(".npc-vendor-editor-modal");
        if (owningModal && pickerPopover.parentElement !== owningModal)
        {
            owningModal.appendChild(pickerPopover);
        }

        getPickerButton(activePicker)?.setAttribute("aria-expanded", "true");
        pickerPopover.hidden = false;
        pickerSearch.value = "";
        pickerState.search = "";
        pickerResults.replaceChildren();
        pickerState.page = 0;
        pickerState.totalPages = 0;
        pickerState.totalCount = 0;
        positionPickerPopover();
        loadPickerItems(true);
        window.setTimeout(() => pickerSearch.focus(), 0);
    };

    document.addEventListener("click", event => {
        const toggle = event.target.closest("[data-npc-vendor-item-picker-toggle]");
        if (toggle)
        {
            event.preventDefault();
            openPicker(toggle.closest("[data-npc-vendor-item-picker]"));
            return;
        }

        const option = event.target.closest(".npc-vendor-item-picker-option");
        if (option && activePicker && pickerPopover.contains(option))
        {
            event.preventDefault();
            setPickerValue(activePicker, option.dataset.itemId, option.dataset.itemName);
            closePicker();
            return;
        }

        if (!pickerPopover.hidden && !pickerPopover.contains(event.target))
        {
            closePicker();
        }
    });

    pickerSearch.addEventListener("input", () => {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            pickerState.search = pickerSearch.value.trim();
            loadPickerItems(true);
        }, 220);
    });

    pickerResults.addEventListener("scroll", () => {
        if (pickerResults.scrollTop + pickerResults.clientHeight >= pickerResults.scrollHeight - 80)
        {
            loadPickerItems(false);
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && !pickerPopover.hidden)
        {
            closePicker();
        }
    });

    window.addEventListener("resize", positionPickerPopover);
    window.addEventListener("scroll", positionPickerPopover, true);

    const addForm = document.querySelector("[data-npc-vendor-add-form]");
    const addModal = getModal("npcVendorAddModal");
    const addRows = addForm?.querySelector("[data-npc-vendor-add-rows]") || null;
    const reindexRows = () => {
        if (!addRows)
        {
            return;
        }

        const rows = Array.from(addRows.querySelectorAll("[data-npc-vendor-add-row-item]"));
        rows.forEach((row, index) => {
            const number = row.querySelector("[data-row-number]");
            if (number)
            {
                number.textContent = String(index + 1);
            }

            row.querySelectorAll("[data-field]").forEach(input => {
                input.name = `Items[${index}].${input.dataset.field}`;
            });
            const remove = row.querySelector("[data-npc-vendor-remove-row]");
            if (remove)
            {
                remove.disabled = rows.length === 1;
            }
        });
    };

    const validatePickerInputs = root => {
        const pickers = Array.from(root.querySelectorAll("[data-npc-vendor-item-picker]"));
        const invalid = pickers.find(picker => !String(getPickerInput(picker)?.value || "").trim());
        pickers.forEach(picker => getPickerButton(picker)?.classList.remove("is-invalid"));
        if (!invalid)
        {
            return true;
        }

        getPickerButton(invalid)?.classList.add("is-invalid");
        openPicker(invalid);
        return false;
    };

    document.querySelectorAll("[data-npc-vendor-add-open]").forEach(button => {
        button.addEventListener("click", () => {
                addModal?.show();
        });
    });

    addForm?.querySelector("[data-npc-vendor-add-row]")?.addEventListener("click", () => {
        if (!addRows)
        {
            return;
        }

        const first = addRows.querySelector("[data-npc-vendor-add-row-item]");
        if (!first)
        {
            return;
        }

        const clone = first.cloneNode(true);
        clone.querySelectorAll("input[data-field]").forEach(input => {
            input.value = input.dataset.field === "ItemId" ? "" : "0";
        });
        resetPicker(clone.querySelector("[data-npc-vendor-item-picker]"));
        addRows.appendChild(clone);
        reindexRows();
        getPickerButton(clone.querySelector("[data-npc-vendor-item-picker]"))?.focus();
    });

    addRows?.addEventListener("click", event => {
        const remove = event.target.closest("[data-npc-vendor-remove-row]");
        if (!remove || remove.disabled)
        {
            return;
        }

        const row = remove.closest("[data-npc-vendor-add-row-item]");
        if (activePicker && row?.contains(activePicker))
        {
            closePicker();
        }

        row?.remove();
        reindexRows();
    });

    addForm?.addEventListener("submit", event => {
        if (!validatePickerInputs(addForm))
        {
            event.preventDefault();
            event.stopPropagation();
        }
    });


    const editForm = document.querySelector("[data-npc-vendor-edit-form]");
    const editModal = getModal("npcVendorEditModal");
    document.querySelectorAll("[data-npc-vendor-edit-open]").forEach(button => {
        if (button.disabled || !editForm)
        {
            return;
        }

        button.addEventListener("click", () => {
            const values = readDatasetItem(button);
            setFields(editForm, "data-original-field", values);
            setFields(editForm, "data-replacement-field", values);
            setPickerValue(
                editForm.querySelector("[data-npc-vendor-item-picker]"),
                values.ItemId,
                button.dataset.itemName || "Vendor item");
            const title = editForm.querySelector("[data-npc-vendor-edit-title]");
            if (title)
            {
                title.textContent = `${button.dataset.itemName || "Vendor item"} · Item ${values.ItemId}`;
            }

            editModal?.show();
        });
    });

    editForm?.addEventListener("submit", event => {
        if (!validatePickerInputs(editForm))
        {
            event.preventDefault();
            event.stopPropagation();
        }
    });


    const deleteForm = document.querySelector("[data-npc-vendor-delete-form]");
    const deleteModal = getModal("npcVendorDeleteModal");
    document.querySelectorAll("[data-npc-vendor-delete-open]").forEach(button => {
        if (button.disabled || !deleteForm)
        {
            return;
        }

        button.addEventListener("click", () => {
            const values = readDatasetItem(button);
            setFields(deleteForm, "data-delete-field", values);
            const title = deleteForm.querySelector("[data-npc-vendor-delete-title]");
            if (title)
            {
                title.textContent = `${button.dataset.itemName || "Vendor item"} (Item ${values.ItemId})`;
            }

            deleteModal?.show();
        });
    });

    document.querySelectorAll(".npc-vendor-editor-modal").forEach(modal => {
        modal.addEventListener("hidden.bs.modal", closePicker);
    });

    document.querySelectorAll("[data-npc-vendor-item-picker]").forEach(resetPicker);
    reindexRows();
})();
