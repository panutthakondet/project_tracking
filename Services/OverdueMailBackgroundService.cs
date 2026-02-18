using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ProjectTracking.Services;

namespace ProjectTracking.Services
{
    public class OverdueMailBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverdueMailBackgroundService> _logger;
        private readonly IConfiguration _configuration;

        public OverdueMailBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<OverdueMailBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("📧 OverdueMailBackgroundService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;

                // =================================================
                // 🎯 TARGET TIME : READ FROM ENV (HH:mm)
                // =================================================
                var timeConfig = _configuration["OVERDUE_MAIL_TIME"] ?? "09:00";

                if (!TimeSpan.TryParse(timeConfig, out var targetTime))
                {
                    // fallback ถ้า config ผิด
                    targetTime = new TimeSpan(9, 0, 0);
                }

                var nextRun = DateTime.Today.Add(targetTime);

                if (now >= nextRun)
                {
                    // เลยเวลาของวันนี้แล้ว → รอวันถัดไป
                    nextRun = nextRun.AddDays(1);
                }

                var delay = nextRun - now;

                if (delay.TotalMilliseconds < 0)
                    delay = TimeSpan.FromMinutes(1); // กัน edge case

                _logger.LogInformation(
                    "⏳ Next overdue mail run at {Time} (in {Minutes} minutes)",
                    nextRun,
                    delay.TotalMinutes.ToString("0.##")
                );

                // =================================================
                // ⏳ WAIT UNTIL TARGET TIME
                // =================================================
                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<OverdueMailService>();

                    _logger.LogInformation("🚀 Sending overdue mail...");
                    await service.SendOncePerDayAsync();
                    _logger.LogInformation("✅ Overdue mail finished");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("⏹ Overdue mail task cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while sending overdue mail");
                }

                // =================================================
                // 💤 SAFETY DELAY (กัน loop ยิงซ้ำ)
                // =================================================
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("🛑 OverdueMailBackgroundService stopped");
        }
    }
}