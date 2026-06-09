CREATE TABLE IF NOT EXISTS `requirement_board_labels` (
  `label_id` int(11) NOT NULL AUTO_INCREMENT,
  `label_name` varchar(100) NOT NULL,
  `color_hex` varchar(20) NOT NULL DEFAULT '#22c7b8',
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_by_user_id` int(11) DEFAULT NULL,
  `created_by_emp_id` int(11) DEFAULT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`label_id`),
  KEY `idx_requirement_labels_active_sort` (`is_active`,`sort_order`),
  KEY `idx_requirement_labels_created_by_user` (`created_by_user_id`),
  KEY `idx_requirement_labels_created_by_emp` (`created_by_emp_id`),
  CONSTRAINT `fk_requirement_labels_user`
    FOREIGN KEY (`created_by_user_id`) REFERENCES `login_user` (`user_id`) ON DELETE SET NULL,
  CONSTRAINT `fk_requirement_labels_emp`
    FOREIGN KEY (`created_by_emp_id`) REFERENCES `employee` (`emp_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

CREATE TABLE IF NOT EXISTS `requirement_card_labels` (
  `card_id` int(11) NOT NULL,
  `label_id` int(11) NOT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`card_id`,`label_id`),
  KEY `idx_requirement_card_labels_label` (`label_id`),
  CONSTRAINT `fk_requirement_card_labels_card`
    FOREIGN KEY (`card_id`) REFERENCES `requirement_cards` (`card_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_requirement_card_labels_label`
    FOREIGN KEY (`label_id`) REFERENCES `requirement_board_labels` (`label_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'งวด : FollowME', '#52cc99', 1
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'งวด : FollowME');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'Wait Contract', '#f5d328', 2
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'Wait Contract');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'งวด : BRD', '#f5d328', 3
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'งวด : BRD');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'งวด : Web Information', '#f59e0b', 4
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'งวด : Web Information');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'Late นัดพี่ไปร์ท', '#f87171', 5
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'Late นัดพี่ไปร์ท');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'งวด Web Member', '#f87171', 6
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'งวด Web Member');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'Forced Stop Working', '#d2352d', 7
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'Forced Stop Working');

INSERT INTO `requirement_board_labels` (`label_name`, `color_hex`, `sort_order`)
SELECT 'Wait Keyin Contract', '#c77af2', 8
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `requirement_board_labels` WHERE `label_name` = 'Wait Keyin Contract');
