-- Generated from SecurityModelConfiguration. New tables only.
CREATE TABLE IF NOT EXISTS `security_artifacts` (`Id` bigint NOT NULL AUTO_INCREMENT,`OriginalName` varchar(255) NOT NULL,`Sha256` varchar(64) NOT NULL,`Size` bigint NOT NULL,`StorageKey` varchar(100) NOT NULL,`VersionId` bigint NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_artifacts_0` (`VersionId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_findings` (`Id` bigint NOT NULL AUTO_INCREMENT,`AssignedEmpId` int NULL,`DueDate` datetime(6) NULL,`Fingerprint` varchar(64) NOT NULL,`FirstSeenAt` datetime(6) NOT NULL,`LastSeenAt` datetime(6) NOT NULL,`ReviewStatus` varchar(30) NOT NULL,`SystemId` bigint NOT NULL,`Title` varchar(500) NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_findings_0` (`SystemId`,`Fingerprint`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_finding_history` (`Id` bigint NOT NULL AUTO_INCREMENT,`CreatedAt` datetime(6) NOT NULL,`CreatedBy` int NOT NULL,`FindingId` bigint NOT NULL,`Note` varchar(4000) NOT NULL,`Status` varchar(30) NOT NULL,PRIMARY KEY (`Id`),KEY `ix_security_finding_history_0` (`FindingId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_finding_issue_links` (`Id` bigint NOT NULL AUTO_INCREMENT,`CreatedAt` datetime(6) NOT NULL,`CreatedBy` int NOT NULL,`FindingId` bigint NOT NULL,`IssueId` int NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_finding_issue_links_0` (`FindingId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_host_approvals` (`Id` bigint NOT NULL AUTO_INCREMENT,`Host` varchar(253) NOT NULL,`IsApproved` tinyint(1) NOT NULL,`ProjectId` int NOT NULL,`UpdatedAt` datetime(6) NOT NULL,`UpdatedBy` int NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_host_approvals_0` (`ProjectId`,`Host`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_issue_defaults` (`SystemId` bigint NOT NULL,`AssignedEmpId` int NOT NULL,`CreatedBy` int NOT NULL,`CreatorEmpId` int NULL,`EndDate` datetime(6) NOT NULL,`StartDate` datetime(6) NOT NULL,PRIMARY KEY (`SystemId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_reports` (`Id` bigint NOT NULL AUTO_INCREMENT,`CreatedAt` datetime(6) NOT NULL,`Format` varchar(20) NOT NULL,`ScanId` bigint NOT NULL,`Sha256` varchar(64) NOT NULL,`Size` bigint NOT NULL,`StorageKey` varchar(100) NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_reports_0` (`ScanId`,`Format`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_scans` (`Id` bigint NOT NULL AUTO_INCREMENT,`AuthProfile` varchar(80) NULL,`Coverage` varchar(2000) NULL,`CreatedAt` datetime(6) NOT NULL,`CreatedBy` int NOT NULL,`Engine` varchar(20) NOT NULL,`EngineVersion` varchar(200) NULL,`Error` varchar(2000) NULL,`FinishedAt` datetime(6) NULL,`LeaseToken` varchar(64) NULL,`LeaseUntil` datetime(6) NULL,`Mode` varchar(20) NOT NULL,`RequestKey` varchar(36) NOT NULL,`RequiresLogin` tinyint(1) NOT NULL,`StartedAt` datetime(6) NULL,`Status` varchar(20) NOT NULL,`TargetUrl` varchar(2000) NULL,`VersionId` bigint NOT NULL,`Workflow` varchar(20) NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_scans_0` (`RequestKey`),KEY `ix_security_scans_1` (`VersionId`),KEY `ix_security_scans_2` (`Status`,`CreatedAt`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_scan_findings` (`Id` bigint NOT NULL AUTO_INCREMENT,`Cwe` varchar(100) NULL,`Description` longtext NOT NULL,`Evidence` longtext NOT NULL,`FindingId` bigint NOT NULL,`Location` varchar(4000) NOT NULL,`References` longtext NOT NULL,`Remediation` longtext NOT NULL,`RuleId` varchar(200) NOT NULL,`ScanId` bigint NOT NULL,`Severity` varchar(20) NOT NULL,`Title` varchar(500) NOT NULL,PRIMARY KEY (`Id`),KEY `ix_security_scan_findings_0` (`FindingId`),UNIQUE KEY `ix_security_scan_findings_1` (`ScanId`,`FindingId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_scan_progress` (`ScanId` bigint NOT NULL,`BytesDone` bigint NULL,`BytesTotal` bigint NULL,`Percent` int NULL,`Stage` varchar(40) NOT NULL,`UpdatedAt` datetime(6) NOT NULL,PRIMARY KEY (`ScanId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_systems` (`Id` bigint NOT NULL AUTO_INCREMENT,`AuthProfile` varchar(80) NULL,`CreatedAt` datetime(6) NOT NULL,`CreatedBy` int NOT NULL,`Kind` varchar(24) NOT NULL,`Name` varchar(160) NOT NULL,`ProjectId` int NOT NULL,`RequiresLogin` tinyint(1) NOT NULL,`TargetUrl` varchar(2000) NULL,PRIMARY KEY (`Id`),KEY `ix_security_systems_0` (`ProjectId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_versions` (`Id` bigint NOT NULL AUTO_INCREMENT,`BuildNumber` varchar(100) NULL,`ChangeNotes` varchar(4000) NULL,`CreatedAt` datetime(6) NOT NULL,`CreatedBy` int NOT NULL,`Environment` varchar(20) NOT NULL,`GitCommit` varchar(64) NULL,`SystemId` bigint NOT NULL,`VersionName` varchar(100) NOT NULL,PRIMARY KEY (`Id`),KEY `ix_security_versions_0` (`SystemId`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_website_scan_parts` (`ScanId` bigint NOT NULL,`BatchKey` varchar(36) NOT NULL,`Section` varchar(24) NOT NULL,PRIMARY KEY (`ScanId`),UNIQUE KEY `ix_security_website_scan_parts_0` (`BatchKey`,`Section`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_website_targets` (`Id` bigint NOT NULL AUTO_INCREMENT,`AuthProfile` varchar(80) NULL,`Enabled` tinyint(1) NOT NULL,`RequiresLogin` tinyint(1) NOT NULL,`Section` varchar(24) NOT NULL,`SystemId` bigint NOT NULL,`TargetUrl` varchar(2000) NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_website_targets_0` (`SystemId`,`Section`)) CHARACTER SET utf8mb4;
