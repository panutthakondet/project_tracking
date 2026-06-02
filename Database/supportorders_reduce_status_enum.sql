UPDATE `project_support_order`
SET `status` = 'OPEN'
WHERE `status` IS NULL
   OR `status` = ''
   OR `status` = 'IN_PROGRESS';

UPDATE `project_support_order`
SET `status` = 'DONE'
WHERE `status` = 'CLOSE';

ALTER TABLE `project_support_order`
  MODIFY COLUMN `status` enum('OPEN','WAIT_TEST','DONE') DEFAULT 'OPEN';
