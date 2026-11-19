using System.Text.Json;
using ProjectTracking.SecurityWorker;
using ProjectTracking.Services.Security;

public static class LargeReportChecks
{
 public static async Task Run()
 {
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
  JsonElement Page(int start,int total,bool duplicate=false){
   using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{alerts=Enumerable.Range(start,Math.Min(500,Math.Max(0,total-start))).Select(i=>new{id=i.ToString(),pluginId="100",alert="Missing header",risk="Medium",url="https://fixture.example/page/"+(duplicate?0:i),evidence="fixture"})}));return doc.RootElement.Clone();
  }
  var offsets=new List<int>();
  var result=await ZapAlertCollector.CollectAsync((start,ct)=>{offsets.Add(start);return Task.FromResult(Page(start,10001));},default);
  using(var doc=JsonDocument.Parse(result.Json))Check(result.Count==10001&&doc.RootElement.GetProperty("alerts").GetArrayLength()==10001&&doc.RootElement.GetProperty("alerts")[10000].GetProperty("id").GetString()=="10000","ZAP collector preserves all 10,001 raw alerts across pages");
  Check(offsets.SequenceEqual(Enumerable.Range(0,21).Select(x=>x*500)),"ZAP pagination advances start offsets without repeating pages");
  Check(SecurityReportParser.Parse("ZAP",result.Json).Count==10001,"backend accepts more than 10,000 distinct findings");
  var duplicates=await ZapAlertCollector.CollectAsync((start,ct)=>Task.FromResult(Page(start,10001,true)),default);
  Check(SecurityReportParser.Parse("ZAP",duplicates.Json).Count==1,"duplicate raw alerts are normalized before counting unique findings while original report remains complete");
  try{await ZapAlertCollector.CollectAsync((start,ct)=>Task.FromResult(Page(0,500)),default);throw new Exception("Repeated page accepted");}catch(InvalidOperationException e)when(e.Message.Contains("repeated")){Console.WriteLine("PASS repeated report page rejected instead of looping or fabricating results");}
  try{await ZapAlertCollector.CollectAsync((start,ct)=>Task.FromResult(Page(start,SecurityReportLimits.MaxRawFindings+1)),default);throw new Exception("Oversized count silently accepted");}catch(InvalidOperationException e)when(e.Message.Contains("100,000")){Console.WriteLine("PASS raw alert capacity fails explicitly without returning a truncated report");}
 }
}
