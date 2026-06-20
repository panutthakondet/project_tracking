ALTER TABLE `ProjectIssues`
  MODIFY COLUMN `IssueStatus` varchar(20) NULL DEFAULT 'OPEN',
  MODIFY COLUMN `DevStatus` varchar(20) NULL DEFAULT 'WIP';

UPDATE `ProjectIssues`
SET `IssueStatus` = CASE
    WHEN `IssueStatus` IN ('OPEN', 'PASS', 'FAIL', 'REJECT') THEN `IssueStatus`
    WHEN `IssueStatus` IN ('WAIT_TEST', 'WIP', 'FIXED', 'IN_PROGRESS', 'TODO', 'DOING', 'BLOCK') THEN 'OPEN'
    WHEN `IssueStatus` IN ('DONE', 'CLOSE', 'CLOSED', 'RESOLVED') THEN 'PASS'
    WHEN `IssueStatus` IS NULL OR `IssueStatus` = '' THEN 'OPEN'
    ELSE 'OPEN'
END;

UPDATE `ProjectIssues`
SET `DevStatus` = CASE
    WHEN `DevStatus` = 'FIXED' THEN 'FIXED'
    WHEN `DevStatus` IN ('TODO', 'DOING', 'BLOCK', 'IN_PROGRESS', 'OPEN', 'FAIL', 'PASS', 'REJECT') THEN 'WIP'
    WHEN `DevStatus` IS NULL OR `DevStatus` = '' THEN 'WIP'
    ELSE 'WIP'
END;

UPDATE `ProjectIssues`
SET `DevStatus` = 'FIXED'
WHERE `IssueStatus` = 'PASS'
  AND `DevStatus` <> 'FIXED';

UPDATE `ProjectIssues`
SET `DevStatus` = 'WIP'
WHERE `IssueStatus` = 'FAIL'
  AND `DevStatus` <> 'WIP';

INSERT INTO `ProjectIssueStatusHistories`
    (`IssueId`, `OldStatus`, `NewStatus`, `IsReopen`, `ReopenCount`, `ChangedAt`, `ChangedByEmpId`)
SELECT
    i.`IssueId`,
    NULL,
    COALESCE(i.`IssueStatus`, 'OPEN'),
    COALESCE(i.`IsReopen`, 0),
    COALESCE(i.`ReopenCount`, 0),
    COALESCE(i.`CreatedAt`, NOW()),
    i.`created_by`
FROM `ProjectIssues` i
WHERE NOT EXISTS (
    SELECT 1
    FROM `ProjectIssueStatusHistories` h
    WHERE h.`IssueId` = i.`IssueId`
);

UPDATE `ProjectIssues` i
LEFT JOIN (
    SELECT `IssueId`, COUNT(*) AS `fail_count`
    FROM `ProjectIssueStatusHistories`
    WHERE `NewStatus` = 'FAIL'
    GROUP BY `IssueId`
) h ON h.`IssueId` = i.`IssueId`
SET i.`ReopenCount` = COALESCE(h.`fail_count`, 0),
    i.`IsReopen` = CASE WHEN COALESCE(h.`fail_count`, 0) > 0 THEN 1 ELSE 0 END;

ALTER TABLE `ProjectIssues`
  MODIFY COLUMN `IssueStatus` varchar(20) NOT NULL DEFAULT 'OPEN',
  MODIFY COLUMN `DevStatus` varchar(20) NOT NULL DEFAULT 'WIP';
