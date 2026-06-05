UPDATE `meetings` m
LEFT JOIN `project` p
  ON p.`project_id` = m.`project_id`
LEFT JOIN (
  SELECT
    `meeting_id`,
    MIN(`user_id`) AS `first_attendee_emp_id`
  FROM `meeting_attendees`
  GROUP BY `meeting_id`
) a
  ON a.`meeting_id` = m.`id`
SET m.`created_by` = COALESCE(p.`ba_emp_id`, a.`first_attendee_emp_id`)
WHERE m.`created_by` IS NULL
  AND COALESCE(p.`ba_emp_id`, a.`first_attendee_emp_id`) IS NOT NULL;
