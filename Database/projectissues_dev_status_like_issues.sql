UPDATE `ProjectIssues`
SET `DevStatus` = CASE
    WHEN `DevStatus` IN ('TODO', 'DOING', 'BLOCK') THEN 'WIP'
    WHEN `DevStatus` IS NULL OR `DevStatus` = '' THEN 'WIP'
    ELSE `DevStatus`
END;
