using System.ComponentModel.DataAnnotations;

public class TestScenarioAttachment
{
    [Key]
    public int AttachmentId { get; set; }
    public int ScenarioId { get; set; }

    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? FileType { get; set; }
    public int? FileSize { get; set; }
     public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}