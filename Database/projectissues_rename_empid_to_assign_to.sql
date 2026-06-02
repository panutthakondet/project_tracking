ALTER TABLE `ProjectIssues`
  DROP FOREIGN KEY `FK_ProjectIssues_Employee`;

ALTER TABLE `ProjectIssues`
  DROP INDEX `FK_ProjectIssues_Employee`;

ALTER TABLE `ProjectIssues`
  CHANGE COLUMN `EmpId` `assign_to` int(11) NOT NULL;

ALTER TABLE `ProjectIssues`
  ADD KEY `idx_ProjectIssues_assign_to` (`assign_to`);

ALTER TABLE `ProjectIssues`
  ADD CONSTRAINT `FK_ProjectIssues_AssignTo`
  FOREIGN KEY (`assign_to`) REFERENCES `employee` (`emp_id`);
