using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Helpers;

namespace ProjectTracking.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        // =====================
        // LOGIN PAGE
        // =====================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (HttpContext.Session.GetInt32("UserId") != null)
                return RedirectToAction("Index", "Home");

            ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            return View();
        }

        // =====================
        // LOGIN POST
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            username = (username ?? "").Trim();
            password = (password ?? "").Trim();
            returnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl.Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "❌ กรุณากรอก Username และ Password";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // ✅ ดึง user มาก่อน แล้วค่อย verify password (รองรับ legacy SHA256 และ PBKDF2 ใหม่)
            // ดึงแบบ tracked เพราะอาจต้อง update email_verified / upgrade password hash
            var user = await _context.LoginUsers
                .FirstOrDefaultAsync(u =>
                    u.Username == username &&
                    u.Status == "ACTIVE"
                );

            if (user == null || !SecurityHelper.VerifyPassword(password, user.Password))
            {
                ViewBag.Error = "❌ Username หรือ Password ไม่ถูกต้อง";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // ✅ ถ้าเป็น password แบบเก่า (SHA256) แล้ว login ผ่าน -> upgrade เป็น PBKDF2 ทันที
            if (SecurityHelper.IsLegacyPasswordHash(user.Password))
            {
                user.Password = SecurityHelper.HashPassword(password);
                await _context.SaveChangesAsync();
            }

            // ✅ ถ้ายังไม่ verify: อนุญาตให้ “verify ผ่าน returnUrl” ได้เท่านั้น
            if (!user.EmailVerified)
            {
                if (IsVerifyEmailReturnUrl(returnUrl))
                {
                    var ok = await VerifyFromReturnUrlAsync(user, returnUrl);

                    if (!ok)
                    {
                        ViewBag.Error = "❌ ลิงก์ยืนยันอีเมลไม่ถูกต้อง/หมดอายุ (ให้ Admin ส่งใหม่)";
                        ViewBag.ReturnUrl = "/";
                        return View();
                    }

                    // verify สำเร็จแล้ว → ให้ login ต่อได้
                    TempData["Success"] = "✅ ยืนยันอีเมลสำเร็จแล้ว";
                }
                else
                {
                    ViewBag.Error = "⚠️ ยังไม่ได้ยืนยันอีเมล กรุณาตรวจสอบอีเมลและกดลิงก์ยืนยันก่อนเข้าสู่ระบบ";
                    ViewBag.ReturnUrl = returnUrl;
                    return View();
                }
            }

            // ✅ สร้าง session หลังผ่านเงื่อนไขทั้งหมด
            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("Username", user.Username ?? "");
            HttpContext.Session.SetString("Role", user.Role ?? "");

            // ✅ Load menu permissions for this user
            var menus = await _context.UserMenus
                .AsNoTracking()
                .Where(x => x.Username == (user.Username ?? ""))
                .Select(x => x.MenuKey)
                .ToListAsync();

            HttpContext.Session.SetString("Menus", string.Join(",", menus));

            // ✅ กัน open redirect
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && !IsVerifyEmailReturnUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        // =====================
        // LOGOUT
        // =====================
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ============================================================
        // ✅ VERIFY EMAIL (ตาม requirement: ต้องผ่านหน้า Login ก่อนเสมอ)
        // ============================================================
        [HttpGet]
        public IActionResult VerifyEmail(string token, string username)
        {
            // ❗ไม่ทำ verify ตรงนี้แล้ว เพื่อบังคับให้ “ผ่านหน้า Login ก่อนเสมอ”
            // ส่งไปหน้า Login พร้อม returnUrl กลับมาที่ VerifyEmail
            var returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;
            return RedirectToAction("Login", "Auth", new { returnUrl = returnUrl.ToString() });
        }

        // =====================
        // Helpers
        // =====================
        private static bool IsVerifyEmailReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl)) return false;
            returnUrl = returnUrl.Trim();
            return returnUrl.StartsWith("/Auth/VerifyEmail", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> VerifyFromReturnUrlAsync(dynamic user, string returnUrl)
        {
            // returnUrl ตัวอย่าง: /Auth/VerifyEmail?token=...&username=...
            // แยก path/query
            var path = returnUrl;
            var query = "";
            var qIndex = returnUrl.IndexOf('?');
            if (qIndex >= 0)
            {
                path = returnUrl.Substring(0, qIndex);
                query = returnUrl.Substring(qIndex);
            }

            if (!path.Equals("/Auth/VerifyEmail", StringComparison.OrdinalIgnoreCase))
                return false;

            var parsed = QueryHelpers.ParseQuery(query);

            var token = parsed.TryGetValue("token", out var t) ? t.ToString() : "";
            var uname = parsed.TryGetValue("username", out var u) ? u.ToString() : "";

            token = (token ?? "").Trim();
            uname = (uname ?? "").Trim();

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(uname))
                return false;

            // ✅ ป้องกัน verify ข้าม user: ต้องตรงกับคนที่ login อยู่
            if (!string.Equals(uname, (string)user.Username, StringComparison.OrdinalIgnoreCase))
                return false;

            // ✅ ตรวจ token
            var tokenHash = SecurityHelper.Sha256(token);

            if (string.IsNullOrWhiteSpace((string?)user.VerifyTokenHash))
                return false;

            if (!string.Equals((string?)user.VerifyTokenHash, tokenHash, StringComparison.OrdinalIgnoreCase))
                return false;

            if (user.VerifyTokenExpire == null || user.VerifyTokenExpire < DateTime.Now)
                return false;

            // ✅ update DB
            user.EmailVerified = true;
            user.VerifyTokenHash = null;
            user.VerifyTokenExpire = null;

            await _context.SaveChangesAsync();
            return true;
        }
    }
}