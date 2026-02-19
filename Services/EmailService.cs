using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectTracking.Models;
using System.Net;
using System.Net.Mail;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.Extensions.Logging;

namespace ProjectTracking.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService>? _logger;

        public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService>? logger = null)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        private static IEnumerable<string> SplitEmails(string emails)
        {
            // รองรับ ; , เว้นวรรค และขึ้นบรรทัดใหม่
            return emails
                .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        // =====================================================
        // GENERIC SEND (ใช้ซ้ำได้ทุกกรณี)
        // =====================================================
        public async Task SendAsync(
            string to,
            string subject,
            string body,
            IEnumerable<string>? ccList = null,
            IEnumerable<string>? bccList = null
        )
        {
            // ✅ Resolve SMTP settings (Options first, then ENV fallback)
            var smtpServer = !string.IsNullOrWhiteSpace(_settings.SmtpServer)
                ? _settings.SmtpServer
                : Environment.GetEnvironmentVariable("SMTP_SERVER");

            var port = _settings.Port > 0
                ? _settings.Port
                : (int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 0);

            var username = !string.IsNullOrWhiteSpace(_settings.Username)
                ? _settings.Username
                : Environment.GetEnvironmentVariable("SMTP_USERNAME");

            var password = !string.IsNullOrWhiteSpace(_settings.Password)
                ? _settings.Password
                : Environment.GetEnvironmentVariable("SMTP_PASSWORD");

            // SenderEmail: รองรับทั้ง SMTP_SENDER_EMAIL และ SMTP_FROM
            var senderEmail = !string.IsNullOrWhiteSpace(_settings.SenderEmail)
                ? _settings.SenderEmail
                : (Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL")
                   ?? Environment.GetEnvironmentVariable("SMTP_FROM"));

            var enableSsl = _settings.EnableSsl;
            if (Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL") is string sslStr &&
                bool.TryParse(sslStr, out var sslEnv))
            {
                enableSsl = sslEnv;
            }

            // ✅ Normalize
            smtpServer = smtpServer?.Trim();
            username = username?.Trim().ToLowerInvariant();
            senderEmail = senderEmail?.Trim().ToLowerInvariant();

            // ✅ Validate required SMTP config
            if (string.IsNullOrWhiteSpace(smtpServer))
                throw new InvalidOperationException("SMTP_SERVER is missing");

            if (port <= 0)
                throw new InvalidOperationException("SMTP_PORT is missing/invalid");

            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("SMTP_USERNAME is missing");

            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("SMTP_PASSWORD is missing/empty");

            if (string.IsNullOrWhiteSpace(senderEmail))
                throw new InvalidOperationException("SMTP_SENDER_EMAIL (or SMTP_FROM) is missing");

            // ✅ Safe diagnostic log (never log password)
            _logger?.LogInformation(
                "SMTP cfg resolved. Host={Host}, Port={Port}, User={User}, Sender={Sender}, SSL={SSL}, PassLen={PassLen}",
                smtpServer, port, username, senderEmail, enableSsl, password.Length
            );

            using var smtp = new SmtpClient(smtpServer, port)
            {
                // สำคัญ: ห้ามใช้ DefaultCredentials เมื่อจะส่งผ่าน Gmail/SMTP Auth
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(username, password),
                EnableSsl = enableSsl,
                Timeout = 30_000
            };

            // Sender
            using var mail = new MailMessage
            {
                From = new MailAddress(senderEmail),
                Subject = subject ?? "",
                SubjectEncoding = Encoding.UTF8,
                Body = body ?? "",
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true
            };

            // =================================================
            // TO (ต้องมีอย่างน้อย 1) + รองรับหลายเมล
            // =================================================
            if (string.IsNullOrWhiteSpace(to))
                throw new InvalidOperationException("Email TO is required");

            foreach (var addr in SplitEmails(to))
                mail.To.Add(addr.Trim().ToLowerInvariant());

            if (mail.To.Count == 0)
                throw new InvalidOperationException("Email TO is required");

            // =================================================
            // CC
            // =================================================
            if (ccList != null)
            {
                foreach (var cc in ccList.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    foreach (var addr in SplitEmails(cc))
                        mail.CC.Add(addr.Trim().ToLowerInvariant());
                }
            }

            // =================================================
            // BCC
            // =================================================
            if (bccList != null)
            {
                foreach (var bcc in bccList.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    foreach (var addr in SplitEmails(bcc))
                        mail.Bcc.Add(addr.Trim().ToLowerInvariant());
                }
            }

            await smtp.SendMailAsync(mail);
        }

        // =====================================================
        // PHASE OVERDUE (สำหรับเรียกตรง ถ้าจำเป็น)
        // =====================================================
        public async Task SendPhaseOverdueAsync()
        {
            var subject = "⚠️ Phase Overdue Notification";
            var body = @"
                <h3>แจ้งเตือน Phase Overdue</h3>
                <p>มี Phase ที่เกินกำหนด กรุณาตรวจสอบในระบบ Project Tracking</p>
            ";

            // ส่งไปที่เมลผู้ส่ง/เมลกลาง (ตาม settings/env)
            var to = !string.IsNullOrWhiteSpace(_settings.SenderEmail)
                ? _settings.SenderEmail
                : (Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL")
                   ?? Environment.GetEnvironmentVariable("SMTP_FROM")
                   ?? "");

            await SendAsync(to: to, subject: subject, body: body);
        }
    }
}