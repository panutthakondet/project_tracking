UPDATE `project_support_order`
SET `status` = CASE
    WHEN `status` IN ('OPEN', 'PASS', 'FAIL', 'REJECT') THEN `status`
    WHEN `status` IN ('WAIT_TEST', 'WIP', 'FIXED', 'IN_PROGRESS', 'TODO', 'DOING', 'BLOCK') THEN 'OPEN'
    WHEN `status` IN ('DONE', 'CLOSE', 'CLOSED', 'RESOLVED') THEN 'PASS'
    WHEN `status` IS NULL OR `status` = '' THEN 'OPEN'
    ELSE 'OPEN'
END;

UPDATE `project_support_order`
SET `dev_status` = CASE
    WHEN `dev_status` = 'FIXED' THEN 'FIXED'
    WHEN `dev_status` IN ('TODO', 'DOING', 'BLOCK', 'IN_PROGRESS', 'OPEN', 'FAIL', 'PASS', 'REJECT') THEN 'WIP'
    WHEN `dev_status` IS NULL OR `dev_status` = '' THEN 'WIP'
    ELSE 'WIP'
END;

ALTER TABLE `project_support_order`
  ADD COLUMN IF NOT EXISTS `is_reopen` tinyint(1) NOT NULL DEFAULT 0 AFTER `dev_detail`,
  ADD COLUMN IF NOT EXISTS `reopen_count` int NOT NULL DEFAULT 0 AFTER `is_reopen`;

UPDATE `project_support_order`
SET `is_reopen` = CASE
        WHEN COALESCE(`reopen_count`, 0) > 0 THEN 1
        ELSE COALESCE(`is_reopen`, 0)
    END,
    `reopen_count` = COALESCE(`reopen_count`, 0);

UPDATE `project_support_order`
SET `dev_status` = 'FIXED'
WHERE `status` = 'PASS'
  AND `dev_status` <> 'FIXED';

UPDATE `project_support_order`
SET `dev_status` = 'WIP'
WHERE `status` = 'FAIL'
  AND `dev_status` <> 'WIP';

ALTER TABLE `project_support_order`
  MODIFY COLUMN `status` varchar(20) NOT NULL DEFAULT 'OPEN',
  MODIFY COLUMN `dev_status` varchar(20) NOT NULL DEFAULT 'WIP';

CREATE TABLE IF NOT EXISTS `project_support_order_status_histories` (
  `id` int NOT NULL AUTO_INCREMENT,
  `order_id` int NOT NULL,
  `old_status` varchar(20) NULL,
  `new_status` varchar(20) NOT NULL DEFAULT 'OPEN',
  `is_reopen` tinyint(1) NOT NULL DEFAULT 0,
  `reopen_count` int NOT NULL DEFAULT 0,
  `changed_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `changed_by_emp_id` int NULL,
  PRIMARY KEY (`id`),
  KEY `IX_project_support_order_status_histories_order_id` (`order_id`),
  KEY `IX_project_support_order_status_histories_changed_at` (`changed_at`),
  KEY `IX_project_support_order_status_histories_order_id_changed_at` (`order_id`, `changed_at`),
  CONSTRAINT `FK_support_order_status_histories_order`
    FOREIGN KEY (`order_id`) REFERENCES `project_support_order` (`order_id`)
    ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `project_support_order_status_histories`
    (`order_id`, `old_status`, `new_status`, `is_reopen`, `reopen_count`, `changed_at`, `changed_by_emp_id`)
SELECT
    o.`order_id`,
    NULL,
    COALESCE(o.`status`, 'OPEN'),
    COALESCE(o.`is_reopen`, 0),
    COALESCE(o.`reopen_count`, 0),
    COALESCE(o.`created_at`, NOW()),
    o.`created_by`
FROM `project_support_order` o
WHERE NOT EXISTS (
    SELECT 1
    FROM `project_support_order_status_histories` h
    WHERE h.`order_id` = o.`order_id`
);

UPDATE `project_support_order` o
LEFT JOIN (
    SELECT `order_id`, COUNT(*) AS `fail_count`
    FROM `project_support_order_status_histories`
    WHERE `new_status` = 'FAIL'
    GROUP BY `order_id`
) h ON h.`order_id` = o.`order_id`
SET o.`reopen_count` = COALESCE(h.`fail_count`, 0),
    o.`is_reopen` = CASE WHEN COALESCE(h.`fail_count`, 0) > 0 THEN 1 ELSE 0 END;
