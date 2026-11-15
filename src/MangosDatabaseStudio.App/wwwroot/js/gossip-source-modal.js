// gossip source modal behavior; kept external so CSP blocks injected inline scripts.
(() => {
        const form = document.querySelector('[data-gossip-add-npc-source-form]');
        const picker = form?.querySelector('[data-world-lookup-picker]');
        const menuId = form?.querySelector('[data-gossip-source-menu-id]');
        picker?.addEventListener('worldlookupchange', event => {
            const existingMenuId = Number(event.detail?.item?.menuId || 0);
            if (menuId && existingMenuId > 0 && !String(menuId.value || '').trim())
            {
                menuId.value = String(existingMenuId);
            }
        });
    })();
