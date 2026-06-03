using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Helpers;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    public class AuthController : Controller
    {
        private const string DefaultProfileImagePath = "/images/Profile/profile.png";

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly OverdueNotificationService _notificationService;

        public AuthController(
            AppDbContext context,
            IWebHostEnvironment env,
            OverdueNotificationService notificationService)
        {
            _context = context;
            _env = env;
            _notificationService = notificationService;
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
            HttpContext.Session.SetString("ProfileImagePath", ResolveProfileImagePath(user.ProfileImagePath));

            // ✅ Load menu permissions for this user
            // NOTE: Normalize username/menu keys (trim + case-insensitive) to avoid missing permissions
            var uname = (user.Username ?? "").Trim();

            var menusRaw = await _context.UserMenus
                .AsNoTracking()
                .Where(x => x.Username != null && x.Username.Trim().ToLower() == uname.ToLower())
                .Select(x => x.MenuKey)
                .ToListAsync();

            var menus = menusRaw
                .Select(m => (m ?? "").Trim())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            HttpContext.Session.SetString("Menus", string.Join(",", menus));
            HttpContext.Session.SetString("ShowLoginFollowupPopup", "1");

            await SyncNotificationsSafelyAsync();

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

        [HttpGet]
        public IActionResult AccessDenied(string? key = null)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            var menuKey = string.IsNullOrWhiteSpace(key) ? "-" : key.Trim();
            return Content($"ไม่มีสิทธิ์เข้าถึงเมนูนี้ ({menuKey}) กรุณาติดต่อผู้ดูแลระบบ", "text/plain; charset=utf-8");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfileImage(IFormFile? profileImage, string? croppedProfileImage = null, string? returnUrl = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", new { returnUrl = "/" });

            var redirectUrl = GetSafeReturnUrl(returnUrl);
            var hasCroppedImage = !string.IsNullOrWhiteSpace(croppedProfileImage);

            if (!hasCroppedImage && (profileImage == null || profileImage.Length == 0))
            {
                TempData["ProfileError"] = "กรุณาเลือกรูปโปรไฟล์";
                return LocalRedirect(redirectUrl);
            }

            const long maxFileSize = 5 * 1024 * 1024;
            byte[]? profileImageBytes = null;
            var extension = ".jpg";

            if (hasCroppedImage)
            {
                if (!TryParseProfileImageDataUrl(croppedProfileImage!, out profileImageBytes, out extension))
                {
                    TempData["ProfileError"] = "ไม่สามารถอ่านรูปที่จัดตำแหน่งได้";
                    return LocalRedirect(redirectUrl);
                }

                if (profileImageBytes.Length == 0 || profileImageBytes.Length > maxFileSize)
                {
                    TempData["ProfileError"] = "ขนาดรูปต้องไม่เกิน 5 MB";
                    return LocalRedirect(redirectUrl);
                }
            }
            else if (profileImage!.Length > maxFileSize)
            {
                TempData["ProfileError"] = "ขนาดรูปต้องไม่เกิน 5 MB";
                return LocalRedirect(redirectUrl);
            }

            if (!hasCroppedImage)
            {
                extension = Path.GetExtension(profileImage!.FileName).ToLowerInvariant();
                var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".webp"
                };

                if (!allowedExtensions.Contains(extension))
                {
                    TempData["ProfileError"] = "รองรับเฉพาะไฟล์ JPG, PNG หรือ WEBP";
                    return LocalRedirect(redirectUrl);
                }

                if (!string.IsNullOrWhiteSpace(profileImage.ContentType)
                    && !profileImage.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ProfileError"] = "ไฟล์ที่เลือกไม่ใช่รูปภาพ";
                    return LocalRedirect(redirectUrl);
                }
            }

            var user = await _context.LoginUsers.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }

            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadFolder = Path.Combine(webRoot, "uploads", "profiles", user.UserId.ToString());
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadFolder, fileName);

            if (profileImageBytes != null)
            {
                await System.IO.File.WriteAllBytesAsync(fullPath, profileImageBytes);
            }
            else
            {
                await using var stream = System.IO.File.Create(fullPath);
                await profileImage!.CopyToAsync(stream);
            }

            var dbPath = $"/uploads/profiles/{user.UserId}/{fileName}";
            var oldPath = user.ProfileImagePath;

            user.ProfileImagePath = dbPath;
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("ProfileImagePath", dbPath);
            TryDeleteOldProfileImage(webRoot, user.UserId, oldPath);

            TempData["ProfileSuccess"] = "เปลี่ยนรูปโปรไฟล์เรียบร้อยแล้ว";
            return LocalRedirect(redirectUrl);
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

        private string GetSafeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl)) return "/";

            returnUrl = returnUrl.Trim();
            if (!Url.IsLocalUrl(returnUrl)) return "/";
            if (returnUrl.StartsWith("/Auth/Login", StringComparison.OrdinalIgnoreCase)) return "/";
            if (returnUrl.StartsWith("/Auth/UpdateProfileImage", StringComparison.OrdinalIgnoreCase)) return "/";

            return returnUrl;
        }

        private async Task SyncNotificationsSafelyAsync()
        {
            try
            {
                await _notificationService.SyncAsync(HttpContext.RequestAborted);
            }
            catch
            {
                // Notification sync should not block login.
            }
        }

        private static string ResolveProfileImagePath(string? profileImagePath)
        {
            if (string.IsNullOrWhiteSpace(profileImagePath))
                return DefaultProfileImagePath;

            var path = profileImagePath.Trim();
            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private static bool TryParseProfileImageDataUrl(string dataUrl, out byte[] bytes, out string extension)
        {
            bytes = Array.Empty<byte>();
            extension = ".jpg";

            var commaIndex = dataUrl.IndexOf(',');
            if (commaIndex <= 0) return false;

            var header = dataUrl[..commaIndex].Trim().ToLowerInvariant();
            var payload = dataUrl[(commaIndex + 1)..];

            extension = header switch
            {
                "data:image/jpeg;base64" => ".jpg",
                "data:image/jpg;base64" => ".jpg",
                "data:image/png;base64" => ".png",
                "data:image/webp;base64" => ".webp",
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(extension)) return false;

            try
            {
                bytes = Convert.FromBase64String(payload);
                return true;
            }
            catch (FormatException)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }

        private static void TryDeleteOldProfileImage(string webRoot, int userId, string? oldPath)
        {
            if (string.IsNullOrWhiteSpace(oldPath)) return;
            if (!oldPath.StartsWith($"/uploads/profiles/{userId}/", StringComparison.OrdinalIgnoreCase)) return;

            var relativePath = oldPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(webRoot, relativePath);

            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
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
