using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Helpers;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ProjectTracking.Controllers
{
    public class UserManagementController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Services.EmailService _emailService;
        private readonly ILogger<UserManagementController> _logger;

        public UserManagementController(
            AppDbContext context,
            Services.EmailService emailService,
            ILogger<UserManagementController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        private bool IsLoggedIn() => HttpContext.Session.GetInt32("UserId") != null;

        private bool IsAdmin()
        {
            var role = HttpContext.Session.GetString("Role") ?? "";
            return role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase);
        }

        private IActionResult? GuardAdmin()
        {
            if (!IsLoggedIn())
            {
                var returnUrl = HttpContext.Request.Path.Value ?? "/";
                return RedirectToAction("Login", "Auth", new { returnUrl });
            }

            if (!IsAdmin())
                return RedirectToAction("Index", "Home");

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            var users = await _context.LoginUsers
                .AsNoTracking()
                .OrderByDescending(u => u.UserId)
                .ToListAsync();

            return View(users);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            return View();
        }

        // ✅ CREATE POST (hash password + create verify token)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string username,
            string email,
            string password,
            string confirmPassword,
            string role,
            string status)
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            username = (username ?? "").Trim();
            email = (email ?? "").Trim();
            password = (password ?? "").Trim();
            confirmPassword = (confirmPassword ?? "").Trim();
            role = (role ?? "USER").Trim().ToUpperInvariant();
            status = (status ?? "ACTIVE").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(username))
            {
                ViewBag.Error = "❌ กรุณากรอก Username";
                return View();
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "❌ กรุณากรอก Email";
                return View();
            }

            if (!email.Contains("@"))
            {
                ViewBag.Error = "❌ รูปแบบ Email ไม่ถูกต้อง";
                return View();
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "❌ กรุณากรอก Password";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "❌ Password และ Confirm Password ไม่ตรงกัน";
                return View();
            }

            if (role != "USER" && role != "ADMIN") role = "USER";
            if (status != "ACTIVE" && status != "INACTIVE") status = "ACTIVE";

            var usernameExists = await _context.LoginUsers
                .AsNoTracking()
                .AnyAsync(u => u.Username == username);

            if (usernameExists)
            {
                ViewBag.Error = "❌ Username นี้มีอยู่แล้ว";
                return View();
            }

            var emailExists = await _context.LoginUsers
                .AsNoTracking()
                .AnyAsync(u => u.Email == email);

            if (emailExists)
            {
                ViewBag.Error = "❌ Email นี้มีอยู่แล้ว";
                return View();
            }

            var passwordHash = SecurityHelper.HashPassword(password);

            // ✅ สร้าง token ส่งเมล แล้วเก็บ hash ลง DB
            var token = SecurityHelper.GenerateToken(32);
            var tokenHash = SecurityHelper.Sha256(token);
            var expire = DateTime.Now.AddHours(24);

            var user = new LoginUser
            {
                Username = username,
                Email = email,

                Password = passwordHash,
                Role = role,
                Status = status,
                CreatedAt = DateTime.Now,

                EmailVerified = false,
                VerifyTokenHash = tokenHash,
                VerifyTokenExpire = expire
            };

            _context.LoginUsers.Add(user);
            await _context.SaveChangesAsync();

            // ✅ ส่งเมลยืนยัน (ห้ามให้เมลพังแล้วล่มหน้า)
            var verifyUrl =
                $"{Request.Scheme}://{Request.Host}/Auth/VerifyEmail?token={Uri.EscapeDataString(token)}&username={Uri.EscapeDataString(username)}";

            var subject = "Verify your email - ProjectTracking";

            // EmailService ของคุณตั้ง IsBodyHtml = true -> ทำเป็น HTML ให้เหมาะสม
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <p>สวัสดี <b>{System.Net.WebUtility.HtmlEncode(username)}</b></p>
                    <p>กรุณายืนยันอีเมล โดยคลิกลิงก์ด้านล่าง (ภายใน 24 ชั่วโมง):</p>
                    <p>
                        <a href='{verifyUrl}' target='_blank' rel='noopener noreferrer'>
                            ยืนยันอีเมล
                        </a>
                    </p>
                    <p style='color:#666;font-size:12px;'>หรือคัดลอกลิงก์นี้ไปวางในเบราว์เซอร์:</p>
                    <p style='color:#666;font-size:12px;word-break:break-all;'>{verifyUrl}</p>
                    <hr/>
                    <p style='color:#999;font-size:12px;'>
                        หากคุณไม่ได้เป็นผู้ขอสร้างบัญชีนี้ สามารถละเว้นอีเมลนี้ได้
                    </p>
                </div>
            ";

            try
            {
                await _emailService.SendAsync(email, subject, body);
                TempData["Success"] = "✅ สร้างผู้ใช้แล้ว และส่งอีเมลยืนยันเรียบร้อย";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Verify email send failed. Username={Username}, Email={Email}", username, email);
                TempData["Error"] = "⚠️ สร้างผู้ใช้แล้ว แต่ส่งอีเมลยืนยันไม่สำเร็จ (โปรดตรวจสอบการตั้งค่า SMTP/Email Service)";
            }

            return RedirectToAction("Index");
        }

        // =====================================================
        // PERMISSIONS (GET)
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Permissions(string username)
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            username = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
                return RedirectToAction(nameof(Index));

            var user = await _context.LoginUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username);

            if (user == null) return NotFound();

            var allMenus = new List<(string Key, string Label)>
            {
                ("Employees.Index", "Employees"),
                ("Projects.Index", "Projects"),
                ("ProjectPhases.Index", "Project Phases"),
                ("PhaseAssigns.Index", "Assigned Employees"),
                ("ProjectIssues.Index", "Project Issues (Tester)"),
                ("ProjectIssues.DevIndex", "Dev Status Update"),
                ("ProjectIssues.ViewOnly", "Issue Report"),
                ("PhaseAssigns.Print", "Assigned Employees Report"),
                ("PhaseStatusReport.Index", "Phase Status Report"),
                ("PhaseStatusReport.Timeline", "Timeline / Gantt"),
                ("Dashboard.Workload", "Employee Workload"),
                ("IssueDashboard.Index", "Issue Dashboard"),
                ("UserManagement.Index", "User Management"),
                ("UserManagement.Permissions", "Permissions"),
            };

            var selected = await _context.UserMenus
                .AsNoTracking()
                .Where(x => x.Username == username && x.MenuKey != null && x.MenuKey != "")
                .Select(x => x.MenuKey)
                .ToListAsync();

            var selectedSet = new HashSet<string>(
                selected.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase
            );

            ViewBag.Username = username;
            ViewBag.AllMenus = allMenus;
            ViewBag.SelectedMenus = selectedSet;

            return View();
        }

        // =====================================================
        // PERMISSIONS (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Permissions(string username, List<string>? menus)
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            username = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
                return RedirectToAction(nameof(Index));

            var userExists = await _context.LoginUsers.AsNoTracking().AnyAsync(x => x.Username == username);
            if (!userExists) return NotFound();

            var oldRows = await _context.UserMenus
                .Where(x => x.Username == username)
                .ToListAsync();

            if (oldRows.Any())
                _context.UserMenus.RemoveRange(oldRows);

            var cleaned = (menus ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var key in cleaned)
            {
                _context.UserMenus.Add(new UserMenu
                {
                    Username = username,
                    MenuKey = key
                });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "✅ บันทึกสิทธิเมนูเรียบร้อย";
            return RedirectToAction(nameof(Permissions), new { username });
        }
    }
}