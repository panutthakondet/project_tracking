-- Run after scan-workflow-schema.sql, in the existing ProjectTracking database.
-- All existing systems remain Formal. No systems, versions or old scans are deleted/moved.
SET @security_system_workflow_sql = IF(
 (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='security_systems' AND COLUMN_NAME='Workflow')=0,
 'ALTER TABLE `security_systems` ADD COLUMN `Workflow` varchar(20) NOT NULL DEFAULT ''Formal''',
 'SELECT ''System Workflow already exists''');
PREPARE security_system_workflow_stmt FROM @security_system_workflow_sql;
EXECUTE security_system_workflow_stmt;
DEALLOCATE PREPARE security_system_workflow_stmt;
