// desktop-scaling user-interface behavior; keep DOM updates scoped to the active view.
(function (globalObject, factory) {
    "use strict";

    const api = factory();
    if (typeof module !== "undefined" && module.exports)
    {
        module.exports = api;
    }
    else
    {
        globalObject.MangosDesktopScaling = api;
        api.applyInitialScale(document, window);
        if (document.readyState === "loading")
        {
            document.addEventListener("DOMContentLoaded", () => api.initialize(document, window));
        }
        else
        {
            api.initialize(document, window);
        }
    }
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const MINIMUM_ZOOM = 50;
    const MAXIMUM_ZOOM = 125;
    const DEFAULT_ZOOM = 100;
    const ZOOM_STEP = 5;
    const DESIGN_WIDTH = 1180;
    const DESIGN_HEIGHT = 720;

    function clampZoom(value) {
        const number = Number(value);
        if (!Number.isFinite(number))
        {
            return DEFAULT_ZOOM;
        }

        const stepped = Math.round(number / ZOOM_STEP) * ZOOM_STEP;
        return Math.min(MAXIMUM_ZOOM, Math.max(MINIMUM_ZOOM, stepped));
    }

    function parseBoolean(value, fallback) {
        if (typeof value === "boolean")
        {
            return value;
        }

        if (typeof value !== "string")
        {
            return fallback;
        }

        if (value.toLowerCase() === "true")
        {
            return true;
        }

        if (value.toLowerCase() === "false")
        {
            return false;
        }

        return fallback;
    }

    function readMetrics(windowObject) {
        const screenObject = windowObject.screen || {};
        return {
            viewportWidth: positiveNumber(windowObject.innerWidth),
            viewportHeight: positiveNumber(windowObject.innerHeight),
            screenWidth: positiveNumber(screenObject.availWidth || screenObject.width),
            screenHeight: positiveNumber(screenObject.availHeight || screenObject.height)
        };
    }

    function calculateAutomaticZoom(metrics) {
        const ratios = [1];
        addRatio(ratios, metrics.viewportWidth, DESIGN_WIDTH);
        addRatio(ratios, metrics.viewportHeight, DESIGN_HEIGHT);
        addRatio(ratios, metrics.screenWidth, DESIGN_WIDTH);
        addRatio(ratios, metrics.screenHeight, DESIGN_HEIGHT);

        const fitPercent = Math.min.apply(null, ratios) * 100;
        const steppedDown = Math.floor(fitPercent / ZOOM_STEP) * ZOOM_STEP;
        return clampZoom(steppedDown);
    }

    function resolveZoom(preferredZoom, automatic, metrics) {
        const preferred = clampZoom(preferredZoom);
        if (!automatic)
        {
            return preferred;
        }

        return Math.min(preferred, calculateAutomaticZoom(metrics));
    }

    function applyZoom(documentObject, zoomPercent) {
        const root = documentObject.documentElement;
        const body = documentObject.body;
        const scale = clampZoom(zoomPercent) / 100;
        root.dataset.desktopEffectiveZoom = String(Math.round(scale * 100));

        if (root.style && "zoom" in root.style)
        {
            root.style.zoom = String(scale);
            if (body)
            {
                body.style.transform = "";
                body.style.transformOrigin = "";
                body.style.width = "";
                body.style.minHeight = "";
            }

            return;
        }

        if (!body)
        {
            return;
        }

        root.style.zoom = "";
        body.style.transform = `scale(${scale})`;
        body.style.transformOrigin = "top left";
        body.style.width = `${100 / scale}%`;
        body.style.minHeight = `${100 / scale}vh`;
    }

    function applyInitialScale(documentObject, windowObject) {
        const root = documentObject.documentElement;
        if (!isDesktopHost(root))
        {
            return null;
        }

        const preferred = clampZoom(root.dataset.desktopZoom);
        const automatic = parseBoolean(root.dataset.desktopAutoScale, true);
        const effective = resolveZoom(preferred, automatic, readMetrics(windowObject));
        applyZoom(documentObject, effective);
        return effective;
    }

    function initialize(documentObject, windowObject) {
        const root = documentObject.documentElement;
        if (!isDesktopHost(root) || root.dataset.desktopScalingInitialized === "true")
        {
            return null;
        }

        root.dataset.desktopScalingInitialized = "true";

        const controls = documentObject.querySelector("[data-desktop-zoom-controls]");
        const state = {
            preferred: clampZoom(root.dataset.desktopZoom),
            automatic: parseBoolean(root.dataset.desktopAutoScale, true),
            effective: DEFAULT_ZOOM,
            saveTimer: null
        };

        const label = controls?.querySelector("[data-desktop-zoom-label]");
        const autoButton = controls?.querySelector("[data-desktop-zoom-auto]");
        const status = controls?.querySelector("[data-desktop-zoom-status]");

        const apply = () => {
            const metrics = readMetrics(windowObject);
            state.effective = resolveZoom(state.preferred, state.automatic, metrics);
            root.dataset.desktopZoom = String(state.preferred);
            root.dataset.desktopAutoScale = String(state.automatic).toLowerCase();
            applyZoom(documentObject, state.effective);
            updateControls(label, autoButton, status, state, metrics);
        };

        const save = () => {
            if (!controls)
            {
                return;
            }

            if (state.saveTimer)
            {
                windowObject.clearTimeout(state.saveTimer);
            }

            state.saveTimer = windowObject.setTimeout(async () => {
                state.saveTimer = null;
                await persistPreference(controls, state, status, windowObject);
            }, 250);
        };

        const changeManual = delta => {
            state.preferred = clampZoom(state.effective + delta);
            state.automatic = false;
            apply();
            save();
        };

        controls?.querySelector("[data-desktop-zoom-out]")?.addEventListener("click", () => changeManual(-ZOOM_STEP));
        controls?.querySelector("[data-desktop-zoom-in]")?.addEventListener("click", () => changeManual(ZOOM_STEP));
        controls?.querySelector("[data-desktop-zoom-reset]")?.addEventListener("click", () => {
            state.preferred = 100;
            state.automatic = false;
            apply();
            save();
        });
        autoButton?.addEventListener("click", () => {
            if (state.automatic)
            {
                state.preferred = state.effective;
                state.automatic = false;
            }
            else
            {
                state.automatic = true;
            }

            apply();
            save();
        });

        documentObject.addEventListener("keydown", event => {
            if (!event.ctrlKey || event.altKey || event.metaKey)
            {
                return;
            }

            if (event.key === "+" || event.key === "=")
            {
                event.preventDefault();
                changeManual(ZOOM_STEP);
            }
            else if (event.key === "-" || event.key === "_")
            {
                event.preventDefault();
                changeManual(-ZOOM_STEP);
            }
            else if (event.key === "0")
            {
                event.preventDefault();
                state.preferred = 100;
                state.automatic = false;
                apply();
                save();
            }
        });

        let resizeTimer = null;
        windowObject.addEventListener("resize", () => {
            if (!state.automatic)
            {
                return;
            }

            if (resizeTimer)
            {
                windowObject.clearTimeout(resizeTimer);
            }

            resizeTimer = windowObject.setTimeout(() => {
                resizeTimer = null;
                apply();
            }, 120);
        });

        apply();
        return state;
    }

    async function persistPreference(controls, state, status, windowObject) {
        const endpoint = controls.getAttribute("action") || controls.dataset.saveUrl;
        const token = controls.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
        if (!endpoint || typeof windowObject.fetch !== "function")
        {
            return;
        }

        setStatus(status, "Saving…", "saving");
        const body = new windowObject.URLSearchParams();
        body.set("ZoomPercent", String(state.preferred));
        body.set("AutoScale", String(state.automatic));
        body.set("__RequestVerificationToken", token);

        try
        {
            const response = await windowObject.fetch(endpoint, {
                method: "POST",
                credentials: "same-origin",
                headers: { "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8" },
                body: body.toString()
            });

            if (!response.ok)
            {
                throw new Error(`Desktop zoom save failed with status ${response.status}.`);
            }

            const result = await response.json();
            state.preferred = clampZoom(result.zoomPercent);
            state.automatic = parseBoolean(String(result.autoScale), state.automatic);
            setStatus(status, "Saved", "saved");
            windowObject.setTimeout(() => setStatus(status, "", ""), 1200);
        }
        catch (error)
        {
            setStatus(status, "Not saved", "error");
            if (windowObject.console?.error)
            {
                windowObject.console.error(error);
            }
        }
    }

    function updateControls(label, autoButton, status, state, metrics) {
        if (label)
        {
            label.textContent = `${state.effective}%`;
            label.title = state.automatic && state.effective < state.preferred
                ? `Preferred ${state.preferred}%; fitted to ${state.effective}% for ${metrics.viewportWidth}×${metrics.viewportHeight}`
                : `Desktop zoom ${state.effective}%`;
        }

        if (autoButton)
        {
            autoButton.classList.toggle("active", state.automatic);
            autoButton.setAttribute("aria-pressed", String(state.automatic));
            autoButton.title = state.automatic
                ? "Automatic screen fitting is enabled"
                : "Enable automatic screen fitting";
        }

        if (status && !status.dataset.state)
        {
            status.textContent = "";
        }
    }

    function setStatus(element, text, state) {
        if (!element)
        {
            return;
        }

        element.textContent = text;
        element.dataset.state = state;
    }

    function isDesktopHost(root) {
        return parseBoolean(root.dataset.desktopHost, false);
    }

    function positiveNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) && number > 0 ? number : 0;
    }

    function addRatio(ratios, value, baseline) {
        const number = positiveNumber(value);
        if (number > 0)
        {
            ratios.push(number / baseline);
        }
    }

    return {
        MINIMUM_ZOOM,
        MAXIMUM_ZOOM,
        DEFAULT_ZOOM,
        ZOOM_STEP,
        clampZoom,
        parseBoolean,
        readMetrics,
        calculateAutomaticZoom,
        resolveZoom,
        applyZoom,
        applyInitialScale,
        initialize
    };
});
