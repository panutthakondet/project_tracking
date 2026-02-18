using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectTracking.Models;
using System.Net;
using System.Net.Mail;
using System.Collections.Generic;
using System.Linq;

namespace ProjectTracking.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
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
            using var smtp = new SmtpClient(_settings.SmtpServer, _settings.Port)
            {
                // สำคัญ: ห้ามใช้ DefaultCredentials เมื่อจะส่งผ่าน Gmail/SMTP Auth
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(
                    _settings.Username,
                    _settings.Password
                ),
                EnableSsl = _settings.EnableSsl,
                Timeout = 30_000 // 30s
            };

            if (string.IsNullOrWhiteSpace(_settings.SenderEmail))
                throw new InvalidOperationException("SMTP_SENDER_EMAIL is missing");

            using var mail = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail),
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
                mail.To.Add(to.Trim());
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