CREATE TABLE `requirement_card_phase_items` (
  `item_id` int(11) NOT NULL AUTO_INCREMENT,
  `card_id` int(11) NOT NULL,
  `phase_name` varchar(500) NOT NULL,
  `phase_type` varchar(20) NOT NULL DEFAULT 'MAIN',
  `phase_order` int(11) NOT NULL DEFAULT 1,
  `period_order` int(11) NOT NULL DEFAULT 1,
  `phase_sort` int(11) NOT NULL DEFAULT 0,
  `phase_status` varchar(50) DEFAULT 'วางแผน',
  `plan_start` date DEFAULT NULL,
  `plan_end` date DEFAULT NULL,
  `period_end_date` date DEFAULT NULL,
  `created_by_user_id` int(11) DEFAULT NULL,
  `created_by_emp_id` int(11) DEFAULT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`item_id`),
  KEY `idx_requirement_card_phase_card_sort` (`card_id`,`phase_sort`),
  CONSTRAINT `fk_requirement_card_phase_card`
    FOREIGN KEY (`card_id`) REFERENCES `requirement_cards` (`card_id`)
    ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;
