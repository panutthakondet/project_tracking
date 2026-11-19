const vm = require('node:vm'), fs = require('node:fs'), assert = require('node:assert/strict');
const source = fs.readFileSync('wwwroot/js/security-scan-actions.js', 'utf8');
async function check(response, reject = false) {
    let handler, ended = 0, clicked = 0, shown = 0, errorText = '', filename;
    const attrs = {};
    const link = { href: 'https://example.test/SecurityScans/Report/18?format=md',
        addEventListener: (_, fn) => handler = fn, getAttribute: key => attrs[key] ?? null,
        setAttribute: (key, value) => attrs[key] = value, removeAttribute: key => delete attrs[key],
        closest: () => ({ after: node => errorText = node.textContent }) };
    const url = class extends URL {};
    url.createObjectURL = () => 'blob:test'; url.revokeObjectURL = () => {};
    vm.runInNewContext(source, {
        document: { querySelectorAll: () => [link], getElementById: () => null,
            body: { append() {} }, createElement: () => ({ setAttribute() {}, remove() {}, click() { clicked++; filename = this.download; } }) },
        window: { ProjectTrackingLoading: { show: (_, options) => { shown++; assert.equal(options.delay, 0); return () => ended++; } } },
        URL: url, AbortController, setTimeout: () => 1, clearTimeout() {},
        fetch: async () => { if (reject) throw new Error('network'); return response; }
    });
    const event = { button: 0, preventDefault() {} };
    await Promise.all([handler(event), handler(event)]);
    assert.equal(shown, 1); assert.equal(ended, 1); assert.equal(attrs['aria-disabled'], undefined);
    return { clicked, errorText, filename };
}
(async () => {
    const success = await check({ ok: true, redirected: false, headers: { get: () => 'attachment; filename=security-scan-18.md' }, blob: async () => ({}) });
    assert.equal(success.clicked, 1); assert.equal(success.filename, 'security-scan-18.md');
    for (const response of [{ ok: false, headers: { get: () => '' } }, { ok: true, redirected: true, headers: { get: () => '' } }]) {
        const failure = await check(response); assert.equal(failure.clicked, 0); assert.ok(failure.errorText);
    }
    const network = await check(null, true); assert.equal(network.clicked, 0); assert.ok(network.errorText);
    console.log('PASS scan download: immediate loading, deduplication, filename, errors and cleanup');
})().catch(error => { console.error(error); process.exitCode = 1; });
