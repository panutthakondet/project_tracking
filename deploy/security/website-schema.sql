-- Select the existing ProjectTracking database. Additive Website upgrade; no existing records changed.
CREATE TABLE IF NOT EXISTS `security_website_scan_parts` (`ScanId` bigint NOT NULL,`BatchKey` varchar(36) NOT NULL,`Section` varchar(24) NOT NULL,PRIMARY KEY (`ScanId`),UNIQUE KEY `ix_security_website_scan_parts_0` (`BatchKey`,`Section`)) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS `security_website_targets` (`Id` bigint NOT NULL AUTO_INCREMENT,`AuthProfile` varchar(80) NULL,`Enabled` tinyint(1) NOT NULL,`RequiresLogin` tinyint(1) NOT NULL,`Section` varchar(24) NOT NULL,`SystemId` bigint NOT NULL,`TargetUrl` varchar(2000) NOT NULL,PRIMARY KEY (`Id`),UNIQUE KEY `ix_security_website_targets_0` (`SystemId`,`Section`)) CHARACTER SET utf8mb4;
