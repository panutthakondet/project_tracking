CREATE TABLE IF NOT EXISTS `employee_positions` (
  `position_id` INT NOT NULL AUTO_INCREMENT,
  `position_name` VARCHAR(150) NOT NULL,
  `sort_order` INT NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`position_id`),
  UNIQUE KEY `ux_employee_positions_name` (`position_name`),
  KEY `idx_employee_positions_active_sort` (`is_active`, `sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO `employee_positions`
    (`position_name`, `sort_order`, `is_active`)
SELECT DISTINCT TRIM(`position`), 100, 1
FROM `employee`
WHERE `position` IS NOT NULL
  AND TRIM(`position`) <> '';

ALTER TABLE `employee`
  ADD COLUMN `position_id` INT NULL AFTER `position`,
  ADD KEY `idx_employee_position_id` (`position_id`),
  ADD CONSTRAINT `fk_employee_position`
    FOREIGN KEY (`position_id`) REFERENCES `employee_positions` (`position_id`)
    ON UPDATE CASCADE ON DELETE SET NULL;

UPDATE `employee` AS employee_row
INNER JOIN `employee_positions` AS position_row
    ON position_row.`position_name` = TRIM(employee_row.`position`)
SET employee_row.`position_id` = position_row.`position_id`
WHERE employee_row.`position_id` IS NULL
  AND employee_row.`position` IS NOT NULL
  AND TRIM(employee_row.`position`) <> '';
