CREATE TABLE IF NOT EXISTS project_sync_runs (
    Id INT NOT NULL AUTO_INCREMENT,
    ProjectId INT NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    ComparedAtUtc DATETIME(6) NULL,
    StructureJson LONGTEXT NOT NULL,
    PlanJson LONGTEXT NULL,
    ResultJson LONGTEXT NULL,
    PRIMARY KEY (Id),
    KEY ix_project_sync_runs_project_id (ProjectId, Id),
    CONSTRAINT fk_project_sync_runs_project FOREIGN KEY (ProjectId)
        REFERENCES project (project_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
