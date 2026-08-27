-- เพิ่มคอลัมน์สำหรับวันทำงานจริง และเติมข้อมูลเดิมจากวันตามแผน
ALTER TABLE `phase_assign`
    ADD COLUMN `actual_start` date NULL AFTER `plan_end`,
    ADD COLUMN `actual_end` date NULL AFTER `actual_start`;

UPDATE `phase_assign`
SET `actual_start` = `plan_start`,
    `actual_end` = `plan_end`;
