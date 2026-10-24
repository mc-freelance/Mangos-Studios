// gossip-menu-viewer user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    const root = document.querySelector('[data-gossip-menu-viewer]');
    if (!root)
    {
        return;
    }

    const setValue = (scope, selector, value) => {
        const input = scope?.querySelector(selector);
        if (!input)
        {
            return;
        }

        input.value = value ?? '';
    };

    const relationshipsModal = document.getElementById('gossipRelationshipsModal');
    relationshipsModal?.addEventListener('show.bs.modal', event => {
        const requestedTab = event.relatedTarget?.getAttribute('data-gossip-relationship-tab');
        const tabSelector = requestedTab === 'parents' ? '#gossipParentsTab' : '#gossipSourcesTab';
        const tabButton = relationshipsModal.querySelector(tabSelector);
        const tabApi = window.bootstrap?.Tab;
        if (tabButton && tabApi)
        {
            tabApi.getOrCreateInstance(tabButton).show();
        }
    });

    const addTextModal = document.getElementById('gossipAddTextModal');
    addTextModal?.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        const input = addTextModal.querySelector('[data-gossip-add-entry]');
        if (!input || !trigger)
        {
            return;
        }

        if (trigger.matches('[data-gossip-new-menu]'))
        {
            input.value = trigger.getAttribute('data-entry') || '';
            input.removeAttribute('readonly');
        }
        else if (trigger.matches('[data-gossip-add-text]'))
        {
            input.value = trigger.getAttribute('data-entry') || '';
            input.setAttribute('readonly', 'readonly');
        }
    });

    const editTextModal = document.getElementById('gossipEditTextModal');
    editTextModal?.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        if (!trigger)
        {
            return;
        }

        const textId = trigger.getAttribute('data-text-id') || '';
        const scriptId = trigger.getAttribute('data-script-id') || '';
        const conditionId = trigger.getAttribute('data-condition-id') || '';
        setValue(editTextModal, '[data-edit-original-text]', textId);
        setValue(editTextModal, '[data-edit-original-script]', scriptId);
        setValue(editTextModal, '[data-edit-original-condition]', conditionId);
        setValue(editTextModal, '[data-edit-text]', textId);
        setValue(editTextModal, '[data-edit-script]', scriptId);
        setValue(editTextModal, '[data-edit-condition]', conditionId);
    });

    const deleteTextModal = document.getElementById('gossipDeleteTextModal');
    deleteTextModal?.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        if (!trigger)
        {
            return;
        }

        const textId = trigger.getAttribute('data-text-id') || '';
        setValue(deleteTextModal, '[data-delete-text]', textId);
        setValue(deleteTextModal, '[data-delete-script]', trigger.getAttribute('data-script-id') || '');
        setValue(deleteTextModal, '[data-delete-condition]', trigger.getAttribute('data-condition-id') || '');
        const label = deleteTextModal.querySelector('[data-delete-text-label]');
        if (label)
        {
            label.textContent = textId;
        }
    });

    const optionAttributes = {
        Id: 'data-id',
        OptionIcon: 'data-icon',
        OptionText: 'data-text',
        OptionBroadcastTextId: 'data-broadcast',
        OptionId: 'data-option-type',
        NpcOptionNpcFlag: 'data-npc-flag',
        ActionMenuId: 'data-action-menu',
        ActionPoiId: 'data-action-poi',
        ActionScriptId: 'data-action-script',
        BoxCoded: 'data-box-coded',
        BoxMoney: 'data-box-money',
        BoxText: 'data-box-text',
        BoxBroadcastTextId: 'data-box-broadcast',
        ConditionId: 'data-condition'
    };

    const fillOptionFields = (modal, trigger) => {
        modal.querySelectorAll('[data-gossip-option-field]').forEach(input => {
            const field = input.getAttribute('data-gossip-option-field');
            const attribute = optionAttributes[field];
            if (!attribute)
            {
                return;
            }

            input.value = trigger.getAttribute(attribute) || (field === 'BoxCoded' ? 'false' : '');
        });
    };

    const setOriginalOption = (container, trigger) => {
        if (!container || !trigger)
        {
            return;
        }

        container.replaceChildren();
        Object.entries(optionAttributes).forEach(([field, attribute]) => {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = `Original.${field}`;
            input.value = trigger.getAttribute(attribute) || (field === 'BoxCoded' ? 'false' : '');
            container.appendChild(input);
        });
    };

    const editOptionModal = document.getElementById('gossipEditOptionModal');
    editOptionModal?.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        if (!trigger)
        {
            return;
        }

        fillOptionFields(editOptionModal, trigger);
        setOriginalOption(editOptionModal.querySelector('[data-gossip-original-option]'), trigger);
    });

    const deleteOptionModal = document.getElementById('gossipDeleteOptionModal');
    deleteOptionModal?.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        if (!trigger)
        {
            return;
        }

        const id = trigger.getAttribute('data-id') || '';
        setOriginalOption(deleteOptionModal.querySelector('[data-gossip-delete-original]'), trigger);
        const label = deleteOptionModal.querySelector('[data-delete-option-label]');
        if (label)
        {
            label.textContent = id;
        }
    });
})();
