using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using System.Text;

namespace ProjectTracking.Services
{
    public class OverdueMailService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly EmailService _emailService;

        public OverdueMailService(
            IDbContextFactory<AppDbContext> dbFactory,
            EmailService emailService)
        {
            _dbFactory = dbFactory;
            _emailService = emailService;
        }

        public async Task SendOncePerDayAsync()
        {
            var today = DateTime.Today;

            // ใช้ DbContext “เฉพาะงานนี้” เสมอ (กัน concurrent กับที่อื่น)
            await using var db = await _dbFactory.CreateDbContextAsync();

            // =================================================
            // ✅ CHECK : วันนี้ส่งไปแล้วหรือยัง
            // =================================================
            bool alreadySent = await db.EmailSendLogs
                .AsNoTracking()
                .AnyAsync(x =>
                    x.MailType == "PHASE_OVERDUE" &&
                    x.SentDate == today
                );

            if (alreadySent)
                return;

            // =================================================
            // ✅ GET OVERDUE PHASES
            // =================================================
            var overduePhases = await db.VwPhaseOwnerStatuses
                .AsNoTracking()
                .Where(x =>
                    x.PhaseStatus == "DELAY" &&
                    x.OverdueDays > 0
                )
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ToListAsync();

            if (overduePhases.Count == 0)
                return;

            // =================================================
            // ✅ GROUP BY PROJECT
            // =================================================
            var projectGroups = overduePhases
                .GroupBy(x => x.ProjectName)
                .ToList();

            // =================================================
            // ✅ SEND EMAIL : 1 PROJECT = 1 EMAIL
            // (ไม่มีการแตะ db ระหว่างส่งเมล)
            // =================================================
            foreach (var project in projectGroups)
            {
                if (project == null || !project.Any())
                    continue;

                var subject = $"⏰ Phase Overdue | {project.Key}";

                var bodyBuilder = new StringBuilder();
                bodyBuilder.Append($@"
                    <h2 style='color:#d9534f;'>🚨 Phase Overdue แจ้งเตือน</h2>
                    <h3>Project: {project.Key}</h3>
                    <hr/>
                ");

                var empNames = project
                    .Where(x => x != null)
                    .Select(x => x.EmpName ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var p in project)
                {
                    if (p == null) continue;

                    bodyBuilder.Append($@"
                        <table border='1' cellpadding='6' cellspacing='0'
                               style='border-collapse:collapse;
                                      margin-bottom:20px;
                                      width:100%;'>
                            <tr style='background:#f8f9fa;'>
                                <td width='200'><b>Phase Order</b></td>
                                <td>{p.PhaseOrder}</td>
                            </tr>
                            <tr>
                                <td><b>Employee</b></td>
                                <td>{p.EmpName}</td>
                            </tr>
                            <tr>
                                <td><b>Role</b></td>
                                <td>{p.Role}</td>
                            </tr>
                            <tr>
                                <td><b>Overdue</b></td>
                                <td style='color:red; font-weight:bold;'>
                                    {p.OverdueDays} วัน
                                </td>
                            </tr>
                            <tr>
                                <td><b>Status</b></td>
                                <td>{p.PhaseStatus}</td>
                            </tr>
                        </table>
                    ");
                }

                bodyBuilder.Append(@"
                    <p>
                        กรุณาเข้าสู่ระบบ <b>Project Tracking</b>
                        เพื่อตรวจสอบรายละเอียดเพิ่มเติม
                    </p>
                ");

                // =================================================
                // ✅ RECIPIENTS : pull from login_user by username (= EmpName)
                // =================================================
                var recipientEmails = await db.LoginUsers
                    .AsNoTracking()
                    .Where(u => empNames.Contains(u.Username) && u.Status == "ACTIVE")
                    .Select(u => (u.Email ?? "").Trim())
                    .ToListAsync();

                recipientEmails = recipientEmails
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // ให้เป็น non-null เสมอ
                string toEmail = "";
                List<string>? bccEmails = null;

                if (recipientEmails.Count > 0)
                {
                    toEmail = recipientEmails[0];
                    var rest = recipientEmails.Skip(1).ToList();
                    bccEmails = rest.Count > 0 ? rest : null;
                }
                else
                {
                    // fallback (เดิม) ถ้าไม่มี email ในระบบ
                    toEmail = "engineering.drive@gmail.com";
                    bccEmails = new List<string>
                    {
                        "varaphorn.soat@gmail.com",
                        "saowalak.moree@gmail.com",
                        "moofaiwirin@gmail.com"
                    };
                }

                if (string.IsNullOrWhiteSpace(toEmail))
                    continue;

                var delivered = await _emailService.SendAsync(
                    to: toEmail,
                    subject: subject,
                    body: bodyBuilder.ToString(),
                    ccList: null,
                    bccList: bccEmails
                );
                if (!delivered)
                    return;
            }

            // =================================================
            // ✅ SAVE LOG (ส่งครบทุก Project แล้ว)
            // =================================================
            db.EmailSendLogs.Add(new EmailSendLog
            {
                MailType = "PHASE_OVERDUE",
                SentDate = today,
                SentAt = DateTime.Now
            });

            await db.SaveChangesAsync();
        }
    }
}