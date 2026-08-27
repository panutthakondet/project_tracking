-- Replace the retired PLAN/วางแผน status before removing it from each master.
START TRANSACTION;

UPDATE `project` source_row
INNER JOIN `project_status` in_progress ON in_progress.`status_code` = 'IN_PROGRESS'
LEFT JOIN `project_status` current_status ON current_status.`status_id` = source_row.`status_id`
SET source_row.`status_id` = in_progress.`status_id`,
    source_row.`status` = in_progress.`status_code`
WHERE UPPER(TRIM(COALESCE(source_row.`status`, ''))) = 'PLAN'
   OR TRIM(COALESCE(source_row.`status`, '')) = 'วางแผน'
   OR UPPER(TRIM(COALESCE(current_status.`status_code`, ''))) = 'PLAN'
   OR TRIM(COALESCE(current_status.`status_desc`, '')) = 'วางแผน';

UPDATE `project_phase` source_row
INNER JOIN `project_phase_status` in_progress ON in_progress.`status_code` = 'IN_PROGRESS'
LEFT JOIN `project_phase_status` current_status ON current_status.`status_id` = source_row.`status_id`
SET source_row.`status_id` = in_progress.`status_id`,
    source_row.`phase_status` = in_progress.`status_desc`
WHERE UPPER(TRIM(COALESCE(source_row.`phase_status`, ''))) = 'PLAN'
   OR TRIM(COALESCE(source_row.`phase_status`, '')) = 'วางแผน'
   OR UPPER(TRIM(COALESCE(current_status.`status_code`, ''))) = 'PLAN'
   OR TRIM(COALESCE(current_status.`status_desc`, '')) = 'วางแผน';

UPDATE `phase_assign` source_row
INNER JOIN `phase_assign_status` in_progress ON in_progress.`status_code` = 'IN_PROGRESS'
LEFT JOIN `phase_assign_status` current_status ON current_status.`status_id` = source_row.`status_id`
SET source_row.`status_id` = in_progress.`status_id`,
    source_row.`work_status` = in_progress.`status_code`
WHERE UPPER(TRIM(COALESCE(source_row.`work_status`, ''))) = 'PLAN'
   OR TRIM(COALESCE(source_row.`work_status`, '')) = 'วางแผน'
   OR UPPER(TRIM(COALESCE(current_status.`status_code`, ''))) = 'PLAN'
   OR TRIM(COALESCE(current_status.`status_desc`, '')) = 'วางแผน';

UPDATE `status_approval_requests`
SET `current_status` = CASE
        WHEN `target_type` = 'PROJECT_PHASE' THEN 'กำลังดำเนินการ'
        ELSE 'IN_PROGRESS'
    END
WHERE UPPER(TRIM(COALESCE(`current_status`, ''))) = 'PLAN'
   OR TRIM(COALESCE(`current_status`, '')) = 'วางแผน';

UPDATE `status_approval_requests`
SET `requested_status` = CASE
        WHEN `target_type` = 'PROJECT_PHASE' THEN 'กำลังดำเนินการ'
        ELSE 'IN_PROGRESS'
    END
WHERE UPPER(TRIM(COALESCE(`requested_status`, ''))) = 'PLAN'
   OR TRIM(COALESCE(`requested_status`, '')) = 'วางแผน';

DELETE FROM `project_status`
WHERE UPPER(TRIM(`status_code`)) = 'PLAN' OR TRIM(`status_desc`) = 'วางแผน';

DELETE FROM `project_phase_status`
WHERE UPPER(TRIM(`status_code`)) = 'PLAN' OR TRIM(`status_desc`) = 'วางแผน';

DELETE FROM `phase_assign_status`
WHERE UPPER(TRIM(`status_code`)) = 'PLAN' OR TRIM(`status_desc`) = 'วางแผน';

COMMIT;

ALTER TABLE `project` ALTER COLUMN `status` SET DEFAULT 'IN_PROGRESS';
ALTER TABLE `project_phase` ALTER COLUMN `phase_status` SET DEFAULT 'กำลังดำเนินการ';
ALTER TABLE `phase_assign` ALTER COLUMN `work_status` SET DEFAULT 'IN_PROGRESS';
