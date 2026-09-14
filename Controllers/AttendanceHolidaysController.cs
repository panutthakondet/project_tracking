using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class AttendanceHolidaysController : BaseController
    {
        private const string PermissionKey = "AttendanceHolidays.Index";
        private readonly AppDbContext _context;

        public AttendanceHolidaysController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [RequireMenu(PermissionKey)]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [RequireMenu(PermissionKey)]
        public async Task<IActionResult> Events(CancellationToken cancellationToken)
        {
            var holidays = await _context.AttendanceHolidays
                .AsNoTracking()
                .OrderBy(x => x.HolidayDate)
                .ToListAsync(cancellationToken);

            return Json(holidays.Select(x => new
            {
                id = x.HolidayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                title = x.HolidayName,
                start = x.HolidayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                allDay = true,
                backgroundColor = x.IsActive ? "#fff7f7" : "#f1f5f9",
                borderColor = x.IsActive ? "#ef4444" : "#94a3b8",
                textColor = "#111827",
                extendedProps = new
                {
                    holidayDate = x.HolidayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    holidayName = x.HolidayName,
                    isActive = x.IsActive
                }
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu(PermissionKey)]
        public async Task<IActionResult> Save(
            string? holidayDate,
            string? holidayName,
            string? originalDate,
            bool isActive,
            CancellationToken cancellationToken)
        {
            if (!TryParseDate(holidayDate, out var targetDate))
                return BadRequest(new { success = false, message = "กรุณาระบุวันที่ให้ถูกต้อง" });

            holidayName = (holidayName ?? string.Empty).Trim();
            if (holidayName.Length == 0)
                return BadRequest(new { success = false, message = "กรุณาระบุชื่อวันหยุด" });
            if (holidayName.Length > 200)
                return BadRequest(new { success = false, message = "ชื่อวันหยุดต้องไม่เกิน 200 ตัวอักษร" });

            var isEdit = TryParseDate(originalDate, out var sourceDate);
            AttendanceHoliday? holiday;

            if (!isEdit)
            {
                var duplicate = await _context.AttendanceHolidays
                    .AsNoTracking()
                    .AnyAsync(x => x.HolidayDate == targetDate, cancellationToken);
                if (duplicate)
                    return Conflict(new { success = false, message = "วันที่นี้มีรายการวันหยุดอยู่แล้ว" });

                holiday = new AttendanceHoliday
                {
                    HolidayDate = targetDate,
                    HolidayName = holidayName,
                    IsActive = isActive,
                    CreatedAt = DateTime.Now
                };
                _context.AttendanceHolidays.Add(holiday);
            }
            else
            {
                holiday = await _context.AttendanceHolidays
                    .FirstOrDefaultAsync(x => x.HolidayDate == sourceDate, cancellationToken);
                if (holiday == null)
                    return NotFound(new { success = false, message = "ไม่พบรายการวันหยุดที่ต้องการแก้ไข" });

                if (sourceDate != targetDate)
                {
                    var duplicate = await _context.AttendanceHolidays
                        .AsNoTracking()
                        .AnyAsync(x => x.HolidayDate == targetDate, cancellationToken);
                    if (duplicate)
                        return Conflict(new { success = false, message = "วันที่ใหม่มีรายการวันหยุดอยู่แล้ว" });

                    _context.AttendanceHolidays.Remove(holiday);
                    holiday = new AttendanceHoliday
                    {
                        HolidayDate = targetDate,
                        HolidayName = holidayName,
                        IsActive = isActive,
                        CreatedAt = holiday.CreatedAt == default ? DateTime.Now : holiday.CreatedAt
                    };
                    _context.AttendanceHolidays.Add(holiday);
                }
                else
                {
                    holiday.HolidayName = holidayName;
                    holiday.IsActive = isActive;
                }
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Conflict(new { success = false, message = "บันทึกไม่สำเร็จ วันที่นี้อาจมีรายการอยู่แล้ว" });
            }

            return Json(new
            {
                success = true,
                message = isEdit ? "แก้ไขวันหยุดเรียบร้อยแล้ว" : "เพิ่มวันหยุดเรียบร้อยแล้ว"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu(PermissionKey)]
        public async Task<IActionResult> Toggle(
            string? holidayDate,
            CancellationToken cancellationToken)
        {
            if (!TryParseDate(holidayDate, out var date))
                return BadRequest(new { success = false, message = "วันที่ไม่ถูกต้อง" });

            var holiday = await _context.AttendanceHolidays
                .FirstOrDefaultAsync(x => x.HolidayDate == date, cancellationToken);
            if (holiday == null)
                return NotFound(new { success = false, message = "ไม่พบรายการวันหยุด" });

            holiday.IsActive = !holiday.IsActive;
            await _context.SaveChangesAsync(cancellationToken);

            return Json(new
            {
                success = true,
                isActive = holiday.IsActive,
                message = holiday.IsActive ? "เปิดใช้งานวันหยุดแล้ว" : "ปิดใช้งานวันหยุดแล้ว"
            });
        }

        private static bool TryParseDate(string? value, out DateTime date)
        {
            if (DateTime.TryParseExact(
                    (value ?? string.Empty).Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                date = parsed.Date;
                return true;
            }

            date = default;
            return false;
        }
    }
}
