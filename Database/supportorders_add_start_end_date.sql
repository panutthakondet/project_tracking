ALTER TABLE `project_support_order`
  ADD COLUMN `start_date` date DEFAULT NULL AFTER `due_date`,
  ADD COLUMN `end_date` date DEFAULT NULL AFTER `start_date`;

UPDATE `project_support_order`
SET `end_date` = `due_date`
WHERE `end_date` IS NULL
  AND `due_date` IS NOT NULL;

ALTER TABLE `project_support_order`
  DROP COLUMN `due_date`;
