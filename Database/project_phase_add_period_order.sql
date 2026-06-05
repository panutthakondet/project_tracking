ALTER TABLE `project_phase`
  ADD COLUMN `period_order` int(11) NOT NULL DEFAULT 1 AFTER `phase_order`;

UPDATE `project_phase`
SET `period_order` = `phase_order`;

UPDATE `project_phase`
SET `phase_order` = 1;

UPDATE `phase_assign` pa
JOIN `project_phase` pp ON pp.`phase_id` = pa.`phase_id`
SET pa.`phase_order` = pp.`phase_order`;

ALTER TABLE `project_phase`
  ADD INDEX `ix_project_phase_part_period_sort`
    (`project_id`, `phase_order`, `period_order`, `phase_sort`);
