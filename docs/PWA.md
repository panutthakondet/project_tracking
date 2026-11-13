# ProjectTracking PWA

- Deploy over HTTPS. Local development works on localhost.
- The shared layout and Login include the manifest, Apple touch icon and service-worker registration.
- Chrome/Edge/Android: use the browser installation action, or “ติดตั้งแอป ProjectTracking” when the browser makes installation available (in the profile/menu or Login).
- iPhone/iPad: open in Safari, Share → Add to Home Screen. The install button provides these instructions.
- Installation launches the existing app in a standalone window; session, permissions and login rules remain unchanged.

## Offline and updates

Only `offline.html` and the four public PWA icons are stored in the service-worker cache. HTML navigations are network-only, with a generic Thai offline page on network failure. API calls, uploads, notes, authentication responses and POST requests are never added to Cache Storage.

The service worker does not call `skipWaiting` or reload open pages. An update waits for the previous worker's app windows/tabs to close, protecting unsaved forms. When editing cached public resources, change the `CACHE` version in `wwwroot/sw.js`; activation removes only older caches with the `projecttracking-pwa-` prefix. It never clears other applications' caches.

`sw.js`, `offline.html` and the manifest have `Cache-Control: no-cache`; `.webmanifest` is served as `application/manifest+json` by ASP.NET static-file middleware.

Offline editing, background sync and push notifications are not implemented. Work requires an internet connection. Verify actual installation on the deployed HTTPS domain and target devices after deployment.
