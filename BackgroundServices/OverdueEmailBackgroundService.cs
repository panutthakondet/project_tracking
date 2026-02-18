using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTracking.Data;
using ProjectTracking.Services;

namespace ProjectTracking.BackgroundServices
{
    public class OverdueEmailBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverdueEmailBackgroundService> _logger;

        public OverdueEmailBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<OverdueEmailBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("📧 Overdue Email Background Service started");

            // 🔁 ทำงานวนเรื่อย ๆ จนกว่าจะ stop แอป
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunSendEmail(stoppingToken);

                    // ⏰ รันวันละครั้ง
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // ปิดแอปปกติ ไม่ต้อง log error
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in Overdue Email Background Service");
                }
            }
        }

        private async Task RunSendEmail(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var email = scope.ServiceProvider.GetRequiredService<EmailService>();

            var overdueList = await db.VwPhaseOwnerStatuses
                .Where(x => x.OverdueDays > 0)
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ToListAsync(stoppingToken);

            if (!overdueList.Any())
            {
                _logger.LogInformation("ℹ No overdue phase found today");
                return;
            }

            // ✅ BCC กลาง (ได้รับทุกเมลแบบอัตโนมัติ)
            var bccList = new List<string>
            {
                "saowalak.moree@gmail.com",
                "varaphorn.soat@gmail.com",
                "moofaiwirin@gmail.com"
            };

            foreach (var p in overdueList)
            {
                await email.SendAsync(
                    to: "Engineering.Drive@gmail.com",
                    subject: "⚠ Phase Overdue Alert (Auto)",
                    body: $@"
                        <h3 style='color:red;'>Phase Overdue</h3>
                        <p><b>Project:</b> {p.ProjectName}</p>
                        <p><b>Phase:</b> {p.PhaseOrder}</p>
                        <p><b>Owner:</b> {p.EmpName}</p>
                        <p><b>Overdue:</b> {p.OverdueDays} days</p>
                    ",
                    ccList: null,
                    bccList: bccList
                );
            }

            _logger.LogInformation(
                "✅ Overdue email sent automatically ({Count} phase)",
                overdueList.Count
            );
        }
    }
}