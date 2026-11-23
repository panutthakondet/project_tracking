using ProjectTracking.Models;
using ProjectTracking.Services.Security;
namespace ProjectTracking.ViewModels.Security;

public record ScanProgressPage(long Id,string Status,string StatusLabel,string Stage,string Label,int? Percent,
    long? BytesDone,long? BytesTotal,DateTime? UpdatedAt,DateTime? StartedAt,DateTime? FinishedAt,
    bool Terminal,int StepIndex,ScanProgressStep[] Steps,string? Error)
{
    public bool IsBatch { get; init; }
    public string[] StepStates { get; init; }=[];
    public static ScanProgressPage FromRun(SecurityScanRun run,IReadOnlyCollection<SecurityScanProgress> progress)
    {
        var ordered=run.Scans.OrderBy(x=>Array.IndexOf(SecurityValues.WebsiteSections,run.Section(x.Id))).ToArray();
        var completed=ordered.Count(x=>x.Status=="Completed");
        var finished=ordered.Count(x=>x.Status is "Completed" or "Failed" or "Cancelled");
        var steps=ordered.Select(x=>new ScanProgressStep(run.Section(x.Id),SecurityValues.SectionLabel(run.Section(x.Id))+" · "+SecurityValues.StatusLabel(x.Status))).ToArray();
        var current=Array.FindIndex(ordered,x=>x.Status is "Running" or "Processing");
        if(current<0)current=Array.FindIndex(ordered,x=>x.Status=="Queued");
        var state=From(run.Scan,null);
        return state with {Stage="WebsiteBatch",Label=$"Website รวม {ordered.Length} ส่วน · สำเร็จ {completed} · สิ้นสุด {finished}",
            Percent=(int)Math.Floor(100d*finished/ordered.Length),StepIndex=current,Steps=steps,
            UpdatedAt=progress.Max(x=>(DateTime?)DateTime.SpecifyKind(x.UpdatedAt,DateTimeKind.Utc)),
            IsBatch=true,StepStates=ordered.Select(x=>x.Status).ToArray()};
    }
    public static ScanProgressPage From(SecurityScan scan,SecurityScanProgress? progress)
    {
        var steps=SecurityProgress.Stages(scan.Engine,scan.Mode,scan.RequiresLogin);
        var terminal=scan.Status is "Completed" or "Failed" or "Cancelled";
        var stage=scan.Status=="Queued"?"Queued":scan.Status=="Completed"?"Completed":progress?.Stage??"Running";
        var index=Array.FindIndex(steps,s=>s.Stage==stage);
        var label=scan.Status=="Completed"?"บันทึกผลสแกนเรียบร้อย":scan.Status=="Queued"?"รอเครื่องสแกนรับงาน":index>=0?steps[index].Label:"กำลังสแกน — รอรายละเอียดจาก Worker";
        var percent=stage=="Completed"?100:stage is "Downloading" or "Uploading" or "Crawling" or "ActiveScanning"?progress?.Percent:null;
        DateTime? Utc(DateTime? value)=>value.HasValue?DateTime.SpecifyKind(value.Value,DateTimeKind.Utc):null;
        return new(scan.Id,scan.Status,SecurityValues.StatusLabel(scan.Status),stage,label,percent,
            progress?.BytesDone,progress?.BytesTotal,Utc(progress?.UpdatedAt),Utc(scan.StartedAt),Utc(scan.FinishedAt),
            terminal,scan.Status=="Completed"?steps.Length:index,steps,scan.Error);
    }
}
