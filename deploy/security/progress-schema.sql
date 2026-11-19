-- Select the existing ProjectTracking database before running. Creates one new table only.
CREATE TABLE IF NOT EXISTS `security_scan_progress` (`ScanId` bigint NOT NULL,`BytesDone` bigint NULL,`BytesTotal` bigint NULL,`Percent` int NULL,`Stage` varchar(40) NOT NULL,`UpdatedAt` datetime(6) NOT NULL,PRIMARY KEY (`ScanId`)) CHARACTER SET utf8mb4;
