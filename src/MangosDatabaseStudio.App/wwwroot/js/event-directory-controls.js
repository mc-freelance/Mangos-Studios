// event directory controls behavior; kept external so CSP blocks injected inline scripts.
(() => {
    const root = document.querySelector('[data-event-viewer]');
    if (!root)
    {
        return;
    }

    const sourceSelect = document.querySelector('[data-event-source-select]');
    const guidLabel = document.querySelector('[data-event-guid-label]');
    const behaviorSelect = document.querySelector('[data-event-behavior-select]');
    const negativeOption = document.querySelector('[data-event-negative-option]');
    const syncAddSource = () => {
        if (!sourceSelect)
        {
            return;
        }

        const option = sourceSelect.selectedOptions[0];
        if (!option)
        {
            return;
        }

        if (guidLabel)
        {
            guidLabel.textContent = option.dataset.memberLabel || 'Spawn GUID';
        }

        const supportsNegative = option.dataset.supportsNegative === 'true';
        if (negativeOption)
        {
            negativeOption.disabled = !supportsNegative;
        }

        if (!supportsNegative && behaviorSelect && behaviorSelect.value === '-1')
        {
            behaviorSelect.value = '1';
        }
    };
    sourceSelect?.addEventListener('change', syncAddSource);
    syncAddSource();

    const editElement = document.getElementById('eventEditMemberModal');
    const editModal = editElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(editElement) : null;
    document.querySelectorAll('[data-event-edit-member-open]').forEach(button => {
        button.addEventListener('click', () => {
            const source = document.querySelector('[data-event-edit-source]');
            const originalGuid = document.querySelector('[data-event-edit-original-guid]');
            const originalEvent = document.querySelector('[data-event-edit-original-event]');
            const guid = document.querySelector('[data-event-edit-guid]');
            const behavior = document.querySelector('[data-event-edit-behavior]');
            const negativeEditOption = document.querySelector('[data-event-edit-negative-option]');
            const label = document.querySelector('[data-event-edit-source-label]');
            const code = document.querySelector('[data-event-edit-source-code]');
            if (source)
            {
                source.value = button.dataset.source || '';
            }

            if (originalGuid)
            {
                originalGuid.value = button.dataset.guid || '';
            }

            if (originalEvent)
            {
                originalEvent.value = button.dataset.eventValue || '';
            }

            if (guid)
            {
                guid.value = button.dataset.guid || '';
            }

            const supportsNegative = button.dataset.supportsNegative === 'true';
            if (negativeEditOption)
            {
                negativeEditOption.disabled = !supportsNegative;
            }

            if (behavior)
            {
                behavior.value = supportsNegative && Number(button.dataset.eventValue || '1') < 0 ? '-1' : '1';
            }

            if (label)
            {
                label.textContent = button.dataset.sourceLabel || 'Event link';
            }

            if (code)
            {
                code.textContent = button.dataset.source || '';
            }

            editModal?.show();
        });
    });

    const removeElement = document.getElementById('eventRemoveMemberModal');
    const removeModal = removeElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(removeElement) : null;
    document.querySelectorAll('[data-event-remove-open]').forEach(button => {
        button.addEventListener('click', () => {
            const source = document.querySelector('[data-event-remove-source]');
            const guid = document.querySelector('[data-event-remove-guid]');
            const eventValue = document.querySelector('[data-event-remove-event]');
            const name = document.querySelector('[data-event-remove-name]');
            if (source)
            {
                source.value = button.dataset.source || '';
            }

            if (guid)
            {
                guid.value = button.dataset.guid || '';
            }

            if (eventValue)
            {
                eventValue.value = button.dataset.eventValue || '';
            }

            if (name)
            {
                name.textContent = button.dataset.memberName || 'Selected spawn';
            }

            removeModal?.show();
        });
    });
})();
