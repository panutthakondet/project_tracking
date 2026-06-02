UPDATE `project_support_order`
SET `created_by` = `assign_to`
WHERE `created_by` IS NULL
  AND `assign_to` IS NOT NULL;
