// Keep TLS certificate and risk controls aligned with the selected transport mode.
(() => {
    "use strict";

    const editor = document.querySelector("[data-connection-tls-editor]");
    if (!editor)
    {
        return;
    }

    const modeInput = editor.querySelector("[data-tls-mode]");
    const caGroup = editor.querySelector("[data-tls-ca-group]");
    const caInput = editor.querySelector("[data-tls-ca-input]");
    const riskGroup = editor.querySelector("[data-tls-risk-group]");
    const riskMessage = editor.querySelector("[data-tls-risk-message]");
    const acknowledgment = editor.querySelector("[data-tls-risk-acknowledgment]");

    if (!modeInput || !caGroup || !caInput || !riskGroup || !riskMessage || !acknowledgment)
    {
        return;
    }

    const warningText = {
        VerifyCA: "The server hostname is not verified.",
        Required: "The server certificate is not verified.",
        Preferred: "The connection may use plaintext.",
        Disabled: "No TLS. Use only with trusted local servers.",
        None: "No TLS. Use only with trusted local servers."
    };

    // Disable hidden certificate inputs and keep consent for weaker modes visible.
    function updateTlsControls()
    {
        const mode = modeInput.value;
        const noTls = mode === "Disabled" || mode === "None";
        const verified = mode === "VerifyFull";
        const needsCa = verified || mode === "VerifyCA";

        caGroup.hidden = noTls;
        caInput.disabled = noTls;
        caInput.required = needsCa;

        riskGroup.hidden = verified;
        acknowledgment.disabled = verified;
        riskMessage.textContent = warningText[mode] || "Server identity is not fully verified.";
    }

    modeInput.addEventListener("change", updateTlsControls);
    updateTlsControls();
})();
