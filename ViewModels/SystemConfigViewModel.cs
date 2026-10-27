namespace ProjectTracking.ViewModels
{
    public class SystemConfigPageViewModel
    {
        public List<SystemConfigItemViewModel> Items { get; set; } = new();

        public int TotalCount => Items.Count;
    }

    public class SystemConfigItemViewModel
    {
        public string ConfigKey { get; set; } = "";
        public string ConfigValue { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
    }

    public class SystemConfigInputViewModel
    {
        public string ConfigKey { get; set; } = "";
        public string ConfigValue { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class SystemConfigPostViewModel
    {
        public List<SystemConfigInputViewModel> Items { get; set; } = new();
    }

    public class SystemConfigMasterDataViewModel
    {
        public string ActiveSection { get; set; } = "positions";
        public List<EmployeePositionConfigViewModel> Positions { get; set; } = new();
        public List<ProjectDepartmentConfigViewModel> Departments { get; set; } = new();
        public List<DocumentTypeConfigViewModel> EmployeeDocumentTypes { get; set; } = new();
        public List<DocumentTypeConfigViewModel> ProjectDocumentTypes { get; set; } = new();
        public List<SystemUpdateAnnouncementConfigViewModel> Announcements { get; set; } = new();
    }

    public class EmployeePositionConfigViewModel
    {
        public int PositionId { get; set; }
        public string PositionName { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int EmployeeCount { get; set; }
    }

    public class ProjectDepartmentConfigViewModel
    {
        public int DepartmentId { get; set; }
        public string DepartmentCode { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int EmployeeCount { get; set; }
        public int ProjectCount { get; set; }
    }

    public class DocumentTypeConfigViewModel
    {
        public int DocumentTypeId { get; set; }
        public string DocumentTypeCode { get; set; } = "";
        public string DocumentTypeName { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int DocumentCount { get; set; }
    }

    public class SystemUpdateAnnouncementConfigViewModel
    {
        public int UpdateId { get; set; }
        public string Version { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Details { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int MaxDisplaysPerDay { get; set; }
        public bool IsActive { get; set; }
        public List<int> DepartmentIds { get; set; } = new();
        public string DepartmentNames { get; set; } = "ทุกฝ่าย";
    }
}
