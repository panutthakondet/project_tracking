using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Controllers;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services.Security;
using ProjectTracking.SecurityWorker;
using System.Net;

public static class ArtifactChecks
{
 public static async Task Run(AppDbContext db,SecurityScanService service,SecurityStorage storage)
 {
  var system=new SecuritySystem{ProjectId=1,Kind="iOS",Name="IPA fixture"};db.SecuritySystemRows.Add(system);await db.SaveChangesAsync();
  var version=new SecurityVersion{SystemId=system.Id,VersionName="artifact-test",Environment="UAT"};db.SecurityVersionRows.Add(version);await db.SaveChangesAsync();
  var file=await storage.WriteAsync(new MemoryStream(new byte[]{1,2,3}),".ipa",1024,default);
  db.SecurityArtifactRows.Add(new(){VersionId=version.Id,StorageKey=file.Key,Sha256=file.Hash,Size=file.Size,OriginalName="fixture.ipa"});await db.SaveChangesAsync();
  var id=await service.QueueAsync(version.Id,"Static",Guid.NewGuid().ToString(),1,default);var job=await service.ClaimAsync(default);
  if(job?.Id!=id)throw new Exception("Artifact fixture not claimed");
  var http=new DefaultHttpContext();http.Request.Headers["X-Scan-Lease"]=job.LeaseToken;
  var controller=new SecurityWorkerController(service,db,storage){ControllerContext=new ControllerContext{HttpContext=http}};
  var result=await controller.Artifact(id,default);
  if(result is not FileStreamResult stream)throw new Exception("Artifact must stream only after opening private file");
  using(var output=new MemoryStream()){await stream.FileStream.CopyToAsync(output);await stream.FileStream.DisposeAsync();if(!output.ToArray().SequenceEqual(new byte[]{1,2,3}))throw new Exception("Artifact bytes changed");}
  storage.Delete(file.Key);
  var missing=await controller.Artifact(id,default);
  if(missing is not NotFoundObjectResult || http.Response.Headers["X-Security-Error"]!="ARTIFACT_FILE_MISSING")throw new Exception("Missing file must give controlled API error");
  http.Request.Headers["X-Scan-Lease"]="invalid";
  if(await controller.Artifact(id,default) is not NotFoundResult)throw new Exception("Invalid lease must not reveal missing file details");
  using var response=new HttpResponseMessage(HttpStatusCode.NotFound);response.Headers.Add("X-Security-Error","ARTIFACT_FILE_MISSING");
  try{ScannerHttp.RequireSuccess(response,"ProjectTracking artifact download");throw new Exception("Expected missing-file failure");}catch(ScannerRequestException e){if(!e.Stage.Contains("file missing on server"))throw;}
  Console.WriteLine("PASS private IPA streams unchanged; missing artifact returns safe 404 instead of HTML redirect; invalid lease reveals no file details");
 }
}
