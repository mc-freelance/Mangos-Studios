// studio-dialog user-interface behavior; keep DOM updates scoped to the active view.
(() => {
    "use strict";

    const variantMap = {
        info: {
            icon: "bi-info-circle-fill",
            confirmClass: "btn-primary",
            dialogClass: "studio-dialog-info"
        },
        success: {
            icon: "bi-check-circle-fill",
            confirmClass: "btn-success",
            dialogClass: "studio-dialog-success"
        },
        warning: {
            icon: "bi-exclamation-triangle-fill",
            confirmClass: "btn-primary",
            dialogClass: "studio-dialog-warning"
        },
        danger: {
            icon: "bi-exclamation-octagon-fill",
            confirmClass: "btn-danger",
            dialogClass: "studio-dialog-danger"
        }
    };

    let activeDialog = null;

    const normalizeOptions = (messageOrOptions, options = {}) => {
        if (typeof messageOrOptions === "object" && messageOrOptions !== null)
        {
            return { ...messageOrOptions };
        }

        return {
            ...options,
            message: String(messageOrOptions ?? "")
        };
    };

    const ensureDialog = () => {
        let element = document.getElementById("studioApplicationDialog");
        if (element)
        {
            return element;
        }

        element = document.createElement("div");
        element.className = "modal fade studio-application-dialog";
        element.id = "studioApplicationDialog";
        element.tabIndex = -1;
        element.setAttribute("aria-hidden", "true");
        element.innerHTML = `
            <div class="modal-dialog modal-md modal-dialog-centered">
                <div class="modal-content">
                    <div class="modal-header studio-dialog-header">
                        <div class="studio-dialog-heading">
                            <span class="studio-dialog-icon" data-studio-dialog-icon aria-hidden="true">
                                <i class="bi bi-info-circle"></i>
                            </span>
                            <div>
                                <span class="eyebrow" data-studio-dialog-eyebrow>Database Studio</span>
                                <h2 class="modal-title" data-studio-dialog-title>Notice</h2>
                            </div>
                        </div>
                        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                    </div>
                    <div class="modal-body">
                        <p class="studio-dialog-message" data-studio-dialog-message></p>
                        <div class="studio-dialog-detail" data-studio-dialog-detail hidden></div>
                        <div class="studio-dialog-prompt" data-studio-dialog-prompt hidden>
                            <label class="form-label" data-studio-dialog-prompt-label for="studioApplicationDialogInput">Value</label>
                            <input class="form-control" id="studioApplicationDialogInput" data-studio-dialog-input autocomplete="off" />
                        </div>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-outline-secondary" data-studio-dialog-cancel data-bs-dismiss="modal">Cancel</button>
                        <button type="button" class="btn btn-primary" data-studio-dialog-confirm>Continue</button>
                    </div>
                </div>
            </div>`;

        document.body.appendChild(element);
        return element;
    };

    const show = (mode, rawOptions) => {
        const options = normalizeOptions(rawOptions);
        const element = ensureDialog();
        const bootstrapModal = window.bootstrap?.Modal?.getOrCreateInstance(element, {
            backdrop: "static",
            keyboard: true
        });

        if (!bootstrapModal)
        {
            return Promise.resolve(mode === "alert" ? true : mode === "prompt" ? null : false);
        }

        if (activeDialog?.resolve)
        {
            activeDialog.resolve(activeDialog.mode === "alert" ? true : activeDialog.mode === "prompt" ? null : false);
            activeDialog = null;
        }

        const variantName = Object.hasOwn(variantMap, options.variant) ? options.variant : "info";
        const variant = variantMap[variantName];
        const title = options.title || (mode === "alert" ? "Notice" : mode === "prompt" ? "Enter a value" : "Confirm action");
        const eyebrow = options.eyebrow || "Database Studio";
        const message = options.message || "";
        const confirmText = options.confirmText || (mode === "alert" ? "OK" : mode === "prompt" ? "Continue" : "Continue");
        const cancelText = options.cancelText || "Cancel";
        const iconClass = options.icon || variant.icon;

        element.classList.remove(...Object.values(variantMap).map(item => item.dialogClass));
        element.classList.add(variant.dialogClass);

        const titleElement = element.querySelector("[data-studio-dialog-title]");
        const eyebrowElement = element.querySelector("[data-studio-dialog-eyebrow]");
        const messageElement = element.querySelector("[data-studio-dialog-message]");
        const detailElement = element.querySelector("[data-studio-dialog-detail]");
        const iconElement = element.querySelector("[data-studio-dialog-icon] i");
        const confirmButton = element.querySelector("[data-studio-dialog-confirm]");
        const cancelButton = element.querySelector("[data-studio-dialog-cancel]");
        const promptBlock = element.querySelector("[data-studio-dialog-prompt]");
        const promptLabel = element.querySelector("[data-studio-dialog-prompt-label]");
        const promptInput = element.querySelector("[data-studio-dialog-input]");

        if (titleElement)
        {
            titleElement.textContent = title;
        }

        if (eyebrowElement)
        {
            eyebrowElement.textContent = eyebrow;
        }

        if (messageElement)
        {
            messageElement.textContent = message;
        }

        if (iconElement)
        {
            iconElement.className = `bi ${iconClass}`;
        }

        if (detailElement)
        {
            const detail = options.detail || "";
            detailElement.textContent = detail;
            detailElement.hidden = !detail;
        }

        if (confirmButton)
        {
            confirmButton.className = `btn ${variant.confirmClass}`;
            confirmButton.textContent = confirmText;
        }

        if (cancelButton)
        {
            cancelButton.textContent = cancelText;
            cancelButton.hidden = mode === "alert";
        }

        if (promptBlock && promptInput && promptLabel)
        {
            const isPrompt = mode === "prompt";
            promptBlock.hidden = !isPrompt;
            promptLabel.textContent = options.inputLabel || "Value";
            promptInput.value = isPrompt ? String(options.defaultValue ?? "") : "";
            promptInput.placeholder = options.placeholder || "";
        }

        return new Promise(resolve => {
            let settled = false;
            const finish = value => {
                if (settled)
                {
                    return;
                }

                settled = true;
                cleanup();
                activeDialog = null;
                resolve(value);
            };

            const onConfirm = () => {
                const value = mode === "prompt" ? promptInput?.value ?? "" : true;
                finish(value);
                bootstrapModal.hide();
            };

            const onHidden = () => {
                finish(mode === "alert" ? true : mode === "prompt" ? null : false);
            };

            const onKeydown = event => {
                if (event.key !== "Enter" || mode !== "prompt" || document.activeElement !== promptInput)
                {
                    return;
                }

                event.preventDefault();
                onConfirm();
            };

            const cleanup = () => {
                confirmButton?.removeEventListener("click", onConfirm);
                element.removeEventListener("hidden.bs.modal", onHidden);
                promptInput?.removeEventListener("keydown", onKeydown);
            };

            confirmButton?.addEventListener("click", onConfirm);
            element.addEventListener("hidden.bs.modal", onHidden, { once: true });
            promptInput?.addEventListener("keydown", onKeydown);
            activeDialog = { resolve: finish, mode };

            bootstrapModal.show();

            element.addEventListener("shown.bs.modal", () => {
                if (mode === "prompt")
                {
                    promptInput?.focus();
                }
                else
                {
                    confirmButton?.focus();
                }
            }, { once: true });
        });
    };

    window.StudioDialog = Object.freeze({
        alert(messageOrOptions, options) {
            return show("alert", normalizeOptions(messageOrOptions, options));
        },
        confirm(messageOrOptions, options) {
            return show("confirm", normalizeOptions(messageOrOptions, options));
        },
        prompt(messageOrOptions, options) {
            return show("prompt", normalizeOptions(messageOrOptions, options));
        }
    });
})();
