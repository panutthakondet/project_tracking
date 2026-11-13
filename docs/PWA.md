# ProjectTracking PWA

- Deploy over HTTPS. Local development works on localhost.
- The shared layout and Login include the manifest, Apple touch icon and service-worker registration.
- Chrome/Edge/Android: use the browser installation action, or “ติดตั้งแอป ProjectTracking” when the browser makes installation available (in the profile/menu or Login).
- iPhone/iPad: open in Safari, Share → Add to Home Screen. The install button provides these instructions.
- Installation requests fullscreen, with standalone fallback when fullscreen is unsupported; session, permissions and login rules remain unchanged.

Existing installations may require closing/reopening or reinstalling after deployment before the browser adopts a new manifest display mode. System bars on iOS/iPadOS remain controlled by the OS. Safe-area insets protect content around notches and home indicators.

## Offline and updates

Only `offline.html` and the four public PWA icons are stored in the service-worker cache. Authentication routes bypass the service worker entirely, leaving login/logout redirects and cookies to the browser. Other HTML navigations are network-only, with a generic Thai connection-error page on network failure (which does not assume the user’s internet is down). API calls, uploads, notes, authentication responses and POST requests are never added to Cache Storage.

The service worker does not call `skipWaiting` or reload open pages. An update waits for the previous worker's app windows/tabs to close, protecting unsaved forms. When editing cached public resources, change the `CACHE` version in `wwwroot/sw.js`; activation removes only older caches with the `projecttracking-pwa-` prefix. It never clears other applications' caches.

`sw.js`, `offline.html` and the manifest have `Cache-Control: no-cache`; `.webmanifest` is served as `application/manifest+json` by ASP.NET static-file middleware.

Offline editing and background sync are not implemented. In-app and Web Push notifications are described in [Notifications.md](Notifications.md). Work requires an internet connection. Verify actual installation on the deployed HTTPS domain and target devices after deployment.

## App icon

The current icon is derived from `wwwroot/pwa/app-icon-source.png` supplied by the user. Versioned `*-v2.png` URLs prevent reuse of the previous installed icon. The maskable variant adds safe padding and an opaque background; the standard variants preserve the original artwork. Older icon files remain available for existing installations during updates.
