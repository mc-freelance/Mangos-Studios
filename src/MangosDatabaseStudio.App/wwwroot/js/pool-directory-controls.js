// pool directory controls behavior; kept external so CSP blocks injected inline scripts.
(() => {
    const root = document.querySelector('[data-pool-viewer]');
    if (!root)
    {
        return;
    }

    const sourceSelect = document.querySelector('[data-pool-source-select]');
    const memberLabel = document.querySelector('[data-pool-member-label]');
    const chanceField = document.querySelector('[data-pool-chance-field]');
    const descriptionField = document.querySelector('[data-pool-description-field]');
    const syncSourceFields = () => {
        if (!sourceSelect)
        {
            return;
        }

        const option = sourceSelect.selectedOptions[0];
        if (!option)
        {
            return;
        }

        if (memberLabel)
        {
            memberLabel.textContent = option.dataset.memberLabel || 'Member ID';
        }

        if (chanceField)
        {
            chanceField.hidden = option.dataset.hasChance !== 'true';
        }

        if (descriptionField)
        {
            descriptionField.hidden = option.dataset.hasDescription !== 'true';
        }
    };
    sourceSelect?.addEventListener('change', syncSourceFields);
    syncSourceFields();

    const editMemberModalElement = document.getElementById('poolEditMemberModal');
    const editMemberModal = editMemberModalElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(editMemberModalElement) : null;
    document.querySelectorAll('[data-pool-edit-member-open]').forEach(button => {
        button.addEventListener('click', () => {
            const source = document.querySelector('[data-pool-edit-source]');
            const originalMember = document.querySelector('[data-pool-edit-original-member]');
            const member = document.querySelector('[data-pool-edit-member]');
            const chance = document.querySelector('[data-pool-edit-chance]');
            const description = document.querySelector('[data-pool-edit-description]');
            const sourceLabel = document.querySelector('[data-pool-edit-source-label]');
            const sourceCode = document.querySelector('[data-pool-edit-source-code]');
            const resolution = document.querySelector('[data-pool-edit-resolution]');
            const resolvedName = document.querySelector('[data-pool-edit-resolved-name]');
            const resolvedEntry = document.querySelector('[data-pool-edit-resolved-entry]');
            const memberLabelElement = document.querySelector('[data-pool-edit-member-label]');
            const chanceFieldElement = document.querySelector('[data-pool-edit-chance-field]');
            const descriptionFieldElement = document.querySelector('[data-pool-edit-description-field]');

            if (source)
            {
                source.value = button.dataset.source || '';
            }

            if (originalMember)
            {
                originalMember.value = button.dataset.memberId || '';
            }

            if (member)
            {
                member.value = button.dataset.memberId || '';
            }

            if (chance)
            {
                chance.value = button.dataset.chance || '';
            }

            if (description)
            {
                description.value = button.dataset.description || '';
            }

            if (sourceLabel)
            {
                sourceLabel.textContent = button.dataset.sourceLabel || 'Pool member';
            }

            if (sourceCode)
            {
                sourceCode.textContent = button.dataset.source || '';
            }

            const isResolved = button.dataset.isResolved === 'true' && Boolean(button.dataset.resolvedEntry);
            if (resolution)
            {
                resolution.classList.toggle('is-unresolved', !isResolved);
            }

            if (resolvedName)
            {
                resolvedName.textContent = isResolved ? (button.dataset.resolvedName || 'Name unavailable') : 'Unresolved reference';
            }

            if (resolvedEntry)
            {
                resolvedEntry.textContent = isResolved ? `Entry ${button.dataset.resolvedEntry}` : `Stored ID ${button.dataset.memberId || ''}`;
            }

            if (memberLabelElement)
            {
                memberLabelElement.textContent = button.dataset.memberLabel || 'Member ID';
            }

            if (chanceFieldElement)
            {
                chanceFieldElement.hidden = button.dataset.hasChance !== 'true';
            }

            if (descriptionFieldElement)
            {
                descriptionFieldElement.hidden = button.dataset.hasDescription !== 'true';
            }

            editMemberModal?.show();
        });
    });

    const removeModalElement = document.getElementById('poolRemoveMemberModal');
    const removeModal = removeModalElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(removeModalElement) : null;
    document.querySelectorAll('[data-pool-remove-open]').forEach(button => {
        button.addEventListener('click', () => {
            const source = document.querySelector('[data-pool-remove-source]');
            const member = document.querySelector('[data-pool-remove-member]');
            const membershipPool = document.querySelector('[data-pool-remove-membership-pool]');
            const name = document.querySelector('[data-pool-remove-name]');
            if (source)
            {
                source.value = button.dataset.source || '';
            }

            if (member)
            {
                member.value = button.dataset.memberId || '';
            }

            if (membershipPool)
            {
                membershipPool.value = button.dataset.membershipPoolEntry || '';
            }

            if (name)
            {
                name.textContent = button.dataset.memberName || 'Selected member';
            }

            removeModal?.show();
        });
    });
})();
