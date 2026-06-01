CREATE TABLE IF NOT EXISTS `system_update_announcements` (
  `update_id` int(11) NOT NULL AUTO_INCREMENT,
  `version` varchar(50) DEFAULT NULL,
  `title` varchar(255) NOT NULL,
  `summary` varchar(500) DEFAULT NULL,
  `details` text DEFAULT NULL,
  `published_at` datetime NOT NULL DEFAULT current_timestamp(),
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`update_id`),
  KEY `idx_system_update_active_published` (`is_active`, `published_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

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
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

-- Example announcement:
-- INSERT INTO `system_update_announcements` (`version`, `title`, `summary`, `details`, `published_at`, `is_active`)
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
--   1
-- );
