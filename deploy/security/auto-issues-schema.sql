-- Select existing ProjectTracking database. Additive table; existing systems remain manual until configured.
CREATE TABLE IF NOT EXISTS `security_issue_defaults` (`SystemId` bigint NOT NULL,`AssignedEmpId` int NOT NULL,`CreatedBy` int NOT NULL,`CreatorEmpId` int NULL,`EndDate` datetime(6) NOT NULL,`StartDate` datetime(6) NOT NULL,PRIMARY KEY (`SystemId`)) CHARACTER SET utf8mb4;
