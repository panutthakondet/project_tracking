ALTER TABLE `ProjectIssues`
  ADD COLUMN `created_by` int(11) DEFAULT NULL AFTER `assign_to`,
  ADD KEY `idx_ProjectIssues_created_by` (`created_by`);

UPDATE `ProjectIssues`
SET `created_by` = `assign_to`
WHERE `created_by` IS NULL
  AND `assign_to` IS NOT NULL;

ALTER TABLE `ProjectIssues`
  ADD CONSTRAINT `FK_ProjectIssues_CreatedBy`
  FOREIGN KEY (`created_by`) REFERENCES `employee` (`emp_id`)
  ON DELETE SET NULL;
