CREATE TABLE `cnt_m_coop` (
  `coop_id` int(11) NOT NULL AUTO_INCREMENT,
  `coop_name` varchar(255) NOT NULL,
  PRIMARY KEY (`coop_id`),
  KEY `idx_cnt_m_coop_name` (`coop_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

ALTER TABLE `project`
  ADD COLUMN `coop_id` int(11) DEFAULT NULL AFTER `project_id`,
  ADD KEY `idx_project_coop_id` (`coop_id`),
  ADD CONSTRAINT `fk_project_coop`
    FOREIGN KEY (`coop_id`) REFERENCES `cnt_m_coop` (`coop_id`)
    ON DELETE SET NULL;
