-- Run once before deploying recurrence support. Keep existing links and Issues.
-- Replaces the original unique FindingId index with a unique FindingId/IssueId pair.
ALTER TABLE `security_finding_issue_links`
    DROP INDEX `ix_security_finding_issue_links_0`,
    ADD UNIQUE KEY `ix_security_finding_issue_links_0` (`FindingId`, `IssueId`);
