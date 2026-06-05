ALTER TABLE `login_user`
  ADD COLUMN `last_seen_at` datetime DEFAULT NULL AFTER `profile_image_path`;

CREATE INDEX `idx_login_user_last_seen_at`
  ON `login_user` (`last_seen_at`);
