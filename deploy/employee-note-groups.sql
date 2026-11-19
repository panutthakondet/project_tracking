-- Run once on an existing database if the application cannot alter its schema.
ALTER TABLE `employee_notes`
  ADD COLUMN `group_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL;
