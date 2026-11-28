-- Run on the existing ProjectTracking MariaDB database before deploying grouped scan results.
ALTER TABLE `security_scan_findings`
    MODIFY COLUMN `Location` LONGTEXT NOT NULL;
