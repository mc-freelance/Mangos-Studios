// quest relation modal behavior; kept external so CSP blocks injected inline scripts.
(() => {
    document.querySelectorAll('[data-quest-edit-relation]').forEach(button => {
        button.addEventListener('click', () => {
            const modal = document.getElementById('questEditRelationModal');
            if (!modal)
            {
                return;
            }

            const kind = button.dataset.kind || '';
            const entry = button.dataset.entry || '';
            const name = button.dataset.name || 'Quest relationship';
            modal.querySelector('[data-quest-edit-original-kind]').value = kind;
            modal.querySelector('[data-quest-edit-original-entry]').value = entry;
            modal.querySelector('[data-quest-edit-kind]').value = kind;
            modal.querySelector('[data-quest-edit-entity-entry]').value = entry;
            modal.querySelector('[data-quest-edit-name]').textContent = name;
            modal.querySelector('[data-quest-edit-entry]').textContent = 'Entry ' + entry;
        });
    });

    document.querySelectorAll('[data-quest-remove-relation]').forEach(button => {
        button.addEventListener('click', () => {
            const modal = document.getElementById('questRemoveRelationModal');
            if (!modal)
            {
                return;
            }

            modal.querySelector('[data-quest-remove-kind]').value = button.dataset.kind || '';
            modal.querySelector('[data-quest-remove-entry]').value = button.dataset.entry || '';
            modal.querySelector('[data-quest-remove-name]').textContent = button.dataset.name || 'Selected source';
        });
    });
})();
