using Microsoft.EntityFrameworkCore;
using ProjectTracking.Models;
namespace ProjectTracking.Services.Security;
public static class SecurityModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<SecurityHostApproval>().HasIndex(x=>new{x.ProjectId,x.Host}).IsUnique();
        b.Entity<SecuritySystem>().HasIndex(x=>x.ProjectId);
        b.Entity<SecurityVersion>().HasOne<SecuritySystem>().WithMany().HasForeignKey(x=>x.SystemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityArtifact>().HasIndex(x=>x.VersionId).IsUnique();
        b.Entity<SecurityArtifact>().HasOne<SecurityVersion>().WithMany().HasForeignKey(x=>x.VersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityScan>().HasIndex(x=>x.RequestKey).IsUnique();
        b.Entity<SecurityScan>().HasIndex(x=>new{x.Status,x.CreatedAt});
        b.Entity<SecurityScan>().HasOne<SecurityVersion>().WithMany().HasForeignKey(x=>x.VersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityScanProgress>().Property(x=>x.ScanId).ValueGeneratedNever();
        b.Entity<SecurityScanProgress>().HasOne<SecurityScan>().WithOne().HasForeignKey<SecurityScanProgress>(x=>x.ScanId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityFinding>().HasIndex(x=>new{x.SystemId,x.Fingerprint}).IsUnique();
        b.Entity<SecurityFinding>().HasOne<SecuritySystem>().WithMany().HasForeignKey(x=>x.SystemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityScanFinding>().HasIndex(x=>new{x.ScanId,x.FindingId}).IsUnique();
        b.Entity<SecurityScanFinding>().HasOne<SecurityScan>().WithMany().HasForeignKey(x=>x.ScanId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityScanFinding>().HasOne<SecurityFinding>().WithMany().HasForeignKey(x=>x.FindingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityReport>().HasIndex(x=>new{x.ScanId,x.Format}).IsUnique();
        b.Entity<SecurityReport>().HasOne<SecurityScan>().WithMany().HasForeignKey(x=>x.ScanId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityFindingHistory>().HasOne<SecurityFinding>().WithMany().HasForeignKey(x=>x.FindingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityFindingIssueLink>().HasIndex(x=>x.FindingId).IsUnique();
        b.Entity<SecurityFindingIssueLink>().HasOne<SecurityFinding>().WithMany().HasForeignKey(x=>x.FindingId).OnDelete(DeleteBehavior.Restrict);
    }
}
