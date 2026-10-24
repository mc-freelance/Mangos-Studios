// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Native desktop startup, session isolation, or optional device-identity support.

namespace MangosDatabaseStudio.App.Desktop
{
    internal static class DesktopStartupPage
    {
        public const string Html = @"
<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<meta http-equiv=""Cache-Control"" content=""no-store"">
<title>MaNGOS Database Studio</title>
<style>

:root {
    color-scheme: dark
}

* {
    box-sizing: border-box
}

html,body {
    width: 100%;
    height: 100%;
    margin: 0
}

body {
    font-family: Inter,Segoe UI,Arial,sans-serif;
    background: #07110f;
    color: #ecf7f3;
    display: grid;
    place-items: center;
    overflow: hidden
}

.shell {
    width: min(560px,calc(100vw - 48px));
    padding: 42px 44px 38px;
    border: 1px solid rgba(126,201,177,.18);
    border-radius: 18px;
    background: linear-gradient(145deg,rgba(19,38,34,.98),rgba(8,20,18,.98));
    box-shadow: 0 28px 70px rgba(0,0,0,.4)
}

.brand {
    display: flex;
    align-items: center;
    gap: 15px;
    margin-bottom: 34px
}

.mark {
    width: 46px;
    height: 46px;
    border-radius: 13px;
    border: 1px solid rgba(207,172,78,.32);
    display: grid;
    place-items: center;
    color: #e6c86e;
    font-size: 25px;
    font-weight: 800;
    background: rgba(207,172,78,.08)
}

.name {
    font-size: 18px;
    font-weight: 750;
    letter-spacing: .02em
}

.sub {
    margin-top: 4px;
    font-size: 11px;
    letter-spacing: .16em;
    text-transform: uppercase;
    color: #76b9a5
}

.status-row {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 20px
}

.status {
    font-size: 15px;
    font-weight: 650
}

.percent {
    font-size: 13px;
    font-weight: 750;
    font-variant-numeric: tabular-nums;
    color: #83c7b2
}

.detail {
    margin-top: 10px;
    font-size: 12px;
    line-height: 1.55;
    color: #9ab1aa;
    min-height: 38px
}

.track {
    height: 8px;
    margin-top: 26px;
    border-radius: 99px;
    background: rgba(255,255,255,.075);
    overflow: hidden;
    box-shadow: inset 0 0 0 1px rgba(255,255,255,.025)
}

.bar {
    height: 100%;
    width: 0;
    border-radius: inherit;
    background: linear-gradient(90deg,#4b8f7b,#79c5ae);
    transition: width .32s ease
}

.error {
    color: #efb7b7
}

.error~.track .bar {
    background: #b96b6b
}

.foot {
    margin-top: 24px;
    font-size: 10px;
    color: #657a74;
    letter-spacing: .08em;
    text-transform: uppercase
}
</style>
</head>
<body>
<main class=""shell"">
    <div class=""brand"">
        <div class=""mark"">M</div>
        <div>
            <div class=""name"">MaNGOS Database Studio</div>
            <div class=""sub"">Desktop workspace</div>
        </div>
    </div>
    <div class=""status-row"">
        <div id=""status"" class=""status"">Starting application</div>
        <div id=""percent"" class=""percent"">0%</div>
    </div>
    <div id=""detail"" class=""detail"">Preparing the secure desktop workspace...</div>
    <div class=""track"" role=""progressbar"" aria-label=""Application startup progress"" aria-valuemin=""0"" aria-valuemax=""100"" aria-valuenow=""0"">
        <div id=""bar"" class=""bar""></div>
    </div>
    <div class=""foot"">Application Designed and Written by Meltie2013</div>
</main>
<script>
(() => {
  const status = document.getElementById('status');
  const detail = document.getElementById('detail');
  const percent = document.getElementById('percent');
  const bar = document.getElementById('bar');
  const track = document.querySelector('.track');
  const visibleSince = performance.now();
  const minimumVisibleMs = 1800;
  const completedHoldMs = 700;
  let shownProgress = 0;
  let redirectScheduled = false;

  function renderProgress(value) {
    const numeric = Number.isFinite(Number(value)) ? Number(value) : 0;
    shownProgress = Math.max(shownProgress, Math.min(100, Math.max(0, Math.round(numeric))));
    bar.style.width = shownProgress + '%';
    percent.textContent = shownProgress + '%';
    track.setAttribute('aria-valuenow', String(shownProgress));
  }

  function scheduleOpen() {
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

  async function check() {
    try
    {
      const response = await fetch('/__studio/status', { cache: 'no-store', credentials: 'same-origin' });
      if (!response.ok)
      {
          throw new Error('Startup status unavailable');
      }
      const state = await response.json();

      renderProgress(state.progress);
      if (state.status)
      {
          status.textContent = state.status;
      }
      if (state.detail)
      {
          detail.textContent = state.detail;
      }

      if (state.failed)
      {
        status.textContent = 'Startup failed';
        status.classList.add('error');
        detail.textContent = state.message || state.detail || 'The application could not be initialized.';
        return;
      }

      if (state.ready)
      {
        scheduleOpen();
        return;
      }
    } catch (_)
    {
      if (shownProgress < 5)
      {
          renderProgress(5);
      }
      status.textContent = 'Starting desktop services';
      detail.textContent = 'Waiting for the secure desktop workspace to become available...';
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
