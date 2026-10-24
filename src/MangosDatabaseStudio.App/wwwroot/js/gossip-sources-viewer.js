// gossip-sources-viewer user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    const root = document.querySelector('.gossip-sources-viewer');
    if (!root)
    {
        return;
    }

    const addTextModal = document.getElementById('gossipAddTextModal');
    addTextModal?.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        const input = addTextModal.querySelector('[data-gossip-add-entry]');
        if (!input || !trigger?.matches('[data-gossip-new-menu]'))
        {
            return;
        }

        input.value = trigger.getAttribute('data-entry') || '';
        input.removeAttribute('readonly');
    });
})();
