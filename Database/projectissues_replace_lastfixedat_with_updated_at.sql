ALTER TABLE `ProjectIssues`
  ADD COLUMN `updated_at` datetime NULL DEFAULT NULL AFTER `CreatedAt`;

UPDATE `ProjectIssues`
SET `updated_at` = COALESCE(`LastFixedAt`, `CreatedAt`, NOW())
WHERE `updated_at` IS NULL;

ALTER TABLE `ProjectIssues`
  MODIFY COLUMN `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp();

ALTER TABLE `ProjectIssues`
  DROP COLUMN `LastFixedAt`;
