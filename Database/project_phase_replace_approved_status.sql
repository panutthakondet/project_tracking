UPDATE `project_phase`
SET `phase_status` = 'ส่งงวดงานแล้ว'
WHERE `phase_status` = 'อนุมัติจ่ายเงินแล้ว';

ALTER TABLE `project_phase`
  MODIFY COLUMN `phase_status` enum('กำลังดำเนินการ','ส่งงวดงานแล้ว') DEFAULT 'กำลังดำเนินการ';
