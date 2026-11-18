using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ProjectTracking.Data;
namespace ProjectTracking.Services.Security;
public static class SecuritySchema
{
    // New tables only; existing business tables and records are never altered.
    public static IEnumerable<string> Statements(AppDbContext db)
    {
        foreach(var entity in db.Model.GetEntityTypes().Where(e=>e.GetTableName()?.StartsWith("security_",StringComparison.Ordinal)==true)){
            var name=entity.GetTableName()!;var store=StoreObjectIdentifier.Table(name,null);
            var columns=entity.GetProperties().Select(p=>$"`{p.GetColumnName(store)}` {p.GetRelationalTypeMapping().StoreType} {(p.IsNullable?"NULL":"NOT NULL")}{(p.Name=="Id"?" AUTO_INCREMENT":"")}").ToList();
            columns.Add("PRIMARY KEY (`Id`)");var index=0;
            foreach(var ix in entity.GetIndexes())columns.Add($"{(ix.IsUnique?"UNIQUE ":"")}KEY `ix_{name}_{index++}` ({string.Join(",",ix.Properties.Select(p=>$"`{p.GetColumnName(store)}`"))})");
            yield return $"CREATE TABLE IF NOT EXISTS `{name}` ({string.Join(",",columns)}) CHARACTER SET utf8mb4;";
        }
    }
    public static async Task EnsureAsync(IServiceProvider services)
    {
        using var scope=services.CreateScope();var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if((Environment.GetEnvironmentVariable("SECURITY_AUTO_CREATE_SCHEMA")??config["SECURITY_AUTO_CREATE_SCHEMA"])!="true")return;
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();foreach(var sql in Statements(db))await db.Database.ExecuteSqlRawAsync(sql);
    }
}
