CREATE TABLE IF NOT EXISTS `system_update_announcements` (
  `update_id` int(11) NOT NULL AUTO_INCREMENT,
  `version` varchar(50) DEFAULT NULL,
  `title` varchar(255) NOT NULL,
  `summary` varchar(500) DEFAULT NULL,
  `details` text DEFAULT NULL,
  `published_at` datetime NOT NULL DEFAULT current_timestamp(),
  `start_date` date NOT NULL,
  `end_date` date DEFAULT NULL,
  `max_displays_per_day` int(11) NOT NULL DEFAULT 1,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`update_id`),
  KEY `idx_system_update_active_published` (`is_active`, `published_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Upgrade existing installations so title/summary/details can store emoji.
ALTER TABLE `system_update_announcements`
  CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `system_update_announcement_departments` (
  `update_id` int(11) NOT NULL,
  `department_id` int(11) NOT NULL,
  PRIMARY KEY (`update_id`, `department_id`),
  KEY `idx_system_update_announcement_departments_department` (`department_id`),
  CONSTRAINT `fk_system_update_announcement_departments_update`
    FOREIGN KEY (`update_id`) REFERENCES `system_update_announcements` (`update_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_system_update_announcement_departments_department`
    FOREIGN KEY (`department_id`) REFERENCES `project_departments` (`department_id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `system_update_displays` (
  `display_id` int(11) NOT NULL AUTO_INCREMENT,
  `update_id` int(11) NOT NULL,
  `user_id` int(11) NOT NULL,
  `displayed_on` date NOT NULL,
  `display_count` int(11) NOT NULL DEFAULT 0,
  `last_displayed_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`display_id`),
  UNIQUE KEY `uq_system_update_displays_daily` (`update_id`, `user_id`, `displayed_on`),
  KEY `idx_system_update_displays_user_date` (`user_id`, `displayed_on`),
  CONSTRAINT `fk_system_update_displays_update`
    FOREIGN KEY (`update_id`) REFERENCES `system_update_announcements` (`update_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_system_update_displays_user`
    FOREIGN KEY (`user_id`) REFERENCES `login_user` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `system_update_reads` (
  `read_id` int(11) NOT NULL AUTO_INCREMENT,
  `update_id` int(11) NOT NULL,
  `user_id` int(11) NOT NULL,
  `read_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`read_id`),
  UNIQUE KEY `uq_system_update_reads_update_user` (`update_id`, `user_id`),
  KEY `idx_system_update_reads_user` (`user_id`),
  CONSTRAINT `fk_system_update_reads_announcement`
    FOREIGN KEY (`update_id`) REFERENCES `system_update_announcements` (`update_id`)
    ON DELETE CASCADE,
  CONSTRAINT `fk_system_update_reads_user`
    FOREIGN KEY (`user_id`) REFERENCES `login_user` (`user_id`)
    ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Example announcement:
-- INSERT INTO `system_update_announcements`
--   (`version`, `title`, `summary`, `details`, `published_at`, `start_date`, `end_date`, `max_displays_per_day`, `is_active`)
-- VALUES (
--   'v1.1.0',
--   'อัปเดตระบบติดตามโครงการ',
--   'เพิ่มหน้ารายงานและปรับปรุงหน้าจอให้ใช้งานง่ายขึ้น',
--   CONCAT(
--     'เพิ่ม Reports Center', CHAR(10),
--     'ปรับ Dashboard ให้แสดงข้อมูลผู้บริหารชัดขึ้น', CHAR(10),
--     'ปรับหน้า Issues และ Support เป็นรูปแบบการ์ด'
--   ),
--   NOW(),
--   CURDATE(),
--   DATE_ADD(CURDATE(), INTERVAL 7 DAY),
--   1,
--   1
-- );
