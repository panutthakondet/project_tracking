-- Clean duplicate notification rows per source/recipient.
-- Run this once on the server database after deploying the notification fix.

UPDATE user_notifications n
JOIN (
    SELECT
        source_type,
        source_id,
        recipient_emp_id,
        MAX(notification_id) AS keep_notification_id
    FROM user_notifications
    WHERE recipient_emp_id IS NOT NULL
    GROUP BY source_type, source_id, recipient_emp_id
    HAVING COUNT(*) > 1
) d
    ON d.source_type = n.source_type
    AND d.source_id = n.source_id
    AND d.recipient_emp_id = n.recipient_emp_id
SET
    n.is_resolved = 1,
    n.resolved_at = COALESCE(n.resolved_at, NOW()),
    n.updated_at = NOW()
WHERE n.notification_id <> d.keep_notification_id;
