// operation-feedback user-interface behavior; keep DOM updates scoped to the active view.
(function (root, factory) {
    "use strict";

    const api = factory();

    if (typeof module === "object" && module.exports)
    {
        module.exports = api;
        return;
    }

    root.MangosOperationFeedback = api;
    const start = () => api.initialize(document, window);
    if (document.readyState === "loading")
    {
        document.addEventListener("DOMContentLoaded", start, { once: true });
    }
    else
    {
        start();
    }
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const operationHeaderName = "X-MDS-Operation-Id";
    const operationQueryName = "mdsOperationId";
    let elapsedTimer = null;
    let downloadTimer = null;
    let progressTimer = null;
    let activeProgressId = "";

    // Backend work is never delayed for presentation. These values only pace
    // progress text that has already been reported so a human can read it.
    const progressPollIntervalMs = 200;
    const minimumStageDisplayMs = 950;
    const completionCatchupStageMs = 450;
    const completionFinalStageMs = 300;
    const maxCompletionCatchupMs = 1800;
    const maxQueuedStages = 3;
    let progressDisplayTimer = null;
    let displayedProgressStage = "";
    let displayedProgressAt = 0;
    let pendingProgressSnapshots = [];
    let downloadFinishing = false;

    function initialize(doc, win) {
        if (!doc || !win)
        {
            return;
        }

        const forms = Array.from(doc.querySelectorAll("form[data-processing-form]"));
        forms.forEach(form => {
            if (form.dataset.processingBound === "true")
            {
                return;
            }

            form.dataset.processingBound = "true";

            form.addEventListener("submit", event => {
                if (form.dataset.processingNativeSubmit === "true")
                {
                    delete form.dataset.processingNativeSubmit;
                    return;
                }

                if (event.defaultPrevented)
                {
                    return;
                }

                if (form.dataset.processing === "true")
                {
                    event.preventDefault();
                    return;
                }

                if (typeof form.checkValidity === "function" && !form.checkValidity())
                {
                    return;
                }

                event.preventDefault();
                const submitter = event.submitter || null;
                const operationId = createOperationId(win);
                begin(form, doc, win);
                startProgressPolling(operationId, doc, win);

                if (form.dataset.processingDownloadToken)
                {
                    prepareNativeOperationUrl(form, operationId, win);
                    waitForDownload(form, doc, win);
                    submitAfterOverlayPaint(form, submitter, win);
                    return;
                }

                // HTML-returning operations start immediately. Keeping the current
                // document mounted with fetch lets the popup display genuine server
                // progress until the completed response is ready to render.
                submitWithLiveProgress(form, submitter, operationId, doc, win)
                    .catch(error => handleLiveRequestFailure(error, doc, win));
            });
        });

        win.addEventListener("pageshow", () => reset(doc));
    }

    function submitAfterOverlayPaint(form, submitter, win) {
        const requestFrame = typeof win.requestAnimationFrame === "function"
            ? callback => win.requestAnimationFrame(callback)
            : callback => Promise.resolve().then(callback);

        // Native download submissions need the current page to stay mounted. Two
        // render frames make the overlay visible before navigation/download handling
        // starts, without imposing any fixed-duration wait.
        requestFrame(() => {
            requestFrame(() => submitNative(form, submitter, win));
        });
    }

    async function submitWithLiveProgress(form, submitter, operationId, doc, win) {
        const request = buildFetchRequest(form, submitter, operationId, win);
        const responsePromise = win.fetch(request.url, request.options);

        // Do not wait for a render frame before starting the real operation. The
        // browser can paint the already-visible overlay while the request is in flight.
        const response = await responsePromise;
        const contentType = String(response.headers.get("content-type") || "").toLowerCase();
        const text = await response.text();

        if (!contentType.includes("text/html"))
        {
            throw new Error(text.trim() || "The operation returned an unexpected response.");
        }

        stopProgressPolling(win);
        await finishProgressPresentation(
            doc,
            win,
            "Rendering results",
            "The server operation completed; replacing the page with the returned result.");
        replaceDocument(text, response.url || request.url, doc, win);
    }

    function buildFetchRequest(form, submitter, operationId, win) {
        const method = String(form.method || "get").toUpperCase();
        const url = new URL(form.action || win.location.href, win.location.href);
        const formData = new win.FormData(form);
        appendSubmitterValue(formData, submitter);
        const headers = new win.Headers();
        headers.set(operationHeaderName, operationId);
        headers.set("X-Requested-With", "MDS-LiveProgress");
        headers.set("Accept", "text/html");

        const options = {
            method,
            credentials: "same-origin",
            redirect: "follow",
            headers
        };

        if (method === "GET" || method === "HEAD")
        {
            for (const [name, value] of formData.entries())
            {
                if (typeof value === "string")
                {
                    url.searchParams.append(name, value);
                }
            }
        }
        else
        {
            options.body = formData;
        }

        return { url: url.toString(), options };
    }

    function appendSubmitterValue(formData, submitter) {
        const name = submitter?.getAttribute?.("name") || "";
        if (!name)
        {
            return;
        }

        formData.append(name, submitter?.value || "");
    }

    function prepareNativeOperationUrl(form, operationId, win) {
        const actionAttribute = form.getAttribute("action");
        if (form.dataset.processingOriginalAction === undefined)
        {
            form.dataset.processingOriginalAction = actionAttribute === null ? "" : actionAttribute;
            form.dataset.processingHadAction = actionAttribute === null ? "false" : "true";
        }

        const url = new URL(form.action || win.location.href, win.location.href);
        url.searchParams.set(operationQueryName, operationId);
        form.action = url.toString();
    }

    function restoreNativeOperationUrl(form) {
        if (form.dataset.processingOriginalAction === undefined)
        {
            return;
        }

        if (form.dataset.processingHadAction === "true")
        {
            form.setAttribute("action", form.dataset.processingOriginalAction);
        }
        else
        {
            form.removeAttribute("action");
        }

        delete form.dataset.processingOriginalAction;
        delete form.dataset.processingHadAction;
    }

    function submitNative(form, submitter, win) {
        if (!form || form.dataset.processing !== "true")
        {
            return;
        }

        let submitterField = null;
        const submitterName = submitter?.getAttribute?.("name") || "";
        if (submitterName)
        {
            submitterField = form.ownerDocument.createElement("input");
            submitterField.type = "hidden";
            submitterField.name = submitterName;
            submitterField.value = submitter?.value || "";
            submitterField.dataset.processingSubmitterValue = "true";
            form.appendChild(submitterField);
        }

        form.dataset.processingNativeSubmit = "true";

        try
        {
            const nativeSubmit = win?.HTMLFormElement?.prototype?.submit;
            if (typeof nativeSubmit === "function")
            {
                nativeSubmit.call(form);
            }
            else if (typeof form.submit === "function")
            {
                form.submit();
            }
        }
        catch (error)
        {
            delete form.dataset.processingNativeSubmit;
            submitterField?.remove();
            restoreNativeOperationUrl(form);
            reset(form.ownerDocument);
            throw error;
        }

        submitterField?.remove();
        restoreNativeOperationUrl(form);
        delete form.dataset.processingNativeSubmit;
    }

    function begin(form, doc, win) {
        clearProgressPresentation(win);
        downloadFinishing = false;
        form.dataset.processing = "true";
        form.setAttribute("aria-busy", "true");

        const label = form.dataset.processingLabel || "Processing...";
        const submitters = Array.from(form.querySelectorAll('button[type="submit"], input[type="submit"]'));
        submitters.forEach(button => setButtonProcessing(button, label));

        const overlay = doc.querySelector("[data-processing-overlay]");
        if (overlay)
        {
            const title = overlay.querySelector("[data-processing-overlay-title]");
            const message = overlay.querySelector("[data-processing-overlay-message]");
            const elapsed = overlay.querySelector("[data-processing-overlay-elapsed]");
            const stage = overlay.querySelector("[data-processing-overlay-stage]");
            const detail = overlay.querySelector("[data-processing-overlay-detail]");
            const counter = overlay.querySelector("[data-processing-overlay-counter]");

            if (title)
            {
                title.textContent = form.dataset.processingTitle || "Processing request";
            }

            if (message)
            {
                message.textContent = form.dataset.processingMessage || "Please wait while the operation completes.";
            }

            if (elapsed)
            {
                elapsed.textContent = "Starting…";
            }

            if (stage)
            {
                stage.textContent = "Starting operation";
            }

            if (detail)
            {
                detail.textContent = "The request has started. Waiting for the first backend progress update.";
            }

            if (counter)
            {
                counter.textContent = "";
            }

            setProgressBar(overlay, null, null);

            overlay.hidden = false;
            overlay.dataset.active = "true";
        }

        doc.body?.classList.add("operation-processing");
        startElapsedTimer(overlay, win);
    }

    function startProgressPolling(operationId, doc, win) {
        stopProgressPolling(win);
        activeProgressId = operationId;
        const poll = async () => {
            if (!activeProgressId || activeProgressId !== operationId)
            {
                return;
            }

            try
            {
                const response = await win.fetch("/operation-progress/status?id=" + encodeURIComponent(operationId), {
                    method: "GET",
                    credentials: "same-origin",
                    cache: "no-store",
                    headers: { "Accept": "application/json" }
                });
                if (!response.ok)
                {
                    return;
                }

                const snapshot = await response.json();
                if (activeProgressId !== operationId)
                {
                    return;
                }

                queueProgressSnapshot(snapshot, doc, win);
            }
            catch
            {
                // Progress is supplemental. The primary operation request remains the
                // source of truth and should never fail because status polling failed.
            }
        };

        void poll();
        progressTimer = win.setInterval(() => void poll(), progressPollIntervalMs);
    }

    function stopProgressPolling(win) {
        activeProgressId = "";
        if (progressTimer !== null && win)
        {
            win.clearInterval(progressTimer);
            progressTimer = null;
        }
    }


    function queueProgressSnapshot(snapshot, doc, win) {
        if (!snapshot || !doc)
        {
            return;
        }

        const stage = String(snapshot.stage || "Processing");

        if (!displayedProgressStage)
        {
            displayProgressSnapshotNow(snapshot, doc);
            return;
        }

        // Counters and detail text for the current backend stage can stay live.
        // Only transitions to a new stage receive the readability hold.
        if (stage === displayedProgressStage)
        {
            applyProgressSnapshot(snapshot, doc);
            return;
        }

        const last = pendingProgressSnapshots[pendingProgressSnapshots.length - 1];
        if (last && String(last.stage || "Processing") === stage)
        {
            pendingProgressSnapshots[pendingProgressSnapshots.length - 1] = snapshot;
        }
        else
        {
            pendingProgressSnapshots.push(snapshot);
        }

        // If the backend races through many stages, keep the next stage plus the
        // newest milestones instead of making the UI trail the server for seconds.
        if (pendingProgressSnapshots.length > maxQueuedStages)
        {
            const first = pendingProgressSnapshots[0];
            const newest = pendingProgressSnapshots.slice(-(maxQueuedStages - 1));
            pendingProgressSnapshots = [first, ...newest];
        }

        scheduleNextProgressStage(doc, win);
    }

    function displayProgressSnapshotNow(snapshot, doc) {
        applyProgressSnapshot(snapshot, doc);
        displayedProgressStage = String(snapshot?.stage || "Processing");
        displayedProgressAt = Date.now();
    }

    function scheduleNextProgressStage(doc, win) {
        if (progressDisplayTimer !== null || pendingProgressSnapshots.length === 0)
        {
            return;
        }

        const elapsed = displayedProgressAt > 0 ? Date.now() - displayedProgressAt : minimumStageDisplayMs;
        const waitMs = Math.max(0, minimumStageDisplayMs - elapsed);
        progressDisplayTimer = win.setTimeout(() => {
            progressDisplayTimer = null;
            const next = pendingProgressSnapshots.shift();
            if (next)
            {
                displayProgressSnapshotNow(next, doc);
            }

            scheduleNextProgressStage(doc, win);
        }, waitMs);
    }

    async function finishProgressPresentation(doc, win, finalStage, finalDetail) {
        if (progressDisplayTimer !== null)
        {
            win.clearTimeout(progressDisplayTimer);
            progressDisplayTimer = null;
        }

        const deadline = Date.now() + maxCompletionCatchupMs;
        if (displayedProgressAt > 0)
        {
            const remaining = minimumStageDisplayMs - (Date.now() - displayedProgressAt);
            if (remaining > 0)
            {
                await presentationDelay(win, Math.min(remaining, Math.max(0, deadline - Date.now())));
            }
        }

        // Show at most a couple of queued milestones after an extremely fast
        // operation. This improves readability without turning quick work into
        // a long fake loading sequence.
        const catchup = [];
        if (pendingProgressSnapshots.length > 0)
        {
            catchup.push(pendingProgressSnapshots[0]);
            const last = pendingProgressSnapshots[pendingProgressSnapshots.length - 1];
            if (last && String(last.stage || "") !== String(catchup[0]?.stage || ""))
            {
                catchup.push(last);
            }
        }

        pendingProgressSnapshots = [];

        for (const snapshot of catchup)
        {
            const remainingBudget = deadline - Date.now();
            if (remainingBudget <= 0)
            {
                break;
            }

            displayProgressSnapshotNow(snapshot, doc);
            await presentationDelay(win, Math.min(completionCatchupStageMs, remainingBudget));
        }

        updateOverlayStage(doc, finalStage, finalDetail);
        displayedProgressStage = finalStage || "Complete";
        displayedProgressAt = Date.now();
        const finalBudget = Math.max(0, deadline - Date.now());
        if (finalBudget > 0)
        {
            await presentationDelay(win, Math.min(completionFinalStageMs, finalBudget));
        }

        clearProgressPresentation(win);
    }

    function presentationDelay(win, milliseconds) {
        const delay = Math.max(0, Math.floor(Number(milliseconds || 0)));
        if (delay <= 0)
        {
            return Promise.resolve();
        }

        return new Promise(resolve => win.setTimeout(resolve, delay));
    }

    function clearProgressPresentation(win) {
        if (progressDisplayTimer !== null && win)
        {
            win.clearTimeout(progressDisplayTimer);
        }

        progressDisplayTimer = null;
        displayedProgressStage = "";
        displayedProgressAt = 0;
        pendingProgressSnapshots = [];
    }

    function applyProgressSnapshot(snapshot, doc) {
        if (!snapshot || !doc)
        {
            return;
        }

        const overlay = doc.querySelector("[data-processing-overlay]");
        if (!overlay)
        {
            return;
        }

        const stage = String(snapshot.stage || "Processing");
        const detail = String(snapshot.detail || "");
        updateOverlayStage(doc, stage, detail);

        const current = normalizeProgressNumber(snapshot.current);
        const total = normalizeProgressNumber(snapshot.total);
        setProgressBar(overlay, current, total);

        const counter = overlay.querySelector("[data-processing-overlay-counter]");
        if (counter)
        {
            if (current !== null && total !== null && total > 0)
            {
                const percent = Math.min(100, Math.max(0, (current / total) * 100));
                counter.textContent = formatProgressValue(current, snapshot.unit) + " / " +
                    formatProgressValue(total, snapshot.unit) + " · " + percent.toFixed(percent >= 10 ? 0 : 1) + "%";
            }
            else if (current !== null)
            {
                counter.textContent = formatProgressValue(current, snapshot.unit);
            }
            else
            {
                counter.textContent = "";
            }
        }
    }

    function normalizeProgressNumber(value) {
        if (value === null || value === undefined || value === "")
        {
            return null;
        }

        const number = Number(value);
        return Number.isFinite(number) && number >= 0 ? number : null;
    }

    function setProgressBar(overlay, current, total) {
        const track = overlay?.querySelector("[data-processing-progress]");
        const bar = overlay?.querySelector("[data-processing-progress-bar]");
        if (!track || !bar)
        {
            return;
        }

        if (current !== null && total !== null && total > 0)
        {
            const percent = Math.min(100, Math.max(0, (current / total) * 100));
            track.classList.add("is-determinate");
            bar.style.width = percent + "%";
            bar.style.left = "0";
            track.setAttribute("aria-valuenow", String(Math.round(percent)));
            return;
        }

        track.classList.remove("is-determinate");
        bar.style.width = "";
        bar.style.left = "";
        track.removeAttribute("aria-valuenow");
    }

    function updateOverlayStage(doc, stageText, detailText) {
        const overlay = doc.querySelector("[data-processing-overlay]");
        const stage = overlay?.querySelector("[data-processing-overlay-stage]");
        const detail = overlay?.querySelector("[data-processing-overlay-detail]");
        if (stage && stageText)
        {
            stage.textContent = stageText;
        }

        if (detail)
        {
            detail.textContent = detailText || "";
        }
    }

    function formatProgressValue(value, unit) {
        const numeric = Number(value || 0);
        const normalizedUnit = String(unit || "").toLowerCase();
        if (normalizedUnit === "bytes")
        {
            return formatBytes(numeric);
        }

        const suffix = unit ? " " + unit : "";
        return Math.round(numeric).toLocaleString() + suffix;
    }

    function formatBytes(bytes) {
        const value = Math.max(0, Number(bytes || 0));
        if (value >= 1024 * 1024 * 1024)
        {
            return (value / (1024 * 1024 * 1024)).toFixed(2).replace(/\.00$/, "") + " GB";
        }

        if (value >= 1024 * 1024)
        {
            return (value / (1024 * 1024)).toFixed(2).replace(/\.00$/, "") + " MB";
        }

        if (value >= 1024)
        {
            return (value / 1024).toFixed(2).replace(/\.00$/, "") + " KB";
        }

        return Math.round(value).toLocaleString() + " bytes";
    }

    function replaceDocument(html, responseUrl, doc, win) {
        stopAllTimers(win);
        try
        {
            if (responseUrl && win.history?.pushState)
            {
                // Match normal form-navigation semantics: a completed search/action
                // becomes a new history entry so Back returns to the prior page.
                win.history.pushState({}, "", responseUrl);
            }
            else if (responseUrl && win.history?.replaceState)
            {
                win.history.replaceState({}, "", responseUrl);
            }
        }
        catch
        {
            // The returned document can still be rendered if history updates are unavailable.
        }

        doc.open();
        doc.write(html);
        doc.close();
    }

    async function handleLiveRequestFailure(error, doc, win) {
        const message = error instanceof Error ? error.message : String(error || "The operation could not complete.");
        reset(doc);
        if (win.StudioDialog?.alert)
        {
            await win.StudioDialog.alert({
                title: "Operation could not complete",
                message,
                variant: "warning",
                confirmText: "OK"
            });
        }
        else
        {
            console.error(message);
        }
    }

    function waitForDownload(form, doc, win) {
        const token = String(form.dataset.processingDownloadToken || "");
        const cookieName = String(form.dataset.processingDownloadCookie || "MdsDownload");
        if (!token || !cookieName)
        {
            return;
        }

        clearDownloadCookie(doc, cookieName);
        if (downloadTimer !== null)
        {
            win.clearInterval(downloadTimer);
        }

        const started = Date.now();
        downloadTimer = win.setInterval(() => {
            const value = readCookie(doc, cookieName);
            if (value === token)
            {
                clearDownloadCookie(doc, cookieName);
                win.clearInterval(downloadTimer);
                downloadTimer = null;
                if (downloadFinishing)
                {
                    return;
                }

                downloadFinishing = true;
                stopProgressPolling(win);
                void finishProgressPresentation(
                    doc,
                    win,
                    "Download ready",
                    "The export was created successfully and the browser is receiving the file.")
                    .finally(() => {
                        downloadFinishing = false;
                        reset(doc);
                    });
                return;
            }

            if (Date.now() - started > 30 * 60 * 1000)
            {
                win.clearInterval(downloadTimer);
                downloadTimer = null;
                reset(doc);
            }
        }, 500);
    }

    function readCookie(doc, name) {
        const prefix = encodeURIComponent(name) + "=";
        const parts = String(doc.cookie || "").split(";");
        for (const part of parts)
        {
            const value = part.trim();
            if (value.startsWith(prefix))
            {
                return decodeURIComponent(value.substring(prefix.length));
            }
        }

        return "";
    }

    function clearDownloadCookie(doc, name) {
        doc.cookie = encodeURIComponent(name) + "=; Max-Age=0; Path=/; SameSite=Strict";
    }

    function setButtonProcessing(button, label) {
        if (!button.dataset.processingOriginalDisabled)
        {
            button.dataset.processingOriginalDisabled = button.disabled ? "true" : "false";
        }

        if (button.tagName?.toLowerCase() === "input")
        {
            if (!button.dataset.processingOriginalValue)
            {
                button.dataset.processingOriginalValue = button.value || "";
            }

            button.value = label;
        }
        else
        {
            if (!button.dataset.processingOriginalHtml)
            {
                button.dataset.processingOriginalHtml = button.innerHTML;
            }

            button.innerHTML = '<span class="button-spinner" aria-hidden="true"></span><span>' + escapeHtml(label) + "</span>";
        }

        button.disabled = true;
        button.setAttribute("aria-disabled", "true");
    }

    function startElapsedTimer(overlay, win) {
        if (elapsedTimer !== null)
        {
            win.clearInterval(elapsedTimer);
        }

        const started = Date.now();
        const update = () => {
            const elapsed = overlay?.querySelector("[data-processing-overlay-elapsed]");
            if (elapsed)
            {
                elapsed.textContent = "Processing for " + formatElapsed(Date.now() - started);
            }
        };

        update();
        elapsedTimer = win.setInterval(update, 1000);
    }

    function stopAllTimers(win) {
        if (elapsedTimer !== null && win)
        {
            win.clearInterval(elapsedTimer);
            elapsedTimer = null;
        }

        if (downloadTimer !== null && win)
        {
            win.clearInterval(downloadTimer);
            downloadTimer = null;
        }

        stopProgressPolling(win);
        clearProgressPresentation(win);
    }

    function reset(doc) {
        if (!doc)
        {
            return;
        }

        const win = doc.defaultView || (typeof window !== "undefined" ? window : null);
        stopAllTimers(win);

        doc.querySelectorAll('form[data-processing-form][data-processing="true"]').forEach(form => {
            delete form.dataset.processing;
            form.removeAttribute("aria-busy");
            restoreNativeOperationUrl(form);

            form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(button => {
                if (button.tagName?.toLowerCase() === "input")
                {
                    if (button.dataset.processingOriginalValue !== undefined)
                    {
                        button.value = button.dataset.processingOriginalValue;
                    }
                }
                else if (button.dataset.processingOriginalHtml !== undefined)
                {
                    button.innerHTML = button.dataset.processingOriginalHtml;
                }

                button.disabled = button.dataset.processingOriginalDisabled === "true";
                button.removeAttribute("aria-disabled");
            });
        });

        const overlay = doc.querySelector("[data-processing-overlay]");
        if (overlay)
        {
            overlay.hidden = true;
            delete overlay.dataset.active;
            setProgressBar(overlay, null, null);
        }

        doc.body?.classList.remove("operation-processing");
    }

    function createOperationId(win) {
        const crypto = win?.crypto || (typeof globalThis !== "undefined" ? globalThis.crypto : null);
        if (crypto?.randomUUID)
        {
            return crypto.randomUUID().replace(/-/g, "").toLowerCase();
        }

        const bytes = new Uint8Array(16);
        if (crypto?.getRandomValues)
        {
            crypto.getRandomValues(bytes);
            return Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
        }

        let fallback = "";
        for (let index = 0; index < 32; index++)
        {
            fallback += Math.floor(Math.random() * 16).toString(16);
        }

        return fallback;
    }

    function formatElapsed(milliseconds) {
        const totalSeconds = Math.max(0, Math.floor(Number(milliseconds || 0) / 1000));
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return String(hours).padStart(2, "0") + ":" + String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0");
        }

        return String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0");
    }

    function escapeHtml(value) {
        return String(value || "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    return {
        initialize,
        begin,
        reset,
        readCookie,
        formatElapsed,
        escapeHtml,
        createOperationId,
        applyProgressSnapshot,
        normalizeProgressNumber,
        formatProgressValue
    };
});
