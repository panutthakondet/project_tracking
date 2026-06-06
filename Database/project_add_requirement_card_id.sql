ALTER TABLE `project`
  ADD COLUMN `requirement_card_id` int(11) DEFAULT NULL AFTER `entry_id`,
  ADD KEY `idx_project_requirement_card_id` (`requirement_card_id`),
  ADD CONSTRAINT `fk_project_requirement_card`
    FOREIGN KEY (`requirement_card_id`) REFERENCES `requirement_cards` (`card_id`)
    ON DELETE SET NULL;
