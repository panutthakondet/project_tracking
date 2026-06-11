ALTER TABLE `project_support_order`
  MODIFY COLUMN `status` varchar(20) NULL DEFAULT 'OPEN',
  MODIFY COLUMN `dev_status` varchar(20) NULL DEFAULT 'TODO';

UPDATE `project_support_order`
SET `status` = CASE
    WHEN `status` = 'WAIT_TEST' THEN 'FIXED'
    WHEN `status` = 'DONE' THEN 'PASS'
    WHEN `status` = 'CLOSE' THEN 'PASS'
    WHEN `status` = 'IN_PROGRESS' THEN 'WIP'
    WHEN `status` IS NULL OR `status` = '' THEN 'OPEN'
    ELSE `status`
END;

UPDATE `project_support_order`
SET `dev_status` = CASE
    WHEN `dev_status` = 'IN_PROGRESS' THEN 'WIP'
    WHEN `dev_status` IN ('TODO', 'DOING', 'BLOCK') THEN 'WIP'
    WHEN `dev_status` IS NULL OR `dev_status` = '' THEN 'WIP'
    ELSE `dev_status`
END;

ALTER TABLE `project_support_order`
  MODIFY COLUMN `status` varchar(20) NOT NULL DEFAULT 'OPEN',
  MODIFY COLUMN `dev_status` varchar(20) NOT NULL DEFAULT 'TODO';
