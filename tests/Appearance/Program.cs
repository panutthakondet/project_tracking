using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

using var connection=new SqliteConnection("DataSource=:memory:");await connection.OpenAsync();
var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using var db=new ThemeTestDb(options);await db.Database.EnsureCreatedAsync();
db.LoginUsers.AddRange(new LoginUser{UserId=1,Username="one",Password="test"},new LoginUser{UserId=2,Username="two",Password="test"});
db.ThemePresets.Add(new ThemePreset{ThemeId=1,ThemeKey="test",ThemeName="Test",IsDefault=true});await db.SaveChangesAsync();
var service=new UserThemeService(db);
AppearancePostViewModel Post(bool enabled)=>new(){ThemeId=1,ColorBlindModeEnabled=enabled,DinoColorHex="#FFFFFF",DinoFoodColorHex="#45D6C6",FontScale=1.05m};
void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
var original=await service.GetThemeCssAsync(1);
Check(original.Contains("--pt-colorblind-enabled:0"),"existing accounts default to standard colours");
Check((await service.SaveAppearanceAsync(1,Post(true))).Success,"save enabled mode");
db.ChangeTracker.Clear();
var appearance=await service.GetAppearanceAsync(1);
Check(appearance.ColorBlindModeEnabled&&appearance.EffectiveTheme.ColorBlindModeEnabled,"mode persists and restores in settings");
var enabled=await service.GetThemeCssAsync(1);
Check(enabled.Contains("--pt-colorblind-enabled:1")&&enabled.Contains("--pt-chart-success:#0072B2"),"shared theme emits adapted palette");
Check(!(await service.GetAppearanceAsync(2)).ColorBlindModeEnabled&&!(await service.GetThemeCssAsync(2)).Contains("--pt-colorblind-enabled:1"),"another user remains unchanged");
Check(appearance.FontScale==1.05m&&appearance.ThemeId==1,"existing theme and font settings preserved");
Check(enabled.Contains("content:'✓ '")&&enabled.Contains("--owner-base:var(--pt-chart-success)"),"redundant status cues and owner chart palette included");
await File.WriteAllTextAsync("/private/tmp/color-vision-enabled.css",enabled);
await File.WriteAllTextAsync("/private/tmp/color-vision-standard.css",original);
await service.SaveAppearanceAsync(1,Post(false));
Check(!(await service.GetThemeCssAsync(1)).Contains("--pt-colorblind-enabled:1"),"mode can be disabled");
await service.SaveAppearanceAsync(1,Post(true));await service.ResetAppearanceAsync(1);
Check(!(await service.GetAppearanceAsync(1)).ColorBlindModeEnabled,"reset clears accessibility preference too");
Console.WriteLine("ALL APPEARANCE CHECKS PASSED (isolated SQLite, no production database)");
class ThemeTestDb(DbContextOptions<AppDbContext> options):AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder m)
    {
        var keep=new[]{typeof(LoginUser),typeof(ThemePreset),typeof(UserThemePreference)};
        foreach(var e in m.Model.GetEntityTypes().ToArray())if(!keep.Contains(e.ClrType))m.Ignore(e.ClrType);
        m.Entity<ThemePreset>().HasKey(p=>p.ThemeId);m.Entity<UserThemePreference>().HasKey(p=>p.UserId);
        m.Entity<UserThemePreference>().HasOne(p=>p.ThemePreset).WithMany(t=>t.UserPreferences).HasForeignKey(p=>p.ThemeId);
        m.Entity<UserThemePreference>().HasOne(p=>p.User).WithMany().HasForeignKey(p=>p.UserId);
    }
}
