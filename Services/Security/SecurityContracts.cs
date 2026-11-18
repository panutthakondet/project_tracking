namespace ProjectTracking.Services.Security;
public record SecurityJob(long Id,string LeaseToken,string Kind,string Engine,string Mode,string VersionName,string? BuildNumber,string Environment,string? TargetUrl,bool RequiresLogin,string? AuthProfile,string? ArtifactName,string? ArtifactSha256);
public record ScanObservation(string RuleId,string Title,string Severity,string Location,string Description,string Evidence,string Remediation,string References,string? Cwe=null);
public record ScanCompletion(string EngineVersion,string Coverage);
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
