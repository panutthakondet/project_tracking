ALTER TABLE `requirement_cards`
  ADD COLUMN `cover_image_path` varchar(500) DEFAULT NULL AFTER `detail`,
  ADD COLUMN `cover_image_name` varchar(255) DEFAULT NULL AFTER `cover_image_path`;
