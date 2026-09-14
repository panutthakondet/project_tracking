using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class AttendanceController : Controller
    {
        private readonly AppDbContext _context;
        private readonly AttendancePolicyService _attendancePolicy;

        public AttendanceController(AppDbContext context, AttendancePolicyService attendancePolicy)
        {
            _context = context;
            _attendancePolicy = attendancePolicy;
        }

        [RequireMenu("Attendance.Index")]
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var today = AttendancePolicyService.ThailandToday;

            ViewBag.WorkDate = FormatThaiDate(today);
            ViewBag.EmployeeName = "-";
            ViewBag.CheckInTime = "-";
            ViewBag.CheckOutTime = "-";
            ViewBag.DistanceKm = "-";
            ViewBag.HasCheckIn = false;
            ViewBag.HasCheckOut = false;

            if (userId.HasValue)
            {
                var employee = await ResolveCurrentEmployeeAsync(userId.Value, asTracking: false);
                if (employee != null)
                {
                    ViewBag.EmployeeName = employee.EmpName ?? "-";
                    var record = await _context.Attendances
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.EmpId == employee.EmpId && x.WorkDate == today);

                    if (record != null)
                    {
                        ViewBag.HasCheckIn = record.CheckinTime.HasValue;
                        ViewBag.HasCheckOut = record.CheckoutTime.HasValue;
                        ViewBag.CheckInTime = record.CheckinTime?.ToString("HH:mm") ?? "-";
                        ViewBag.CheckOutTime = record.CheckoutTime?.ToString("HH:mm") ?? "-";
                        ViewBag.DistanceKm = record.DistanceKm?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";
                    }
                }
            }

            return View();
        }

        [HttpPost]
        [RequireMenu("Attendance.Index")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromBody] AttendanceCheckDto dto)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
            if (dto == null || !IsValidCoordinate(dto.Lat, dto.Lng))
                return Json(new { success = false, message = "ไม่พบพิกัดที่ถูกต้อง กรุณาเปิด Location / GPS แล้วลองใหม่" });

            var type = (dto.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (type is not ("checkin" or "checkout"))
                return Json(new { success = false, message = "ประเภทการเช็คไม่ถูกต้อง" });

            var policy = await _attendancePolicy.GetAsync();
            if (!dto.Accuracy.HasValue || !double.IsFinite(dto.Accuracy.Value) || dto.Accuracy <= 0)
                return Json(new { success = false, message = "อุปกรณ์ไม่ส่งค่าความแม่นยำ GPS กรุณารอให้ระบุตำแหน่งได้ชัดเจนแล้วลองใหม่" });
            if (dto.Accuracy > policy.MaximumGpsAccuracyM)
                return Json(new
                {
                    success = false,
                    message = $"ตำแหน่ง GPS คลาดเคลื่อนประมาณ {dto.Accuracy.Value:0} เมตร เกินค่าที่กำหนด {policy.MaximumGpsAccuracyM:0} เมตร กรุณาออกไปยังพื้นที่เปิดแล้วลองใหม่"
                });

            var employee = await ResolveCurrentEmployeeAsync(userId.Value, asTracking: false);
            if (employee == null)
                return Json(new { success = false, message = "ไม่พบข้อมูลพนักงานที่เชื่อมกับบัญชีผู้ใช้" });

            var now = AttendancePolicyService.ThailandNow;
            var today = now.Date;
            var record = await _context.Attendances
                .FirstOrDefaultAsync(x => x.EmpId == employee.EmpId && x.WorkDate == today);

            if (type == "checkin")
            {
                if (record?.CheckinTime != null)
                    return Json(new { success = false, message = "วันนี้เช็คเข้างานแล้ว" });

                record ??= new Attendance { EmpId = employee.EmpId, WorkDate = today };
                record.CheckinTime = now;
                record.CheckinLat = (decimal)dto.Lat;
                record.CheckinLng = (decimal)dto.Lng;
                record.CheckinAccuracyM = (decimal)dto.Accuracy.Value;
                if (record.Id == 0)
                    _context.Attendances.Add(record);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    return Json(new { success = false, message = "มีการบันทึกเข้างานของวันนี้แล้ว กรุณารีเฟรชหน้าจอ" });
                }

                return Json(new
                {
                    success = true,
                    type,
                    checkinTime = record.CheckinTime?.ToString("HH:mm"),
                    accuracyM = record.CheckinAccuracyM
                });
            }

            if (record?.CheckinTime == null)
                return Json(new { success = false, message = "กรุณาเช็คเข้างานก่อน" });
            if (record.CheckoutTime != null)
                return Json(new { success = false, message = "วันนี้เช็คออกงานแล้ว" });
            if (!record.CheckinLat.HasValue || !record.CheckinLng.HasValue
                || !IsValidCoordinate((double)record.CheckinLat.Value, (double)record.CheckinLng.Value))
                return Json(new { success = false, message = "ข้อมูลพิกัดเข้างานไม่สมบูรณ์ กรุณาติดต่อผู้ดูแลระบบ" });

            var distance = GetDistanceKm(
                (double)record.CheckinLat.Value,
                (double)record.CheckinLng.Value,
                dto.Lat,
                dto.Lng);

            if (distance > policy.MaximumCheckoutDistanceKm)
            {
                return Json(new
                {
                    success = false,
                    message = $"ตำแหน่งออกงานห่างจากจุดเข้างาน {distance:0.00} กม. เกินค่าที่กำหนด {policy.MaximumCheckoutDistanceKm:0.##} กม."
                });
            }

            record.DistanceKm = (decimal)distance;
            record.CheckoutTime = now;
            record.CheckoutLat = (decimal)dto.Lat;
            record.CheckoutLng = (decimal)dto.Lng;
            record.CheckoutAccuracyM = (decimal)dto.Accuracy.Value;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                type,
                distanceKm = record.DistanceKm,
                checkinTime = record.CheckinTime?.ToString("HH:mm"),
                checkoutTime = record.CheckoutTime?.ToString("HH:mm"),
                accuracyM = record.CheckoutAccuracyM
            });
        }

        [RequireMenu("Attendance.Map")]
        public async Task<IActionResult> Map(string? fromDate, string? toDate, int? departmentId)
        {
            departmentId = await ReportDepartmentSupport.LoadAsync(this, _context, departmentId);
            var today = AttendancePolicyService.ThailandToday;
            var policy = await _attendancePolicy.GetAsync();
            var validationMessage = string.Empty;
            var hasInputError = false;

            var hasValidFromDate = TryParseThaiDate(fromDate, out var parsedStart);
            var hasValidToDate = TryParseThaiDate(toDate, out var parsedEnd);
            var start = hasValidFromDate ? parsedStart : today;
            var end = hasValidToDate ? parsedEnd : today;

            if (!string.IsNullOrWhiteSpace(fromDate) && !hasValidFromDate
                || !string.IsNullOrWhiteSpace(toDate) && !hasValidToDate)
            {
                validationMessage = "รูปแบบวันที่ไม่ถูกต้อง กรุณาระบุเป็น วว/ดด/พ.ศ. เช่น 14/09/2569";
                hasInputError = true;
            }
            else if (start > end)
            {
                validationMessage = "วันที่เริ่มต้องไม่มากกว่าวันที่สิ้นสุด";
                hasInputError = true;
            }
            else if ((end - start).Days + 1 > policy.MaximumReportDays)
            {
                validationMessage = $"เลือกช่วงวันที่ได้ไม่เกิน {policy.MaximumReportDays:N0} วัน";
                hasInputError = true;
            }

            if (end > today)
            {
                end = today;
                validationMessage = string.IsNullOrEmpty(validationMessage)
                    ? "ระบบแสดงข้อมูลถึงวันที่ปัจจุบัน เพื่อไม่ให้นับวันในอนาคตเป็นการขาดงาน"
                    : validationMessage;
            }

            var rows = new List<AttendanceReportRowViewModel>();
            if (!hasInputError && start <= end)
            {
                var employees = await _context.Employees
                    .AsNoTracking()
                    .Where(x => x.Status == "ACTIVE")
                    .Where(x => !departmentId.HasValue || x.DepartmentId == departmentId.Value)
                    .Select(x => new { x.EmpId, x.EmpName, x.Position })
                    .OrderBy(x => x.EmpName)
                    .ToListAsync();

                var employeeIds = employees.Select(x => x.EmpId).ToList();
                var holidays = await _attendancePolicy.GetHolidayDatesAsync(start, end);
                var dates = Enumerable.Range(0, (end - start).Days + 1)
                    .Select(offset => start.AddDays(offset))
                    .Where(date => AttendancePolicyService.IsWorkday(date, holidays))
                    .ToList();

                if (end == today && AttendancePolicyService.ThailandNow.TimeOfDay < policy.WorkStart.Add(TimeSpan.FromMinutes(policy.LateGraceMinutes)))
                    dates.Remove(today);

                var attendances = await _context.Attendances
                    .AsNoTracking()
                    .Where(x => employeeIds.Contains(x.EmpId) && x.WorkDate >= start && x.WorkDate <= end)
                    .ToListAsync();
                var attendanceLookup = attendances.ToDictionary(x => (x.EmpId, x.WorkDate.Date));

                foreach (var date in dates)
                {
                    foreach (var employee in employees)
                    {
                        attendanceLookup.TryGetValue((employee.EmpId, date.Date), out var attendance);
                        rows.Add(new AttendanceReportRowViewModel
                        {
                            EmpId = employee.EmpId,
                            EmpName = employee.EmpName ?? "-",
                            Position = employee.Position,
                            WorkDate = date,
                            CheckinLat = attendance?.CheckinLat,
                            CheckinLng = attendance?.CheckinLng,
                            CheckinAccuracyM = attendance?.CheckinAccuracyM,
                            CheckoutLat = attendance?.CheckoutLat,
                            CheckoutLng = attendance?.CheckoutLng,
                            CheckoutAccuracyM = attendance?.CheckoutAccuracyM,
                            CheckinTime = attendance?.CheckinTime,
                            CheckoutTime = attendance?.CheckoutTime,
                            DistanceKm = attendance?.DistanceKm
                        });
                    }
                }
            }

            return View(new AttendanceReportViewModel
            {
                FromDate = FormatThaiDate(start),
                ToDate = FormatThaiDate(end),
                ValidationMessage = string.IsNullOrEmpty(validationMessage) ? null : validationMessage,
                MaximumRangeDays = policy.MaximumReportDays,
                Rows = rows
            });
        }

        private async Task<Employee?> ResolveCurrentEmployeeAsync(int userId, bool asTracking)
        {
            var sessionEmpId = HttpContext.Session.GetInt32("EmpId");
            var loginEmpId = await _context.LoginUsers
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.EmpId)
                .FirstOrDefaultAsync();
            var resolvedEmpId = sessionEmpId ?? loginEmpId;

            IQueryable<Employee> query = _context.Employees;
            if (!asTracking)
                query = query.AsNoTracking();

            var employee = await query.FirstOrDefaultAsync(x =>
                (resolvedEmpId.HasValue && x.EmpId == resolvedEmpId.Value)
                || x.LoginUserId == userId);

            if (employee != null && sessionEmpId != employee.EmpId)
                HttpContext.Session.SetInt32("EmpId", employee.EmpId);

            return employee;
        }

        private static bool IsValidCoordinate(double latitude, double longitude) =>
            double.IsFinite(latitude)
            && double.IsFinite(longitude)
            && latitude is >= -90 and <= 90
            && longitude is >= -180 and <= 180
            && !(Math.Abs(latitude) < double.Epsilon && Math.Abs(longitude) < double.Epsilon);

        private static double GetDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                    + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                    * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static bool TryParseThaiDate(string? value, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value)
                || !DateTime.TryParseExact(value.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return false;

            var year = parsed.Year > 2400 ? parsed.Year - 543 : parsed.Year;
            if (year is < 1900 or > 2200)
                return false;

            date = new DateTime(year, parsed.Month, parsed.Day);
            return true;
        }

        private static string FormatThaiDate(DateTime date) =>
            $"{date:dd/MM}/{date.Year + 543}";
    }
}
