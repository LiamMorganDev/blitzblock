const { spawn } = require('node:child_process');
const { writeFileSync } = require('node:fs');
const { join } = require('node:path');
const assert = require('node:assert/strict');

const exe = process.env.BLITZBLOCK_BLITZ_EXE || join(process.env.LOCALAPPDATA, 'Programs/Blitz/Blitz.exe');
const domains = ['aditude.io', 'doubleclick.net', 'googlesyndication.com', 'googletagservices.com', 'adnxs.com', 'primis.tech', 'kueezrtb.com'];
const rules = domains.flatMap(domain => ['MAP ' + domain + ' ~NOTFOUND', 'MAP *.' + domain + ' ~NOTFOUND']).join(', ');
const app = spawn(exe, ['--host-resolver-rules=' + rules, '--remote-debugging-pipe', ...process.argv.slice(2).filter(arg => arg === '--autostart')], {
  stdio: ['ignore', 'ignore', 'ignore', 'pipe', 'pipe']
});
const verifyFolder = process.argv.slice(2).find(arg => !arg.startsWith('--'));
const requests = new Map();
let sequence = 0;
let buffer = '';
let verified = false;
const rail = '[class~="🤑-wrapper"]';
const grid = '.content-container.width-rail:has(> .ads-rail-marker)';
const banner = 'a.leaderboard-promo';
const promoArt = ':is(img, source, video):is([src*="/self-promotion/"], [srcset*="/self-promotion/"], [poster*="/self-promotion/"], [src*="/blitz/premium/cta/"])';
const promoSlot = ':is([class^="🤑-"], [class*=" 🤑-"])';
const sharedPromos = `
  ${promoSlot}, ${promoArt},
  :is(a, button, picture):has(${promoArt}),
  :is([style*="/self-promotion/"], [style*="/blitz/premium/cta/"]) { display: none !important; }
`;
const css = `
  ${rail}, ${grid} > .ads-rail-marker, ${banner},
  .leaderboard-container:has(> ${banner}:only-child) { display: none !important; }
  ${grid} { grid-template-columns: minmax(0, 1fr) !important; }
  .blitz-logo > a.blitz-home-link:has(> svg) { width: auto; }
  .blitz-logo > a.blitz-home-link > svg { display: none; }
  .blitz-logo > a.blitz-home-link:has(> svg)::before {
    content: "BlitzBlock";
    font: 700 14px "Segoe UI", sans-serif;
    white-space: nowrap;
  }
`;
const blockVersion = process.env.BLITZBLOCK_VERSION;
const versionStyle = /^\d+\.\d+\.\d+$/.test(blockVersion || '') ? `
  .app-version:has(> button.app-version-label)::after {
    content: " · BB v${blockVersion}";
    color: inherit;
    font: inherit;
    font-size: 11px;
    white-space: nowrap;
  }
` : '';
const installStyle = `(() => {
  if (!['https://blitzapp.gg', 'https://blitz-desktop.blitz.gg'].includes(location.origin)) return;
  if (window.__blitzLocalPromoFilter) return;
  window.__blitzLocalPromoFilter = true;
  const style = document.createElement('style');
  style.id = 'blitz-local-promo-filter';
  style.textContent = ${JSON.stringify(css + sharedPromos + versionStyle)};
  const restore = () => {
    if (!style.isConnected && document.head) document.head.append(style);
  };
  new MutationObserver(restore).observe(document, { childList: true, subtree: true });
  restore();
})()`;

function send(method, params = {}, sessionId) {
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    requests.set(id, { resolve, reject });
    app.stdio[3].write(JSON.stringify({ id, method, params, sessionId }) + '\0');
  });
}

const traceFile = process.env.BLITZ_TEST_TRACE;
function trace(value) {
  if (traceFile) require('node:fs').appendFileSync(traceFile, JSON.stringify(value) + '\n');
}
async function apply({ targetInfo, sessionId }) {
  if (targetInfo.type !== 'page') return;
  if (traceFile) setTimeout(async () => {
    try {
      const state = await evaluate(`({ host: location.hostname, rail: document.querySelector(${JSON.stringify(rail)}) && getComputedStyle(document.querySelector(${JSON.stringify(rail)})).display, styles: [...document.querySelectorAll('style')].filter(s => s.textContent.includes('.ads-rail-marker')).length })`);
      trace(state);
      if (state?.host !== 'blitzapp.gg') return;
      assert.equal(state.styles, 1);
      assert.equal(state.rail, 'none');
      const headerLabel = await evaluate(`getComputedStyle(document.querySelector('.blitz-logo > a.blitz-home-link'), '::before').content`);
      assert.equal(headerLabel, '"BlitzBlock"');
      trace('BlitzBlock header verified');
      if (versionStyle) {
        const versions = await evaluate(`(() => { const native = document.querySelector('button.app-version-label'); return [native.textContent.trim(),getComputedStyle(native.parentElement,'::after').content]; })()`);
        assert.match(versions[0], /v?\d/);
        assert.ok(versions[1].includes('BB v' + blockVersion));
        trace('Both version labels verified');
      }
      const profileShots = process.env.BLITZ_TEST_PROFILE_SHOTS;
      if (profileShots && await evaluate("location.pathname.includes('/lol/profile/')")) {
        const snapshot = async (width, label) => {
          await send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false }, sessionId);
          const {data} = await send('Page.captureScreenshot', {format: 'png'}, sessionId);
          writeFileSync(join(profileShots, 'profile-' + label + '.png'), Buffer.from(data, 'base64'));
        };
        const setStyle = styleCss => evaluate(`document.querySelector('#blitz-local-promo-filter').textContent = ${JSON.stringify(styleCss)}`);
        const visibleArt = () => evaluate(`[...document.querySelectorAll(${JSON.stringify(promoArt)})].filter(e => e.getBoundingClientRect().width > 0).length`);
        await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }] }, sessionId);
        try {
          await setStyle(css);
          await snapshot(1440, 'desktop-before');
          assert.ok(await visibleArt(), 'Expected an inline profile promo with the previous filter');
          await snapshot(390, 'mobile-before');
          await setStyle(css + sharedPromos);
          await snapshot(1440, 'desktop-after');
          assert.equal(await visibleArt(), 0);
          await snapshot(390, 'mobile-after');
          assert.equal(await visibleArt(), 0);
          trace('Inline profile promos hidden at both widths');
        } finally {
          await setStyle(css + sharedPromos);
          await send('Emulation.clearDeviceMetricsOverride', {}, sessionId);
          await send('Emulation.setEmulatedMedia', {}, sessionId);
        }
      }
      await send('Page.reload', {}, sessionId);
      for (let i = 0; i < 24; i++) {
        await new Promise(resolve => setTimeout(resolve, 500));
        const reloaded = await evaluate(`document.querySelector('#blitz-local-promo-filter')?.isConnected && document.querySelector(${JSON.stringify(rail)}) && getComputedStyle(document.querySelector(${JSON.stringify(rail)})).display === 'none'`).catch(() => false);
        if (reloaded) { trace('Reload passed'); return; }
      }
      throw new Error('Reload did not restore filter');
    } catch (error) { trace(error.message); }
  }, 6500);
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true }, sessionId)).result.value;
  if (verifyFolder && !verified) {

    const rendered = expression => evaluate(`(() => { ${expression} })()`);
    const ready = 'return location.hostname === "blitzapp.gg" && (location.pathname.includes("/lol/match/") || !!document.querySelector(\'a[href*="/lol/match/"]\'))';
    for (let i = 0; i < 30 && !await rendered(ready); i++) await new Promise(resolve => setTimeout(resolve, 500));
    if (verified || !await rendered(ready)) return;
    verified = true;
    const onMatch = 'return location.pathname.includes("/lol/match/")';
    for (let i = 0; i < 10 && !await rendered(onMatch); i++) {
      await rendered('if (!location.pathname.includes("/lol/match/")) document.querySelector(\'a[href*="/lol/match/"]\')?.click()');
      await new Promise(resolve => setTimeout(resolve, 600));
    }
    const state = () => rendered(`
      const rail = document.querySelector(${JSON.stringify(rail)});
      const grid = document.querySelector(${JSON.stringify(grid)});
      const banner = document.querySelector(${JSON.stringify(banner)});
      if (!rail || !grid || !banner) return null;
      return { rail: getComputedStyle(rail).display, columns: getComputedStyle(grid).gridTemplateColumns,
        marker: getComputedStyle(grid.querySelector('.ads-rail-marker')).display, banner: getComputedStyle(banner).display };
    `);
    for (let i = 0; i < 24 && !await state(); i++) await new Promise(resolve => setTimeout(resolve, 500));
    if (!await state()) console.log(await rendered(`return [location.pathname.includes('/lol/match/'), !!document.querySelector(${JSON.stringify(rail)}), !!document.querySelector(${JSON.stringify(grid)}), !!document.querySelector(${JSON.stringify(banner)})]`));
    assert.ok(await state(), 'Match page promos did not load');
    await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }] }, sessionId);
    const capture = async (width, label) => {
      await send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false }, sessionId);
      const { data } = await send('Page.captureScreenshot', { format: 'png' }, sessionId);
      writeFileSync(join(verifyFolder, label + '.png'), Buffer.from(data, 'base64'));
    };

    await capture(1440, 'premium-desktop-before');
    const before = await state();
    assert.notEqual(before.rail, 'none');
    assert.notEqual(before.banner, 'none');
    assert.equal(before.columns.split(' ').length, 2);
    await capture(390, 'premium-mobile-before');
    await evaluate(installStyle);

    await capture(1440, 'premium-desktop-after');
    const after = await state();
    assert.equal(after.rail, 'none');
    assert.equal(after.banner, 'none');
    assert.equal(after.marker, 'none');
    assert.equal(after.columns.split(' ').length, 1);
    await capture(390, 'premium-mobile-after');
    await send('Emulation.clearDeviceMetricsOverride', {}, sessionId);
    await send('Emulation.setEmulatedMedia', {}, sessionId);
    console.log('Verified: Both promos hidden and grid reduced to one column.');
  }
  await send('Page.addScriptToEvaluateOnNewDocument', { source: installStyle }, sessionId);
  if (!verifyFolder) await send('Runtime.enable', {}, sessionId);

}

app.stdio[3].on('error', error => { if (error.code !== 'EPIPE') console.error(error.message); });
app.stdio[4].setEncoding('utf8');
app.stdio[4].on('data', chunk => {
  buffer += chunk;
  let end;
  while ((end = buffer.indexOf('\0')) !== -1) {
    const message = JSON.parse(buffer.slice(0, end));
    buffer = buffer.slice(end + 1);
    const request = requests.get(message.id);
    if (request) {
      requests.delete(message.id);
      if (message.error) request.reject(new Error(message.error.message));
      else request.resolve(message.result);
    }
    if (!verifyFolder && message.method === 'Runtime.executionContextCreated' && message.params.context.auxData?.isDefault) {
      const context = message.params.context;
      send('Runtime.evaluate', { expression: installStyle, contextId: context.id }, message.sessionId)
        .then(result => { if (result.exceptionDetails) trace(result.exceptionDetails.exception?.description); })
        .catch(error => trace(error.message));
    }
    if (message.method === 'Target.attachedToTarget') apply(message.params).catch(error => { trace(error.message); console.error(error.message); });
  }
});

app.on('error', error => console.error(error.message));
send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: false, flatten: true }).catch(error => console.error(error.message));
