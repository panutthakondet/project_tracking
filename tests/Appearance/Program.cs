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
var saved = await service.SaveAppearanceAsync(1, new AppearancePostViewModel
{
    ThemeId = 1, DinoColorHex = "#FFFFFF", DinoFoodColorHex = "#45D6C6", FontScale = 1.05m
});
db.ChangeTracker.Clear();
var appearance = await service.GetAppearanceAsync(1);
if (!saved.Success || appearance.FontScale != 1.05m || appearance.ThemeId != 1)
    throw new Exception("Appearance settings did not persist.");
Console.WriteLine("PASS appearance settings persist in isolated SQLite");

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
