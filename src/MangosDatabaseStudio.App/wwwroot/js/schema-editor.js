// schema-editor user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const columnForm = document.querySelector("[data-schema-column-form]");
    if (columnForm)
    {
        const defaultMode = columnForm.querySelector('[name="DefaultMode"]');
        const defaultValue = columnForm.querySelector('[name="DefaultValue"]');
        const position = columnForm.querySelector('[name="Position"]');
        const afterColumn = columnForm.querySelector('[name="AfterColumn"]');

        const sync = () => {
            if (defaultMode && defaultValue)
            {
                const enabled = defaultMode.value === "value" || defaultMode.value === "bit-literal";
                defaultValue.disabled = !enabled;
                defaultValue.closest(".form-group")?.classList.toggle("is-muted", !enabled);
            }

            if (position && afterColumn)
            {
                const enabled = position.value === "AFTER";
                afterColumn.disabled = !enabled;
                afterColumn.closest(".form-group")?.classList.toggle("is-muted", !enabled);
            }
        };

        defaultMode?.addEventListener("change", sync);
        position?.addEventListener("change", sync);
        sync();
    }

    const copyButton = document.querySelector("[data-copy-schema-sql]");
    const sqlCode = document.querySelector("[data-schema-sql]");
    if (copyButton && sqlCode)
    {
        copyButton.addEventListener("click", async () => {
            const text = sqlCode.textContent || "";
            if (!text.trim())
            {
                return;
            }

            const original = copyButton.innerHTML;
            try
            {
                await navigator.clipboard.writeText(text);
                copyButton.innerHTML = '<i class="bi bi-check2"></i> Copied';
            }
            catch
            {
                const textarea = document.createElement("textarea");
                textarea.value = text;
                textarea.setAttribute("readonly", "readonly");
                textarea.style.position = "fixed";
                textarea.style.opacity = "0";
                document.body.appendChild(textarea);
                textarea.select();
                document.execCommand("copy");
                textarea.remove();
                copyButton.innerHTML = '<i class="bi bi-check2"></i> Copied';
            }

            window.setTimeout(() => { copyButton.innerHTML = original; }, 1400);
        });
    }
})();
