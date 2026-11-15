// theme bootstrap behavior; kept external so CSP blocks injected inline scripts.
(() => {
    const root = document.documentElement;
    const preference = root.dataset.themePreference || "system";
    const dark = preference === "dark" || (preference === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
    root.dataset.theme = dark ? "dark" : "light";
})();
