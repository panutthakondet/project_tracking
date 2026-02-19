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

        // =====================================================
        // GENERIC SEND (ใช้ซ้ำได้ทุกกรณี)
        // =====================================================
        public async Task SendAsync(
            string to,
            string subject,
            string body,
            IEnumerable<string>? ccList = null,   // รองรับ List / Array
            IEnumerable<string>? bccList = null   // รองรับ List / Array
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

            var senderEmail = !string.IsNullOrWhiteSpace(_settings.SenderEmail)
                ? _settings.SenderEmail
                : Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL");

            var enableSsl = _settings.EnableSsl;
            if (Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL") is string sslStr && bool.TryParse(sslStr, out var sslEnv))
                enableSsl = sslEnv;

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
                throw new InvalidOperationException("SMTP_SENDER_EMAIL is missing");

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
                Timeout = 30_000 // 30s
            };

            if (string.IsNullOrWhiteSpace(senderEmail))
                throw new InvalidOperationException("SMTP_SENDER_EMAIL is missing");

            using var mail = new MailMessage
            {
                From = new MailAddress(senderEmail),
                Subject = subject,
                SubjectEncoding = Encoding.UTF8,
                Body = body,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true
            };

            // =================================================
            // TO (ต้องมีอย่างน้อย 1)
            // =================================================
            if (!string.IsNullOrWhiteSpace(to))
            {
                mail.To.Add(to.Trim().ToLowerInvariant());
            }
            else
            {
                throw new InvalidOperationException("Email TO is required");
            }

            // =================================================
            // CC (ใช้จาก parameter ถ้ามี)
            // =================================================
            if (ccList != null)
            {
                foreach (var cc in ccList.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    mail.CC.Add(cc.Trim());
                }
            }

            // =================================================
            // BCC (ใช้จาก parameter ถ้ามี)
            // =================================================
            if (bccList != null)
            {
                foreach (var bcc in bccList.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    mail.Bcc.Add(bcc.Trim());
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

            await SendAsync(
                to: _settings.SenderEmail, // admin / mail กลาง
                subject: subject,
                body: body
            );
        }
    }
}