using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectTracking.ViewModels;

public class FieldServiceFormViewModel
{
    public int VisitId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกสหกรณ์")]
    [Display(Name = "สหกรณ์")]
    public int CoopId { get; set; }

    [Required(ErrorMessage = "กรุณาระบุชื่องาน")]
    [StringLength(200)]
    [Display(Name = "ชื่องาน")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "ประเภทบริการ")]
    public string ServiceType { get; set; } = "MA";

    [Required(ErrorMessage = "กรุณาระบุวันที่เข้าปฏิบัติงาน")]
    [DataType(DataType.Date)]
    [Display(Name = "วันที่เข้าปฏิบัติงาน")]
    public DateTime VisitDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    [Display(Name = "วันที่สิ้นสุด")]
    public DateTime? EndVisitDate { get; set; }

    [DataType(DataType.Time), Display(Name = "เวลาเริ่ม")]
    public TimeSpan? StartTime { get; set; }

    [DataType(DataType.Time), Display(Name = "เวลาสิ้นสุด")]
    public TimeSpan? EndTime { get; set; }

    [Display(Name = "ผู้ติดต่อ")]
    public string? ContactName { get; set; }

    [Display(Name = "เบอร์โทร")]
    public string? ContactPhone { get; set; }

    [Display(Name = "รายละเอียดงาน")]
    public string? Description { get; set; }

    [Display(Name = "ผลการปฏิบัติงาน")]
    public string? ServiceResult { get; set; }

    [Display(Name = "สถานะ")]
    public string Status { get; set; } = "PLANNED";

    [DataType(DataType.Date), Display(Name = "วันนัดหมายครั้งถัดไป")]
    public DateTime? NextVisitDate { get; set; }

    [Display(Name = "พนักงาน")]
    public List<int> AssigneeIds { get; set; } = new();

    public IEnumerable<SelectListItem> Cooperatives { get; set; } = Array.Empty<SelectListItem>();
    public IEnumerable<SelectListItem> Employees { get; set; } = Array.Empty<SelectListItem>();
}

public class FieldServiceCalendarMoveViewModel
{
    public int Id { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public bool AllDay { get; set; }
}
