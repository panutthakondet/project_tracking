-- Status master tables are intentionally separated by business owner.
-- Run once when deploying without allowing Program.cs to update the schema.

CREATE TABLE IF NOT EXISTS `project_status` (
  `status_id` INT NOT NULL AUTO_INCREMENT,
  `status_code` VARCHAR(50) NOT NULL,
  `status_desc` VARCHAR(100) NOT NULL,
  `sort_order` INT NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`status_id`),
  UNIQUE KEY `uq_project_status_code` (`status_code`),
  KEY `idx_project_status_active_sort` (`is_active`, `sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `project_phase_status` (
  `status_id` INT NOT NULL AUTO_INCREMENT,
  `status_code` VARCHAR(50) NOT NULL,
  `status_desc` VARCHAR(100) NOT NULL,
  `sort_order` INT NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`status_id`),
  UNIQUE KEY `uq_project_phase_status_code` (`status_code`),
  KEY `idx_project_phase_status_active_sort` (`is_active`, `sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `phase_assign_status` (
  `status_id` INT NOT NULL AUTO_INCREMENT,
  `status_code` VARCHAR(50) NOT NULL,
  `status_desc` VARCHAR(100) NOT NULL,
  `sort_order` INT NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`status_id`),
  UNIQUE KEY `uq_phase_assign_status_code` (`status_code`),
  KEY `idx_phase_assign_status_active_sort` (`is_active`, `sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `project_status` (`status_code`, `status_desc`, `sort_order`, `is_active`) VALUES
  ('PLAN', 'วางแผน', 10, 1),
  ('IN_PROGRESS', 'กำลังดำเนินการ', 20, 1),
  ('DONE', 'เสร็จสิ้น', 30, 1)
ON DUPLICATE KEY UPDATE
  `status_desc` = VALUES(`status_desc`),
  `sort_order` = VALUES(`sort_order`);

INSERT INTO `project_phase_status` (`status_code`, `status_desc`, `sort_order`, `is_active`) VALUES
  ('PLAN', 'วางแผน', 10, 1),
  ('IN_PROGRESS', 'กำลังดำเนินการ', 20, 1),
  ('SUBMITTED', 'ส่งงวดงานแล้ว', 30, 1)
ON DUPLICATE KEY UPDATE
  `status_desc` = VALUES(`status_desc`),
  `sort_order` = VALUES(`sort_order`);

INSERT INTO `phase_assign_status` (`status_code`, `status_desc`, `sort_order`, `is_active`) VALUES
  ('PLAN', 'วางแผน', 10, 1),
  ('IN_PROGRESS', 'กำลังดำเนินการ', 20, 1),
  ('DONE', 'เสร็จสิ้น', 30, 1)
ON DUPLICATE KEY UPDATE
  `status_desc` = VALUES(`status_desc`),
  `sort_order` = VALUES(`sort_order`);

-- The application startup performs the conditional ALTER/BACKFILL/FK steps,
-- because older installations may use ENUM columns or already have indexes.
