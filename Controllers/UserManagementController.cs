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

            await SendVerifyEmailSafeAsync(username, email, token, isReverify: false);

            return RedirectToAction("Index");
        }

        private async Task SendVerifyEmailSafeAsync(string username, string email, string token, bool isReverify)
        {
            var verifyUrl = $"{Request.Scheme}://{Request.Host}/Auth/VerifyEmail?token={Uri.EscapeDataString(token)}&username={Uri.EscapeDataString(username)}";
            var subject = "Verify your email - ProjectTracking";

            var body = isReverify
                ? $@"
สวัสดี {username}

มีการแก้ไขข้อมูลผู้ใช้ กรุณายืนยันอีเมลอีกครั้ง (ภายใน 24 ชั่วโมง):
{verifyUrl}

หากคุณไม่ได้เป็นผู้ขอแก้ไข สามารถละเว้นอีเมลนี้ได้
"
                : $@"
สวัสดี {username}

กรุณายืนยันอีเมล โดยคลิกลิงก์ด้านล่าง (ภายใน 24 ชั่วโมง):
{verifyUrl}

หากคุณไม่ได้เป็นผู้ขอสร้างบัญชีนี้ สามารถละเว้นอีเมลนี้ได้
";

            try
            {
                await _emailService.SendAsync(email, subject, body);
                TempData["Success"] = isReverify
                    ? "✅ แก้ไขผู้ใช้แล้ว และส่งอีเมลยืนยันใหม่เรียบร้อย"
                    : "✅ สร้างผู้ใช้แล้ว และส่งอีเมลยืนยันเรียบร้อย";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Verify email send failed. Username={Username}, Email={Email}", username, email);
                TempData["Error"] = isReverify
                    ? "⚠️ แก้ไขผู้ใช้แล้ว แต่ส่งอีเมลยืนยันใหม่ไม่สำเร็จ (โปรดตรวจสอบการตั้งค่า SMTP/Email Service)"
                    : "⚠️ สร้างผู้ใช้แล้ว แต่ส่งอีเมลยืนยันไม่สำเร็จ (โปรดตรวจสอบการตั้งค่า SMTP/Email Service)";
            }
        }

        // =====================================================
        // EDIT (GET)
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Edit(string username)
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

            // ✅ Use dedicated view: Views/UserManagement/Edit.cshtml
            return View(user);
        }

        // =====================================================
        // EDIT (POST) - Force re-verify after edit
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            string originalUsername,
            string email,
            string password,
            string confirmPassword,
            string role,
            string status)
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            originalUsername = (originalUsername ?? "").Trim();
            email = (email ?? "").Trim();
            password = (password ?? "").Trim();
            confirmPassword = (confirmPassword ?? "").Trim();
            role = (role ?? "USER").Trim().ToUpperInvariant();
            status = (status ?? "ACTIVE").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(originalUsername))
            {
                TempData["Error"] = "❌ ไม่พบ Username สำหรับแก้ไข";
                return RedirectToAction(nameof(Index));
            }

            var user = await _context.LoginUsers.FirstOrDefaultAsync(x => x.Username == originalUsername);
            if (user == null) return NotFound();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                ViewBag.Error = "❌ รูปแบบ Email ไม่ถูกต้อง";
                user.Email = email;
                user.Role = role;
                user.Status = status;
                return View("Edit", user);
            }

            if (role != "USER" && role != "ADMIN") role = "USER";
            if (status != "ACTIVE" && status != "INACTIVE") status = "ACTIVE";

            // Prevent duplicate email with other users
            var emailExists = await _context.LoginUsers.AsNoTracking()
                .AnyAsync(u => u.Email == email && u.Username != originalUsername);
            if (emailExists)
            {
                ViewBag.Error = "❌ Email นี้มีอยู่แล้ว";
                user.Email = email;
                user.Role = role;
                user.Status = status;
                return View("Edit", user);
            }

            // Update fields
            user.Email = email;
            user.Role = role;
            user.Status = status;

            // Optional password change
            if (!string.IsNullOrWhiteSpace(password) || !string.IsNullOrWhiteSpace(confirmPassword))
            {
                if (password != confirmPassword)
                {
                    ViewBag.Error = "❌ Password และ Confirm Password ไม่ตรงกัน";
                    user.Email = email;
                    user.Role = role;
                    user.Status = status;
                    return View("Edit", user);
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    ViewBag.Error = "❌ กรุณากรอก Password";
                    user.Email = email;
                    user.Role = role;
                    user.Status = status;
                    return View("Edit", user);
                }

                user.Password = SecurityHelper.HashPassword(password);
            }

            // ✅ Force re-verify
            user.EmailVerified = false;
            var token = SecurityHelper.GenerateToken(32);
            user.VerifyTokenHash = SecurityHelper.Sha256(token);
            user.VerifyTokenExpire = DateTime.Now.AddHours(24);

            await _context.SaveChangesAsync();
            await SendVerifyEmailSafeAsync(user.Username, email, token, isReverify: true);

            return RedirectToAction(nameof(Index));
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

        // =====================================================
        // DELETE (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string username)
        {
            var guard = GuardAdmin();
            if (guard != null) return guard;

            username = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
                return RedirectToAction(nameof(Index));

            // Optional: prevent deleting yourself (comment out if you want to allow)
            var currentUsername = (HttpContext.Session.GetString("Username") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(currentUsername) &&
                currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "❌ ไม่สามารถลบผู้ใช้ที่กำลังล็อกอินอยู่ได้";
                return RedirectToAction(nameof(Index));
            }

            var user = await _context.LoginUsers.FirstOrDefaultAsync(x => x.Username == username);
            if (user == null)
            {
                TempData["Error"] = "❌ ไม่พบผู้ใช้งาน";
                return RedirectToAction(nameof(Index));
            }

            // Remove permissions first
            var menus = await _context.UserMenus.Where(x => x.Username == username).ToListAsync();
            if (menus.Any())
                _context.UserMenus.RemoveRange(menus);

            _context.LoginUsers.Remove(user);

            try
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = $"✅ ลบผู้ใช้งาน {username} เรียบร้อย";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete user failed. Username={Username}", username);
                TempData["Error"] = "⚠️ ลบผู้ใช้งานไม่สำเร็จ (โปรดตรวจสอบฐานข้อมูล/ความสัมพันธ์ข้อมูล)";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}