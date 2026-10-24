// site user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const sidebarToggle = document.querySelector("[data-sidebar-toggle]");
    sidebarToggle?.addEventListener("click", () => document.body.classList.toggle("sidebar-open"));

    document.querySelectorAll("form[data-confirm]").forEach(form => {
        form.addEventListener("submit", async event => {
            if (form.dataset.studioConfirmApproved === "true")
            {
                delete form.dataset.studioConfirmApproved;
                return;
            }

            event.preventDefault();
            const submitter = event.submitter || null;
            const message = form.getAttribute("data-confirm") || "Continue with this operation?";
            const isDanger = Boolean(submitter?.matches?.(".btn-danger, .btn-outline-danger") || form.querySelector(".btn-danger, .btn-outline-danger"));
            const confirmed = await window.StudioDialog?.confirm({
                title: form.getAttribute("data-confirm-title") || (isDanger ? "Confirm deletion" : "Confirm action"),
                message,
                variant: form.getAttribute("data-confirm-variant") || (isDanger ? "danger" : "warning"),
                icon: form.getAttribute("data-confirm-icon") || (isDanger ? "bi-trash3" : "bi-exclamation-triangle"),
                confirmText: form.getAttribute("data-confirm-action") || (isDanger ? "Delete" : "Continue")
            });

            if (!confirmed)
            {
                return;
            }

            form.dataset.studioConfirmApproved = "true";
            form.requestSubmit(submitter || undefined);
        });
    });

    document.querySelectorAll("[data-copy-target]").forEach(button => {
        button.addEventListener("click", async () => {
            const selector = button.getAttribute("data-copy-target");
            const target = selector ? document.querySelector(selector) : null;
            if (!target)
            {
                return;
            }

            const text = target.textContent || "";
            const original = button.innerHTML;

            try
            {
                await copyText(text);
                button.innerHTML = '<i class="bi bi-check2"></i><span>Copied</span>';
            }
            catch
            {
                button.innerHTML = '<i class="bi bi-exclamation-triangle"></i><span>Copy failed</span>';
            }

            window.setTimeout(() => button.innerHTML = original, 1400);
        });
    });

    document.querySelectorAll(".drop-zone").forEach(zone => {
        const input = zone.querySelector("input[type=file]");
        ["dragenter", "dragover"].forEach(name => zone.addEventListener(name, event => {
            event.preventDefault();
            zone.classList.add("dragging");
        }));
        ["dragleave", "drop"].forEach(name => zone.addEventListener(name, event => {
            event.preventDefault();
            zone.classList.remove("dragging");
        }));
        input?.addEventListener("change", () => {
            const strong = zone.querySelector("strong");
            if (strong && input.files?.length)
            {
                strong.textContent = input.files[0].name;
            }
        });
    });

    initializeThemePreference();
    initializeSidebarNavigationGroups();
    initializeCollapsibleSections();
    initializeWorldCatalog();
    initializeRecordOutline();


    function initializeSidebarNavigationGroups() {
        document.querySelectorAll("[data-nav-group]").forEach(group => {
            const name = group.getAttribute("data-nav-group");
            if (!name)
            {
                return;
            }

            const storageKey = `mangos-studio.sidebar.${name}`;
            const isActive = group.getAttribute("data-nav-active") === "true";

            if (isActive)
            {
                group.open = true;
            }
            else
            {
                try
                {
                    const savedState = window.localStorage.getItem(storageKey);
                    if (savedState !== null)
                    {
                        group.open = savedState === "open";
                    }
                }
                catch
                {
                    // The native details element remains fully functional when storage is unavailable.
                }
            }

            group.addEventListener("toggle", () => {
                try
                {
                    window.localStorage.setItem(storageKey, group.open ? "open" : "closed");
                }
                catch
                {
                    // Ignore private-mode or storage-policy failures.
                }
            });
        });
    }

    function initializeThemePreference() {
        const root = document.documentElement;
        const preference = root.dataset.themePreference || "system";
        if (preference !== "system" || !window.matchMedia)
        {
            return;
        }

        const media = window.matchMedia("(prefers-color-scheme: dark)");
        const apply = event => {
            root.dataset.theme = event.matches ? "dark" : "light";
        };

        if (media.addEventListener)
        {
            media.addEventListener("change", apply);
        }
        else if (media.addListener)
        {
            media.addListener(apply);
        }
    }

    async function copyText(text) {
        if (navigator.clipboard?.writeText && window.isSecureContext)
        {
            await navigator.clipboard.writeText(text);
            return;
        }

        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.setAttribute("readonly", "readonly");
        textarea.style.position = "fixed";
        textarea.style.left = "-9999px";
        textarea.style.top = "0";
        document.body.appendChild(textarea);
        textarea.select();
        textarea.setSelectionRange(0, textarea.value.length);

        try
        {
            if (!document.execCommand("copy"))
            {
                throw new Error("Copy command was rejected.");
            }
        }
        finally
        {
            textarea.remove();
        }
    }

    function initializeCollapsibleSections() {
        document.querySelectorAll("[data-collapse-section]").forEach(button => {
            button.addEventListener("click", () => {
                const section = button.closest(".world-module-section, .record-field-section");
                if (!section)
                {
                    return;
                }

                section.classList.toggle("is-collapsed");
                button.setAttribute("aria-expanded", String(!section.classList.contains("is-collapsed")));
            });
        });
    }

    function initializeWorldCatalog() {
        const catalog = document.querySelector("[data-world-catalog]");
        if (!catalog)
        {
            return;
        }

        const input = catalog.querySelector("[data-world-table-filter]");
        const cards = Array.from(catalog.querySelectorAll("[data-world-table]"));
        const sections = Array.from(catalog.querySelectorAll("[data-world-category]"));
        const empty = catalog.querySelector("[data-world-empty]");

        const filter = () => {
            const query = normalize(input?.value || "");
            let visibleTotal = 0;

            cards.forEach(card => {
                const matches = !query || normalize(card.dataset.searchText || card.textContent || "").includes(query);
                card.hidden = !matches;
                if (matches)
                {
                    visibleTotal++;
                }
            });

            sections.forEach(section => {
                const hasVisibleCards = Array.from(section.querySelectorAll("[data-world-table]")).some(card => !card.hidden);
                section.hidden = !hasVisibleCards;
            });

            if (empty)
            {
                empty.hidden = visibleTotal !== 0;
            }
        };

        input?.addEventListener("input", filter);
        document.addEventListener("keydown", event => {
            if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey)
            {
                return;
            }

            const activeTag = document.activeElement?.tagName?.toLowerCase();
            if (["input", "textarea", "select"].includes(activeTag || ""))
            {
                return;
            }

            event.preventDefault();
            input?.focus();
        });
    }

    function initializeRecordOutline() {
        const filterInput = document.querySelector("[data-field-filter]");
        if (!filterInput)
        {
            return;
        }

        const groups = Array.from(document.querySelectorAll("[data-field-group]"));
        const links = Array.from(document.querySelectorAll("[data-outline-link]"));

        const filter = () => {
            const query = normalize(filterInput.value);

            groups.forEach(group => {
                const cards = Array.from(group.querySelectorAll("[data-field-card]"));
                let visible = 0;

                cards.forEach(card => {
                    const matches = !query || normalize(card.dataset.searchText || card.textContent || "").includes(query);
                    card.hidden = !matches;
                    if (matches)
                    {
                        visible++;
                    }
                });

                group.hidden = visible === 0;
                const counter = group.querySelector("[data-visible-count]");
                if (counter)
                {
                    counter.textContent = `${visible} field${visible === 1 ? "" : "s"}`;
                }

                const id = group.dataset.fieldGroup;
                const outlineLink = links.find(link => link.dataset.outlineLink === id);
                if (outlineLink)
                {
                    outlineLink.hidden = visible === 0;
                }
            });
        };

        filterInput.addEventListener("input", filter);

        links.forEach(link => {
            link.addEventListener("click", () => {
                links.forEach(item => item.classList.remove("active"));
                link.classList.add("active");
            });
        });

        if ("IntersectionObserver" in window)
        {
            const observer = new IntersectionObserver(entries => {
                const visible = entries
                    .filter(entry => entry.isIntersecting)
                    .sort((left, right) => left.boundingClientRect.top - right.boundingClientRect.top)[0];
                if (!visible)
                {
                    return;
                }

                const id = visible.target.getAttribute("data-field-group");
                links.forEach(link => link.classList.toggle("active", link.dataset.outlineLink === id));
            }, { rootMargin: "-180px 0px -65% 0px", threshold: 0.01 });

            groups.forEach(group => observer.observe(group));
        }
    }

    function normalize(value) {
        return String(value || "").trim().toLowerCase();
    }
})();
