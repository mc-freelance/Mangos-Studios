// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Render the gated desktop startup and Field Data recovery page without MVC dependencies.

namespace MangosDatabaseStudio.App.Desktop
{
    internal static class DesktopStartupPage
    {
        public const string Html = @"<!doctype html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <meta http-equiv=""Cache-Control"" content=""no-store"">
    <title>MaNGOS Database Studio — Starting</title>
    <style>
        :root {
            color-scheme: dark;
            font-family: Inter, ""Segoe UI"", Arial, sans-serif;
        }

        * {
            box-sizing: border-box;
        }

        html, body {
            min-height: 100%;
            margin: 0;
        }

        body {
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 24px 16px;
            color: #ecf7f3;
            background: #07110f;
        }

        .shell {
            width: min(620px, 100%);
            padding: 34px 38px;
            border: 1px solid rgba(126, 201, 177, 0.2);
            border-radius: 18px;
            background: linear-gradient(145deg, #132622, #081412);
            box-shadow: 0 28px 70px rgba(0, 0, 0, 0.4);
        }

        .brand {
            display: flex;
            align-items: center;
            gap: 15px;
            margin-bottom: 30px;
        }

        .mark {
            flex: 0 0 46px;
            width: 46px;
            height: 46px;
            display: grid;
            place-items: center;
            border: 1px solid rgba(207, 172, 78, 0.32);
            border-radius: 13px;
            background: rgba(207, 172, 78, 0.08);
            color: #e6c86e;
            font-size: 25px;
            font-weight: 800;
        }

        .name {
            font-size: 18px;
            font-weight: 750;
        }

        .sub {
            margin-top: 5px;
            color: #76b9a5;
            font-size: 11px;
            letter-spacing: 0.13em;
            text-transform: uppercase;
        }

        .status-row, .catalog-heading {
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            gap: 16px;
        }

        .status {
            font-size: 15px;
            font-weight: 650;
        }

        .percent {
            color: #83c7b2;
            font-size: 13px;
            font-weight: 750;
            font-variant-numeric: tabular-nums;
            white-space: nowrap;
        }

        .detail {
            min-height: 36px;
            margin-top: 10px;
            color: #a4bdb5;
            font-size: 12px;
            line-height: 1.65;
            overflow-wrap: anywhere;
        }

        .track {
            height: 8px;
            margin-top: 22px;
            overflow: hidden;
            border-radius: 99px;
            background: rgba(255, 255, 255, 0.08);
        }

        .bar {
            width: 0;
            height: 100%;
            border-radius: inherit;
            background: linear-gradient(90deg, #4b8f7b, #79c5ae);
            transition: width 0.25s ease;
        }

        .catalog {
            margin-top: 26px;
            padding: 16px 18px;
            border: 1px solid rgba(126, 201, 177, 0.14);
            border-radius: 12px;
            background: rgba(0, 0, 0, 0.13);
        }

        .catalog-title {
            font-size: 12px;
            font-weight: 650;
        }

        .catalog-count {
            color: #83c7b2;
            font-size: 12px;
            font-variant-numeric: tabular-nums;
        }

        .catalog-current {
            margin-top: 9px;
            color: #a4bdb5;
            font-size: 12px;
            line-height: 1.6;
            overflow-wrap: anywhere;
        }

        .failure {
            margin-top: 24px;
            padding-top: 22px;
            border-top: 1px solid rgba(227, 121, 121, 0.24);
        }

        .failure h2 {
            margin: 0 0 8px;
            color: #f5c4c4;
            font-size: 15px;
            font-weight: 700;
        }

        .failure p, .failure li {
            color: #c0ceca;
            font-size: 12px;
            line-height: 1.65;
        }

        .failure ol {
            padding-left: 22px;
            margin: 12px 0 17px;
        }

        .failure li + li {
            margin-top: 9px;
        }

        .diagnostic {
            margin: 14px 0;
            padding: 12px 14px;
            border: 1px solid rgba(225, 125, 125, 0.18);
            border-radius: 8px;
            color: #f0c8c8;
            background: rgba(115, 42, 42, 0.12);
            font-size: 12px;
            line-height: 1.6;
            overflow-wrap: anywhere;
            user-select: text;
        }

        .copy-button {
            padding: 10px 16px;
            border: 1px solid rgba(126, 201, 177, 0.45);
            border-radius: 8px;
            color: #e1f5ee;
            background: rgba(74, 142, 123, 0.18);
            cursor: pointer;
            font: inherit;
            font-size: 12px;
            font-weight: 650;
        }

        .copy-button:hover, .copy-button:focus-visible {
            background: rgba(74, 142, 123, 0.3);
        }

        .copy-feedback {
            margin-left: 10px;
            color: #9bd5c0;
            font-size: 12px;
        }

        .shell.failed .status, .shell.failed .percent {
            color: #f1b9b9;
        }

        .shell.failed .bar {
            background: #b96b6b;
        }

        .foot {
            margin-top: 26px;
            color: #789087;
            font-size: 10px;
            letter-spacing: 0.07em;
            text-transform: uppercase;
        }

        [hidden] {
            display: none !important;
        }

        @media (max-width: 540px) {
            body {
                align-items: flex-start;
                padding: 14px;
            }

            .shell {
                padding: 25px 20px;
                border-radius: 13px;
            }

            .brand {
                margin-bottom: 24px;
            }

            .name {
                font-size: 16px;
            }
        }

        @media (prefers-reduced-motion: reduce) {
            .bar {
                transition: none;
            }
        }
    </style>
</head>
<body>
    <main id=""shell"" class=""shell"">
        <header class=""brand"">
            <div class=""mark"" aria-hidden=""true"">M</div>
            <div>
                <div class=""name"">MaNGOS Database Studio</div>
                <div class=""sub"">Desktop workspace</div>
            </div>
        </header>

        <div class=""status-row"">
            <div id=""status"" class=""status"" role=""status"" aria-live=""polite"">Starting application</div>
            <div id=""percent"" class=""percent"">0%</div>
        </div>
        <div id=""detail"" class=""detail"">Preparing the secure desktop workspace...</div>
        <div id=""track"" class=""track"" role=""progressbar"" aria-label=""Application startup progress"" aria-valuemin=""0"" aria-valuemax=""100"" aria-valuenow=""0"">
            <div id=""bar"" class=""bar""></div>
        </div>

        <section id=""catalog"" class=""catalog"" aria-label=""Field Data synchronization progress"" hidden>
            <div class=""catalog-heading"">
                <span class=""catalog-title"">SQLite Field Data catalog</span>
                <span id=""catalog-count"" class=""catalog-count"">0 / 0 definitions</span>
            </div>
            <div id=""catalog-current"" class=""catalog-current"">Checking built-in field definitions...</div>
        </section>

        <section id=""failure"" class=""failure"" aria-labelledby=""failure-title"" hidden>
            <h2 id=""failure-title"">Startup blocked — normal workspace remains locked</h2>
            <p id=""failure-area"">The application could not complete its startup checks.</p>
            <div id=""diagnostic"" class=""diagnostic"" role=""alert""></div>
            <h2>Suggested recovery steps</h2>
            <ol id=""recovery-steps""></ol>
            <button id=""copy-button"" class=""copy-button"" type=""button"">Copy diagnostic details</button>
            <span id=""copy-feedback"" class=""copy-feedback"" role=""status"" aria-live=""polite""></span>
        </section>

        <footer class=""foot"">Application Designed and Written by Meltie2013</footer>
    </main>

    <script>
        (() => {
            const shell = document.getElementById('shell');
            const status = document.getElementById('status');
            const detail = document.getElementById('detail');
            const percent = document.getElementById('percent');
            const bar = document.getElementById('bar');
            const track = document.getElementById('track');
            const catalog = document.getElementById('catalog');
            const catalogCount = document.getElementById('catalog-count');
            const catalogCurrent = document.getElementById('catalog-current');
            const failure = document.getElementById('failure');
            const failureArea = document.getElementById('failure-area');
            const diagnostic = document.getElementById('diagnostic');
            const recoverySteps = document.getElementById('recovery-steps');
            const copyButton = document.getElementById('copy-button');
            const copyFeedback = document.getElementById('copy-feedback');
            const visibleSince = performance.now();
            const minimumVisibleMs = 1800;
            const completedHoldMs = 700;
            let shownProgress = 0;
            let redirectScheduled = false;
            let failureReport = '';

            // Keep the progress indicator monotonic even during rapid status updates.
            function renderProgress(value)
            {
                const numeric = Number.isFinite(Number(value)) ? Number(value) : 0;
                shownProgress = Math.max(shownProgress, Math.min(100, Math.max(0, Math.round(numeric))));
                bar.style.width = shownProgress + '%';
                percent.textContent = shownProgress + '%';
                track.setAttribute('aria-valuenow', String(shownProgress));
            }

            // Surface the current world table / column so long catalog updates are visible.
            function renderFieldData(fieldData)
            {
                if (!fieldData || !fieldData.visible)
                {
                    return;
                }

                catalog.hidden = false;
                catalogCount.textContent = fieldData.completed + ' / ' + fieldData.total + ' definitions';
                catalogCurrent.textContent = fieldData.current || 'Checking the SQLite Field Data catalog...';
            }

            // Recovery instructions are supplied as text by the authenticated local host.
            function renderFailure(state)
            {
                shell.classList.add('failed');
                failure.hidden = false;
                status.textContent = state.status || 'Startup failed';
                detail.textContent = state.detail || 'The application could not be initialized.';
                failureArea.textContent = (state.failureArea || 'Application initialization') + ' failed. The normal workspace is disabled until the issue is fixed.';
                diagnostic.textContent = state.message || 'No additional error details are available.';
                recoverySteps.replaceChildren();

                const steps = Array.isArray(state.recoverySteps) ? state.recoverySteps : [];
                for (const step of steps)
                {
                    const item = document.createElement('li');
                    item.textContent = step;
                    recoverySteps.appendChild(item);
                }

                failureReport = 'MaNGOS Database Studio startup failure\n' +
                    'Area: ' + (state.failureArea || 'Application initialization') + '\n' +
                    'Status: ' + status.textContent + '\n' +
                    'Details: ' + diagnostic.textContent + '\n' +
                    'Field Data: ' + catalogCurrent.textContent;
            }

            // The workspace is opened only when the backend reports complete success.
            function scheduleOpen()
            {
                if (redirectScheduled)
                {
                    return;
                }

                redirectScheduled = true;
                renderProgress(100);
                status.textContent = 'Startup complete';
                detail.textContent = 'MaNGOS Database Studio is ready. Opening the login workspace...';
                const elapsed = performance.now() - visibleSince;
                const delay = Math.max(completedHoldMs, minimumVisibleMs - elapsed);
                setTimeout(() => location.replace('/'), delay);
            }

            // Copy only a diagnostic summary; database contents and keys are not collected.
            copyButton.addEventListener('click', async () => {
                try
                {
                    await navigator.clipboard.writeText(failureReport);
                    copyFeedback.textContent = 'Copied.';
                }
                catch (_)
                {
                    copyFeedback.textContent = 'Select and copy the diagnostic text above.';
                }
            });

            // Poll a small local-only status endpoint while application services initialize.
            async function check()
            {
                try
                {
                    const response = await fetch('/__studio/status', {
                        cache: 'no-store',
                        credentials: 'same-origin'
                    });

                    if (!response.ok)
                    {
                        throw new Error('Startup status unavailable');
                    }

                    const state = await response.json();
                    renderProgress(state.progress);
                    renderFieldData(state.fieldData);
                    status.textContent = state.status || status.textContent;
                    detail.textContent = state.detail || detail.textContent;

                    if (state.failed)
                    {
                        renderFailure(state);
                        return;
                    }

                    if (state.ready)
                    {
                        scheduleOpen();
                        return;
                    }
                }
                catch (_)
                {
                    if (shownProgress < 5)
                    {
                        renderProgress(5);
                    }

                    status.textContent = 'Waiting for desktop services';
                    detail.textContent = 'Waiting for the secure local startup service to respond...';
                }

                setTimeout(check, 480);
            }

            renderProgress(2);
            check();
        })();
    </script>
</body>
</html>
";
    }
}
