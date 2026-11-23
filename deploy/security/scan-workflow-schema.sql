-- Select the ProjectTracking database before running. Existing rounds remain Formal.
SET @security_workflow_sql = IF(
 (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='security_scans' AND COLUMN_NAME='Workflow')=0,
 'ALTER TABLE `security_scans` ADD COLUMN `Workflow` varchar(20) NOT NULL DEFAULT ''Formal''',
 'SELECT ''Workflow already exists''');
PREPARE security_workflow_stmt FROM @security_workflow_sql;
EXECUTE security_workflow_stmt;
DEALLOCATE PREPARE security_workflow_stmt;
