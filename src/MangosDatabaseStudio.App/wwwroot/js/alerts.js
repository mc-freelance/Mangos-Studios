// alerts user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const initialized = new WeakSet();

    function initializeAlert(alert) {
        if (!(alert instanceof HTMLElement) || initialized.has(alert))
        {
            return;
        }

        initialized.add(alert);

        const duration = Number.parseInt(alert.dataset.autoDismiss || "", 10);
        if (!Number.isFinite(duration) || duration <= 0)
        {
            return;
        }

        const timer = alert.querySelector(".studio-notice-timer > span");
        let remaining = duration;
        let startedAt = performance.now();
        let timeoutId = 0;
        let paused = false;
        let closed = false;

        alert.style.setProperty("--studio-alert-duration", `${duration}ms`);
        window.requestAnimationFrame(() => alert.classList.add("is-counting-down"));

        const close = () => {
            if (closed || !alert.isConnected)
            {
                return;
            }

            closed = true;
            window.clearTimeout(timeoutId);

            const instance = window.bootstrap?.Alert?.getOrCreateInstance(alert);
            if (instance)
            {
                instance.close();
            }
            else
            {
                alert.remove();
            }
        };

        const schedule = () => {
            window.clearTimeout(timeoutId);
            if (closed || paused || remaining <= 0)
            {
                return;
            }

            startedAt = performance.now();
            timeoutId = window.setTimeout(close, remaining);
        };

        const pause = () => {
            if (closed || paused)
            {
                return;
            }

            remaining = Math.max(0, remaining - (performance.now() - startedAt));
            paused = true;
            window.clearTimeout(timeoutId);
            alert.classList.add("is-timer-paused");
        };

        const resume = () => {
            if (closed || !paused)
            {
                return;
            }

            paused = false;
            alert.classList.remove("is-timer-paused");
            if (remaining <= 0)
            {
                close();
                return;
            }

            schedule();
        };

        alert.addEventListener("mouseenter", pause);
        alert.addEventListener("mouseleave", () => {
            if (!alert.matches(":focus-within"))
            {
                resume();
            }
        });
        alert.addEventListener("focusin", pause);
        alert.addEventListener("focusout", event => {
            const next = event.relatedTarget;
            if (next instanceof Node && alert.contains(next))
            {
                return;
            }

            if (!alert.matches(":hover"))
            {
                resume();
            }
        });
        alert.addEventListener("close.bs.alert", () => {
            closed = true;
            window.clearTimeout(timeoutId);
        });

        if (timer)
        {
            timer.setAttribute("data-alert-timer-active", "true");
        }

        schedule();
    }

    function initializeAlerts(root = document) {
        if (root instanceof Element && root.matches("[data-studio-alert]"))
        {
            initializeAlert(root);
        }

        root.querySelectorAll?.("[data-studio-alert]").forEach(initializeAlert);
    }

    initializeAlerts();

    const observer = new MutationObserver(records => {
        records.forEach(record => {
            record.addedNodes.forEach(node => {
                if (node instanceof Element)
                {
                    initializeAlerts(node);
                }
            });
        });
    });

    if (document.body)
    {
        observer.observe(document.body, { childList: true, subtree: true });
    }

    window.StudioAlerts = Object.freeze({ initialize: initializeAlerts });
})();
