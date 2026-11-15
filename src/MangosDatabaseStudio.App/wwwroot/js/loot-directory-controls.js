// loot directory controls behavior; kept external so CSP blocks injected inline scripts.
(() => {
    const root = document.querySelector('[data-loot-viewer]');
    if (!root)
    {
        return;
    }

    const setFieldVisible = (field, visible) => {
        if (!field)
        {
            return;
        }

        field.hidden = !visible;
        field.querySelectorAll('input, select, textarea').forEach(control => { control.disabled = !visible; });
    };

    const addTable = document.querySelector('[data-loot-add-table]');
    const addKind = document.querySelector('[data-loot-add-kind]');
    const addEntry = document.querySelector('[data-loot-add-entry]');
    const addKindField = document.querySelector('[data-loot-row-kind-field]');
    const addItemPickerField = document.querySelector('[data-loot-item-picker-field]');
    const addRowKeyField = document.querySelector('[data-loot-row-key-field]');
    const addChanceField = document.querySelector('[data-loot-chance-field]');
    const addGroupField = document.querySelector('[data-loot-group-field]');
    const addMinField = document.querySelector('[data-loot-min-field]');
    const addReferenceField = document.querySelector('[data-loot-reference-field]');
    const addMaxField = document.querySelector('[data-loot-max-field]');
    const addConditionField = document.querySelector('[data-loot-condition-field]');
    const addMaxLabel = document.querySelector('[data-loot-max-label]');
    const addHelp = document.querySelector('[data-loot-row-help]');

    const syncAddFields = () => {
        const option = addTable?.selectedOptions?.[0];
        if (!option)
        {
            return;
        }

        const supportsReference = option.dataset.supportsReference === 'true';
        const isReference = supportsReference && addKind?.value === 'reference';
        setFieldVisible(addKindField, supportsReference);
        setFieldVisible(addItemPickerField, !isReference);
        setFieldVisible(addRowKeyField, isReference);
        setFieldVisible(addChanceField, option.dataset.hasChance === 'true');
        setFieldVisible(addGroupField, option.dataset.hasGroup === 'true');
        setFieldVisible(addMinField, option.dataset.hasMin === 'true' && !isReference);
        setFieldVisible(addReferenceField, supportsReference && isReference);
        setFieldVisible(addMaxField, option.dataset.hasMax === 'true');
        setFieldVisible(addConditionField, option.dataset.hasCondition === 'true');
        if (addMaxLabel)
        {
            addMaxLabel.textContent = isReference ? 'Reference repetitions' : 'Maximum count';
        }

        if (addHelp)
        {
            addHelp.textContent = isReference
                ? 'Reference rows point to reference_loot_template through the reference entry. The row key still must be unique within this loot definition. Entering a new loot entry creates a new definition with this as its first row.'
                : 'Direct rows resolve the item entry through item_template. Entering a new loot entry creates a new definition with this as its first row. Negative MaNGOS quest chances are supported.';
        }
    };
    addTable?.addEventListener('change', syncAddFields);
    addKind?.addEventListener('change', syncAddFields);
    document.querySelectorAll('[data-loot-add-open]').forEach(button => {
        button.addEventListener('click', () => {
            if (addTable && button.dataset.table)
            {
                addTable.value = button.dataset.table;
            }

            if (addEntry)
            {
                addEntry.value = button.dataset.entry || '';
            }

            syncAddFields();
        });
    });
    syncAddFields();

    const editModalElement = document.getElementById('lootEditRowModal');
    const editModal = editModalElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(editModalElement) : null;
    const syncEditKind = () => {
        const kind = document.querySelector('[data-loot-edit-kind]');
        const isReference = kind?.value === 'reference';
        setFieldVisible(document.querySelector('[data-loot-edit-item-picker-field]'), !isReference);
        setFieldVisible(document.querySelector('[data-loot-edit-row-key-field]'), isReference);
        setFieldVisible(document.querySelector('[data-loot-edit-min-field]'), !isReference);
        setFieldVisible(document.querySelector('[data-loot-edit-reference-field]'), isReference);
        const maxLabel = document.querySelector('[data-loot-edit-max-label]');
        const help = document.querySelector('[data-loot-edit-help]');
        if (maxLabel)
        {
            maxLabel.textContent = isReference ? 'Reference repetitions' : 'Maximum count';
        }

        if (help)
        {
            help.textContent = isReference
                ? 'This row resolves through reference_loot_template. The row key remains part of the loot table primary key even though it is not the referenced loot entry.'
                : 'The loot table and definition entry stay fixed. Editing changes this existing row directly and records a reversible SQL change.';
        }
    };
    document.querySelector('[data-loot-edit-kind]')?.addEventListener('change', syncEditKind);

    document.querySelectorAll('[data-loot-edit-open]').forEach(button => {
        button.addEventListener('click', () => {
            const value = key => button.dataset[key] || '';
            const assign = (selector, v) =>
            {
                const el = document.querySelector(selector);
                if (el)
                {
                    el.value = v;
                }
            };
            const originalKind = value('kind') || 'item';
            assign('[data-loot-edit-original-item]', value('item'));
            assign('[data-loot-edit-original-chance]', value('chance'));
            assign('[data-loot-edit-original-group]', value('group'));
            assign('[data-loot-edit-original-kind]', originalKind);
            assign('[data-loot-edit-original-min]', value('min'));
            assign('[data-loot-edit-original-reference]', value('reference'));
            assign('[data-loot-edit-original-max]', value('max'));
            assign('[data-loot-edit-original-condition]', value('condition'));
            const itemPicker = document.querySelector('[data-loot-edit-item-picker-field] [data-world-lookup-picker]');
            if (originalKind === 'reference')
            {
                assign('[data-loot-edit-row-key]', value('item'));
                window.WorldLookupPicker?.reset(itemPicker);
            }
            else
            {
                window.WorldLookupPicker?.setValue(itemPicker, value('item'), button.dataset.itemName || value('name') || 'Item');
                assign('[data-loot-edit-row-key]', '');
            }

            assign('[data-loot-edit-chance]', value('chance'));
            assign('[data-loot-edit-group]', value('group'));
            assign('[data-loot-edit-min]', value('min'));
            assign('[data-loot-edit-reference]', value('reference'));
            assign('[data-loot-edit-max]', value('max'));
            assign('[data-loot-edit-condition]', value('condition'));
            assign('[data-loot-edit-kind]', originalKind);
            const name = document.querySelector('[data-loot-edit-name]');
            if (name)
            {
                name.textContent = value('name') || 'Loot row';
            }

            syncEditKind();
            editModal?.show();
        });
    });

    const deleteModalElement = document.getElementById('lootDeleteRowModal');
    const deleteModal = deleteModalElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(deleteModalElement) : null;
    document.querySelectorAll('[data-loot-delete-open]').forEach(button => {
        button.addEventListener('click', () => {
            const value = key => button.dataset[key] || '';
            const assign = (selector, v) =>
            {
                const el = document.querySelector(selector);
                if (el)
                {
                    el.value = v;
                }
            };
            assign('[data-loot-delete-item]', value('item'));
            assign('[data-loot-delete-chance]', value('chance'));
            assign('[data-loot-delete-group]', value('group'));
            assign('[data-loot-delete-kind]', value('kind') || 'item');
            assign('[data-loot-delete-min]', value('min'));
            assign('[data-loot-delete-reference]', value('reference'));
            assign('[data-loot-delete-max]', value('max'));
            assign('[data-loot-delete-condition]', value('condition'));
            const name = document.querySelector('[data-loot-delete-name]');
            if (name)
            {
                name.textContent = value('name') || 'Selected loot row';
            }

            deleteModal?.show();
        });
    });
})();
