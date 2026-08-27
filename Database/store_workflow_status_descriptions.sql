-- Normalize the three workflow tables so their legacy text columns store
-- the Thai description from their own status master table.
START TRANSACTION;

UPDATE `project` target
INNER JOIN `project_status` status_master
    ON status_master.`status_id` = target.`status_id`
SET target.`status_id` = status_master.`status_id`,
    target.`status` = status_master.`status_desc`;

UPDATE `project` target
INNER JOIN `project_status` status_master
    ON UPPER(TRIM(status_master.`status_code`)) = UPPER(TRIM(target.`status`))
    OR UPPER(TRIM(status_master.`status_desc`)) = UPPER(TRIM(target.`status`))
SET target.`status_id` = status_master.`status_id`,
    target.`status` = status_master.`status_desc`
WHERE target.`status_id` IS NULL;

UPDATE `project_phase` target
INNER JOIN `project_phase_status` status_master
    ON status_master.`status_id` = target.`status_id`
SET target.`status_id` = status_master.`status_id`,
    target.`phase_status` = status_master.`status_desc`;

UPDATE `project_phase` target
INNER JOIN `project_phase_status` status_master
    ON UPPER(TRIM(status_master.`status_code`)) = UPPER(TRIM(target.`phase_status`))
    OR UPPER(TRIM(status_master.`status_desc`)) = UPPER(TRIM(target.`phase_status`))
SET target.`status_id` = status_master.`status_id`,
    target.`phase_status` = status_master.`status_desc`
WHERE target.`status_id` IS NULL;

UPDATE `phase_assign` target
INNER JOIN `phase_assign_status` status_master
    ON status_master.`status_id` = target.`status_id`
SET target.`status_id` = status_master.`status_id`,
    target.`work_status` = status_master.`status_desc`;

UPDATE `phase_assign` target
INNER JOIN `phase_assign_status` status_master
    ON UPPER(TRIM(status_master.`status_code`)) = UPPER(TRIM(target.`work_status`))
    OR UPPER(TRIM(status_master.`status_desc`)) = UPPER(TRIM(target.`work_status`))
SET target.`status_id` = status_master.`status_id`,
    target.`work_status` = status_master.`status_desc`
WHERE target.`status_id` IS NULL;

UPDATE `requirement_card_phase_items` draft
INNER JOIN `project_phase_status` in_progress
    ON in_progress.`status_code` = 'IN_PROGRESS'
SET draft.`phase_status` = in_progress.`status_desc`
WHERE UPPER(TRIM(COALESCE(draft.`phase_status`, ''))) = 'PLAN'
   OR TRIM(COALESCE(draft.`phase_status`, '')) = 'วางแผน'
   OR TRIM(COALESCE(draft.`phase_status`, '')) = '';

UPDATE `requirement_card_phase_items` draft
INNER JOIN `project_phase_status` status_master
    ON UPPER(TRIM(status_master.`status_code`)) = UPPER(TRIM(draft.`phase_status`))
    OR UPPER(TRIM(status_master.`status_desc`)) = UPPER(TRIM(draft.`phase_status`))
SET draft.`phase_status` = status_master.`status_desc`;

ALTER TABLE `project` ALTER COLUMN `status` SET DEFAULT 'กำลังดำเนินการ';
ALTER TABLE `project_phase` ALTER COLUMN `phase_status` SET DEFAULT 'กำลังดำเนินการ';
ALTER TABLE `phase_assign` ALTER COLUMN `work_status` SET DEFAULT 'กำลังดำเนินการ';

COMMIT;
