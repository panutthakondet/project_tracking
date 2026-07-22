using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectTracking.ViewModels;

public class FieldServiceResultViewModel
{
    public int VisitId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CoopName { get; set; } = string.Empty;
    [Required(ErrorMessage = "กรุณาระบุผลการปฏิบัติงาน")]
    [Display(Name = "ผลการปฏิบัติงาน")]
    public string ServiceResult { get; set; } = string.Empty;
    [Display(Name = "สถานะงาน")]
    public string Status { get; set; } = "COMPLETED";
    [DataType(DataType.Date), Display(Name = "วันนัดหมายครั้งถัดไป")]
    public DateTime? NextVisitDate { get; set; }
    [Display(Name = "รูปภาพและไฟล์แนบ")]
    public List<IFormFile> Files { get; set; } = new();
    public List<int> DeleteAttachmentIds { get; set; } = new();
    public List<ProjectTracking.Models.FieldServiceAttachment> ExistingAttachments { get; set; } = new();
}
