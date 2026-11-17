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
Check(enabled.Contains("--pt-body-bg: #F1F3F5") || enabled.Contains("--pt-body-bg:#F1F3F5"), "page background adapts, not only charts");
Check(enabled.Contains("#20252B") && enabled.Contains("#005A91"), "sidebar, menus and primary controls use accessible theme");
foreach(var mode in new[]{"blue-yellow","monochrome"})
{
    var post=Post(true);post.ColorVisionMode=mode;
    await service.SaveAppearanceAsync(1,post);db.ChangeTracker.Clear();
    Check((await service.GetAppearanceAsync(1)).ColorVisionMode==mode, mode+" persists per user");
    var css=await service.GetThemeCssAsync(1);
    Check(css.Contains(mode=="monochrome"?"--pt-chart-success:#E6E6E6":"--pt-chart-success:#006B58"),mode+" semantic palette changes");
}
var invalid=Post(true);invalid.ColorVisionMode="invalid";await service.SaveAppearanceAsync(1,invalid);
Check((await service.GetAppearanceAsync(1)).ColorVisionMode=="red-green","unknown modes fall back safely");
await File.WriteAllTextAsync("/private/tmp/color-vision-enabled.css",enabled);
await File.WriteAllTextAsync("/private/tmp/color-vision-standard.css",original);
await service.SaveAppearanceAsync(1,Post(false));
var disabled=await service.GetThemeCssAsync(1);
Check(!disabled.Contains("--pt-colorblind-enabled:1"),"mode can be disabled");
Check(disabled.Contains("--pt-body-bg: #EEF3F9;"),"disabling restores original background palette");
await service.SaveAppearanceAsync(1,Post(true));await service.ResetAppearanceAsync(1);
Check(!(await service.GetAppearanceAsync(1)).ColorBlindModeEnabled,"reset clears accessibility preference too");
await service.SaveAppearanceAsync(1,Post(true));
await service.SetColorVisionModeAsync(1,"blue-yellow");
var shortcut=await service.GetAppearanceAsync(1);
Check(shortcut.ColorVisionMode=="blue-yellow" && shortcut.FontScale==1.05m,"dashboard shortcut changes only colour vision preferences");
var styles=await service.GetColorVisionStylesAsync(1);
Check(styles.Count==4 && styles["standard"].Contains("--pt-colorblind-enabled:0") && styles["monochrome"].Contains("--pt-chart-success:#E6E6E6"),"preloaded styles include standard and all three modes");
Check((await service.GetAppearanceAsync(1)).ColorVisionMode=="blue-yellow","preloading does not change saved preferences");
await service.SetColorVisionModeAsync(1,"standard");
Check(!(await service.GetAppearanceAsync(1)).ColorBlindModeEnabled,"dashboard shortcut restores standard mode");
await service.SetColorVisionModeAsync(2,"monochrome");
Check((await service.GetAppearanceAsync(2)).ColorVisionMode=="monochrome","dashboard shortcut supports accounts without preferences");
var paletteReport=new Dictionary<string,object>();
foreach(var mode in new[]{"red-green","blue-yellow","monochrome"})
{
    var palette=ColorVisionTheme.GetPalette(mode);
    foreach(var (tone,color) in palette)
        Check(ColorVisionTheme.Contrast(color,ColorVisionTheme.TextOn(color))>=4.5,mode+" "+tone+" foreground contrast >= 4.5:1");
    if(mode=="monochrome")Check(palette.Values.All(c=>c.Substring(1,2)==c.Substring(3,2)&&c.Substring(3,2)==c.Substring(5,2)),"monochrome semantic palette is achromatic");
    var css=ColorVisionTheme.BuildCss(true,mode);
    Check(css.Contains("repeating-linear-gradient")&&css.Contains("content:'✓ '")&&css.Contains("content:'× '"),mode+" includes non-colour status cues");
    paletteReport[mode]=palette.ToDictionary(p=>p.Key,p=>new {background=p.Value,foreground=ColorVisionTheme.TextOn(p.Value),contrast=ColorVisionTheme.Contrast(p.Value,ColorVisionTheme.TextOn(p.Value))});
    var fullStyles=await service.GetColorVisionStylesAsync(1);
    await File.WriteAllTextAsync("/private/tmp/color-vision-"+mode+".css",fullStyles[mode]);
}
Check(ColorVisionTheme.Contrast("#17212B","#F1F3F5")>=4.5&&ColorVisionTheme.Contrast("#47515C","#FFFFFF")>=4.5,"page text and muted labels meet normal text contrast");
await File.WriteAllTextAsync("/private/tmp/color-vision-palette-audit.json",System.Text.Json.JsonSerializer.Serialize(paletteReport));
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
