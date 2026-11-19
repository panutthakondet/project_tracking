namespace ProjectTracking.Services.Security;
public record SecurityJob(long Id,string LeaseToken,string Kind,string Engine,string Mode,string VersionName,string? BuildNumber,string Environment,string? TargetUrl,bool RequiresLogin,string? AuthProfile,string? ArtifactName,string? ArtifactSha256,string[]? ApprovedHosts=null,SecurityLoginCredentials? Login=null);
// Class rather than record: incidental ToString() must not print credentials.
public sealed class SecurityLoginCredentials
{
    public string LoginUrl { get; set; } = "";
    public string RequestData { get; set; } = "username={%username%}&password={%password%}";
    public string LoggedInRegex { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public record ScanObservation(string RuleId,string Title,string Severity,string Location,string Description,string Evidence,string Remediation,string References,string? Cwe=null);
public record ScanCompletion(string EngineVersion,string Coverage);
public record ScanProgressUpdate(string Stage,int? Percent=null,long? BytesDone=null,long? BytesTotal=null);
public record ScanProgressStep(string Stage,string Label);
public static class SecurityProgress
{
    public static ScanProgressStep[] Stages(string engine,string mode="",bool requiresLogin=false) => engine=="MobSF"
        ? [new("Preparing","เตรียมงาน"),new("Downloading","ดาวน์โหลดไฟล์จากระบบ"),new("Verifying","ตรวจสอบไฟล์"),new("Uploading","ส่งไฟล์เข้า MobSF"),new("Analyzing","วิเคราะห์ความปลอดภัย"),new("FetchingReport","รับรายงานจาก MobSF"),new("SavingResults","บันทึกผลเข้าระบบ")]
        : new[]{new ScanProgressStep("Preparing","เตรียมงาน"),new("Authenticating","เข้าสู่ระบบทดสอบ"),new("Crawling","สำรวจหน้าเว็บไซต์"),new("ActiveScanning","ตรวจแบบ Active"),new("PassiveChecks","ตรวจแบบ Passive"),new("FetchingReport","รับรายงานจาก ZAP"),new("SavingResults","บันทึกผลเข้าระบบ")}.Where(s=>(s.Stage!="Authenticating"||requiresLogin)&&(s.Stage!="ActiveScanning"||mode=="Active")).ToArray();
    public static bool IsValid(ScanProgressUpdate update) => !string.IsNullOrEmpty(update.Stage)
        && Stages("MobSF").Concat(Stages("ZAP","Active",true)).Any(s=>s.Stage==update.Stage)
        && (update.Percent is null or >=0 and <=100)
        && (update.BytesDone is null or >=0) && (update.BytesTotal is null or >0)
        && (!update.BytesDone.HasValue||!update.BytesTotal.HasValue||update.BytesDone<=update.BytesTotal);
}
public static class SecurityValues
{
    public static readonly string[] Kinds=["WebInformation","WebMember","Android","iOS"];
    public static readonly string[] ReviewStatuses=["Open","InProgress","FixedPendingVerification","VerifiedFixed","FalsePositive","AcceptedRisk"];
    public static bool IsMobile(string kind)=>kind is "Android" or "iOS";
    public static string Severity(string? value)=>value?.Trim().ToLowerInvariant() switch {"critical"=>"Critical","high" or "3" or "danger"=>"High","medium" or "moderate" or "2" or "warning"=>"Medium","low" or "1" or "good"=>"Low",_=>"Info"};
    public static int Rank(string severity)=>severity switch{"Critical"=>5,"High"=>4,"Medium"=>3,"Low"=>2,_=>1};
    public static string StatusLabel(string status)=>status switch{"Queued"=>"รอสแกน","Running"=>"กำลังสแกน","Processing"=>"กำลังเก็บรายงาน","Completed"=>"เสร็จสิ้น","Failed"=>"ไม่สำเร็จ","Cancelled"=>"ยกเลิก","Open"=>"ยังไม่แก้ไข","InProgress"=>"กำลังแก้ไข","FixedPendingVerification"=>"แก้ไขแล้ว รอตรวจยืนยัน","VerifiedFixed"=>"ตรวจยืนยันแล้ว","FalsePositive"=>"ผลตรวจคลาดเคลื่อน","AcceptedRisk"=>"ยอมรับความเสี่ยง",_=>status};
    public static string Cut(string? value,int length)=>string.IsNullOrEmpty(value)?"":value.Length<=length?value:value[..length];
}
public static class SecurityReportLimits
{
    public const int MaxRawFindings=100000;
    public const int MaxUniqueFindings=50000;
    public const int MaxReportBytes=50*1024*1024;
}
