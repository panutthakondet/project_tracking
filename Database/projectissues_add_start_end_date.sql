ALTER TABLE `ProjectIssues`
  ADD COLUMN `StartDate` date DEFAULT NULL AFTER `IssuePriority`,
  ADD COLUMN `EndDate` date DEFAULT NULL AFTER `StartDate`;
