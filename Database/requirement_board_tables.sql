CREATE TABLE IF NOT EXISTS `requirement_board_columns` (
  `column_id` int(11) NOT NULL AUTO_INCREMENT,
  `column_name` varchar(150) NOT NULL,
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_by_user_id` int(11) DEFAULT NULL,
  `created_by_emp_id` int(11) DEFAULT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`column_id`),
  KEY `idx_requirement_columns_sort` (`sort_order`),
  KEY `idx_requirement_columns_created_by_user` (`created_by_user_id`),
  KEY `idx_requirement_columns_created_by_emp` (`created_by_emp_id`),
  CONSTRAINT `fk_requirement_columns_user` FOREIGN KEY (`created_by_user_id`) REFERENCES `login_user` (`user_id`) ON DELETE SET NULL,
  CONSTRAINT `fk_requirement_columns_emp` FOREIGN KEY (`created_by_emp_id`) REFERENCES `employee` (`emp_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

CREATE TABLE IF NOT EXISTS `requirement_cards` (
  `card_id` int(11) NOT NULL AUTO_INCREMENT,
  `column_id` int(11) NOT NULL,
  `title` varchar(255) NOT NULL,
  `detail` text DEFAULT NULL,
  `cover_image_path` varchar(500) DEFAULT NULL,
  `cover_image_name` varchar(255) DEFAULT NULL,
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `is_archived` tinyint(1) NOT NULL DEFAULT 0,
  `created_by_user_id` int(11) DEFAULT NULL,
  `created_by_emp_id` int(11) DEFAULT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`card_id`),
  KEY `idx_requirement_cards_column_sort` (`column_id`,`sort_order`),
  KEY `idx_requirement_cards_created_by_user` (`created_by_user_id`),
  KEY `idx_requirement_cards_created_by_emp` (`created_by_emp_id`),
  CONSTRAINT `fk_requirement_cards_column` FOREIGN KEY (`column_id`) REFERENCES `requirement_board_columns` (`column_id`),
  CONSTRAINT `fk_requirement_cards_user` FOREIGN KEY (`created_by_user_id`) REFERENCES `login_user` (`user_id`) ON DELETE SET NULL,
  CONSTRAINT `fk_requirement_cards_emp` FOREIGN KEY (`created_by_emp_id`) REFERENCES `employee` (`emp_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

CREATE TABLE IF NOT EXISTS `requirement_card_attachments` (
  `attachment_id` int(11) NOT NULL AUTO_INCREMENT,
  `card_id` int(11) NOT NULL,
  `file_name` varchar(255) NOT NULL,
  `stored_file_name` varchar(255) NOT NULL,
  `file_path` varchar(500) NOT NULL,
  `content_type` varchar(150) DEFAULT NULL,
  `file_size` bigint DEFAULT 0,
  `uploaded_by_user_id` int(11) DEFAULT NULL,
  `uploaded_by_emp_id` int(11) DEFAULT NULL,
  `uploaded_at` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`attachment_id`),
  KEY `idx_requirement_attachments_card` (`card_id`),
  KEY `idx_requirement_attachments_user` (`uploaded_by_user_id`),
  KEY `idx_requirement_attachments_emp` (`uploaded_by_emp_id`),
  CONSTRAINT `fk_requirement_attachments_card` FOREIGN KEY (`card_id`) REFERENCES `requirement_cards` (`card_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_requirement_attachments_user` FOREIGN KEY (`uploaded_by_user_id`) REFERENCES `login_user` (`user_id`) ON DELETE SET NULL,
  CONSTRAINT `fk_requirement_attachments_emp` FOREIGN KEY (`uploaded_by_emp_id`) REFERENCES `employee` (`emp_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

INSERT INTO `requirement_board_columns` (`column_name`, `sort_order`)
SELECT 'To Do', 1
WHERE NOT EXISTS (
  SELECT 1 FROM `requirement_board_columns` WHERE `column_name` = 'To Do'
);

INSERT INTO `requirement_board_columns` (`column_name`, `sort_order`)
SELECT 'Complete', 2
WHERE NOT EXISTS (
  SELECT 1 FROM `requirement_board_columns` WHERE `column_name` = 'Complete'
);
