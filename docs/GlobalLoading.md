# Shared modern loading spinner

`_GlobalLoading.cshtml` loads `global-loading.css` and `global-loading.js` once. `_Layout` and `_StandaloneTheme` include it; standalone pages without the theme include it directly, including Login. Print media hides the component.

Standard same-window form submissions show the shared spinner only after native and page validation. Cancelled/AJAX-handled submits do not trigger it. Repeat POST submissions are blocked without disabling named submit buttons or removing submitted values. Repeated GET filters remain allowed so the latest selection wins. `pageshow` resets the spinner and restores busy/ARIA state, including browser back/forward cache restores. Programmatic submits use `ProjectTrackingLoading.submit(form)` to preserve their existing direct-submit behavior.

Downloads, exports, print actions, other-window targets and `data-loading="off"` are excluded from automatic handling because they may not navigate the current document. Use `data-loading-message` for a specific native-form message.

Frontend API:

```javascript
const stop = ProjectTrackingLoading.show('กำลังบันทึก…');
try { await operation(); } finally { stop(); }

await ProjectTrackingLoading.run(() => save(), 'กำลังบันทึก…');
await ProjectTrackingLoading.fetch(url, options);
await ProjectTrackingLoading.fetch(url, options, {show:true, blocking:false, message:'กำลังโหลด…'});
```

`fetch` is explicitly used by page actions; the native global `window.fetch` is **not** replaced. By default, GET/HEAD and heartbeat/token/poll requests remain quiet. AJAX mutations show the spinner after a short 150ms delay and release it on success, rejection or abort. Multiple operations share a reference-counted indicator. Real-time room/reordering actions use a nonblocking indicator. Employee chunked uploads own one nonblocking token through their existing `finally`, leaving progress and cancel controls usable. Document tabs use a nonblocking indicator. Notification polling and Dashboard colour switching keep their existing lightweight behavior.

The spinner uses theme CSS tokens, conic gradients, a masked ring and subtle glow. `prefers-reduced-motion` stops rotation while the status text remains. Business APIs, request payloads and notification delivery are unchanged.

Run `node tests/Loading/global-loading.test.cjs` to check concurrency, success/failure cleanup, quiet background requests, cancelled/invalid forms, named button preservation, duplicate POST protection, latest GET filters, download/target exclusions and browser-back reset. These are isolated frontend tests; no production forms or notifications are submitted.
