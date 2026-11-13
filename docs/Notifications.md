# In-app and Web Push notifications

The bell in the mobile header and desktop profile shows the signed-in account's notifications, unread count, read-all control, and per-device push opt-in/out. It polls every 30 seconds while the page is visible and refreshes when a service-worker push arrives. No notification permission dialog is opened automatically.

## Event coverage

The existing Telegram notification feature switches also govern the additional web channel. Recipient employee IDs are resolved to ACTIVE `login_user` accounts through `emp_id`; users do not need a Telegram chat or bot token to receive inbox notifications. Telegram bot diagnostics, raw chat messages/webhook replies, and existing legacy `Notifications`/OpenWork routes remain unchanged.

| Existing source | Events mirrored to inbox and opted-in devices |
| --- | --- |
| PhaseAssigns | New assignment |
| ProjectIssues | Create, developer FIXED, BA result |
| SupportOrders / SupportOrdersDev | Create, FIXED, BA result |
| Followups | Create, acknowledge, owner update |
| Meetings | Create/invite, update, cancel, scheduled reminders |
| FieldService | Create, update, cancel, scheduled reminders |
| Employees/LineOverdue | Selected manual overdue notifications |
| Overdue notification background service | Automatic risk/overdue notifications |

Only notifications generated after deployment are included; historical Telegram messages are not imported. Delivery counts in the existing notification paths now represent recipients reached through either Telegram or the added web channel. Actual Telegram audit logs still record only successful Telegram sends. Meeting reminder logs and automatic daily overdue deduplication also account for web deliveries.

## Deployment and devices

- The four new MySQL tables are created at startup using the same schema-bootstrap approach as the existing app: `web_notifications`, `web_push_subscriptions`, `web_push_deliveries`, `web_push_keys`. The app database identity needs CREATE TABLE permission on first startup. No existing tables are replaced.
- HTTPS and permission from the user/device are required. On iPhone/iPad use a supported Home Screen PWA and enable notifications from the bell after installing it.
- VAPID keys are generated on first use and persist in the database. Private keys and subscription details are encrypted with the app's existing ASP.NET Data Protection configuration. Preserve the database and persistent `DATAPROTECTION_KEYS_PATH` across deployments/instances; never delete keys on each release.
- Optional `WEB_PUSH_SUBJECT` can be a valid `mailto:` administrator contact or an HTTPS application URL. It defaults to `APP_BASE_URL` (or the existing ProjectTracking deployment domain). No private VAPID keys need to be copied into the repository/client.
- The server must reach browser push providers over outbound HTTPS. Accepted subscription hosts are FCM, Mozilla Push, Apple Web Push and Microsoft WNS; redirect following is disabled. No real external push or Telegram send is needed for the automated test harness.
- Existing installed PWAs may need to close all tabs/app windows and reopen to activate service worker v3. No forced reload or `skipWaiting` is used, to preserve unsaved notes/forms.

## Privacy and reliability

Each inbox API is scoped to the session user and checks ACTIVE status. Read/subscribe/unsubscribe mutations and notification-open handoff POSTs require antiforgery tokens. Opening a notification still passes through the target screen's existing permissions. Push notifications show a generic message; personal work details remain in the authenticated inbox. Links are same-origin/local, and tapping push opens a new window unless that exact notification URL is already open, avoiding navigation away from unsaved work.

Subscriptions belong to the current login session/device, are rebound on an already-consented account switch, and are revoked on Logout. The background sender checks account/owner/active status again. Device opt-out affects only that device. A durable outbox leases jobs atomically, retries transient failures up to four attempts, and disables expired 404/410 subscriptions. A five-minute content key suppresses duplicate event retries; separate scheduled-event logs prevent repeated reminders.

No inbox/API data is stored in the PWA offline cache. Data remains in the application database. Tables currently retain notifications and delivery history; add retention according to the organisation's policy when needed.

## Verification

Run `dotnet run --project tests/Notifications/NotificationTests.csproj`. This uses isolated SQLite storage with a narrowly scoped MySQL upsert adapter and a mocked HTTP push transport; it never reads the production database or sends external notifications. It checks recipient ownership, ACTIVE accounts, CSRF attributes, encrypted storage, VAPID round-trip, no-Telegram delivery, deduplication, outbox encryption/completion, expired subscriptions, account switching and logout revocation.

Browser verification covers bell counts/read-all, plain-text rendering of untrusted content, explicit permission/subscribe/disable, keyboard dismissal and widths 320–1440px. Real end-to-end reception on the deployed HTTPS domain and physical Android/iOS devices still needs to be verified after deployment and user consent.
