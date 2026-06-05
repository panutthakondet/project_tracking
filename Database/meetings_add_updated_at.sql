ALTER TABLE `meetings`
  ADD COLUMN `updated_at` datetime DEFAULT NULL
  AFTER `created_at`;

UPDATE `meetings`
SET `updated_at` = COALESCE(`created_at`, NOW())
WHERE `updated_at` IS NULL;

ALTER TABLE `meetings`
  MODIFY COLUMN `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp();
