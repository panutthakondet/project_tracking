using Microsoft.EntityFrameworkCore;
using ProjectTracking.Models;
using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Data;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class AttendanceController : Controller
    {
        private readonly AppDbContext _context;

        public AttendanceController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("Attendance.Index")]
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var today = DateTime.Today;

            ViewBag.WorkDate = today.ToString("dd/MM/yyyy");
            ViewBag.EmployeeName = "-";
            ViewBag.CheckInTime = "-";
            ViewBag.CheckOutTime = "-";
            ViewBag.DistanceKm = "-";
            ViewBag.HasCheckIn = false;
            ViewBag.HasCheckOut = false;

            if (userId != null)
            {
                var emp = await ResolveCurrentEmployeeAsync(userId.Value, asTracking: false);

                if (emp != null)
                {
                    ViewBag.EmployeeName = emp.EmpName ?? "-";

                    var record = await _context.Attendances
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.EmpId == emp.EmpId && x.WorkDate == today);

                    if (record != null)
                    {
                        ViewBag.HasCheckIn = record.CheckinTime.HasValue;
                        ViewBag.HasCheckOut = record.CheckoutTime.HasValue;
                        ViewBag.CheckInTime = record.CheckinTime?.ToString("HH:mm") ?? "-";
                        ViewBag.CheckOutTime = record.CheckoutTime?.ToString("HH:mm") ?? "-";
                        ViewBag.DistanceKm = record.DistanceKm.HasValue
                            ? record.DistanceKm.Value.ToString("0.00")
                            : "-";
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

            if (dto == null)
            {
                return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });
            }

            if (dto.Lat == 0 || dto.Lng == 0)
            {
                return Json(new { success = false, message = "ไม่พบตำแหน่ง กรุณาเปิด GPS" });
            }

            if (userId == null)
                return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });

            // map user -> employee (supports both legacy and current account links)
            var emp = await ResolveCurrentEmployeeAsync(userId.Value, asTracking: true);

            if (emp == null)
                return Json(new { success = false, message = "ไม่พบข้อมูลพนักงาน" });

            var empId = emp.EmpId;
            var today = DateTime.Today;

            // find today's record
            var record = await _context.Attendances
                .FirstOrDefaultAsync(x => x.EmpId == empId && x.WorkDate == today);

            // CHECK-IN
            if (dto.Type == "checkin")
            {
                if (record != null && record.CheckinTime != null)
                {
                    return Json(new { success = false, message = "วันนี้เช็คเข้างานแล้ว" });
                }

                var newRecord = record ?? new Attendance
                {
                    EmpId = empId,
                    WorkDate = today
                };

                newRecord.CheckinTime = DateTime.Now;
                newRecord.CheckinLat = (decimal)dto.Lat;
                newRecord.CheckinLng = (decimal)dto.Lng;

                if (record == null)
                    _context.Attendances.Add(newRecord);

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    type = "checkin",
                    checkinTime = newRecord.CheckinTime?.ToString("HH:mm")
                });
            }

            // CHECK-OUT
            if (dto.Type == "checkout")
            {
                if (record == null || record.CheckinTime == null)
                {
                    return Json(new { success = false, message = "กรุณาเช็คเข้างานก่อน" });
                }

                if (record.CheckoutTime != null)
                {
                    return Json(new { success = false, message = "วันนี้เช็คออกงานแล้ว" });
                }

                // 🔥 validate location (within distance)
                var distance = GetDistanceKm(
                    (double)(record.CheckinLat ?? 0),
                    (double)(record.CheckinLng ?? 0),
                    dto.Lat,
                    dto.Lng
                );

                var maxKmStr = await _context.SystemConfigs
                    .Where(x => x.ConfigKey == "WFH_MAX_DISTANCE_KM")
                    .Select(x => x.ConfigValue)
                    .FirstOrDefaultAsync();

                double maxDistance = double.TryParse(maxKmStr, out var val) ? val : 5;

                if (distance > maxDistance)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"ตำแหน่งออกงานอยู่ไกลจากจุดเข้างานเกิน {maxDistance} กม."
                    });
                }

                record.DistanceKm = (decimal)distance;
                record.CheckoutTime = DateTime.Now;
                record.CheckoutLat = (decimal)dto.Lat;
                record.CheckoutLng = (decimal)dto.Lng;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    type = "checkout",
                    distanceKm = record.DistanceKm,
                    checkinTime = record.CheckinTime?.ToString("HH:mm"),
                    checkoutTime = record.CheckoutTime?.ToString("HH:mm")
                });
            }

            return Json(new { success = false, message = "ประเภทการเช็คไม่ถูกต้อง" });

            // already completed
            // return Json(new { success = false, message = "วันนี้เช็คครบแล้ว" });
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
            {
                query = query.AsNoTracking();
            }

            var employee = await query.FirstOrDefaultAsync(x =>
                (resolvedEmpId.HasValue && x.EmpId == resolvedEmpId.Value)
                || x.LoginUserId == userId);

            if (employee != null && sessionEmpId != employee.EmpId)
            {
                HttpContext.Session.SetInt32("EmpId", employee.EmpId);
            }

            return employee;
        }

        private double GetDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371; // Earth radius in km
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }
        [RequireMenu("Attendance.Map")]
        public async Task<IActionResult> Map(string fromDate, string toDate)
        {
            DateTime start;
            DateTime end;

            // parse dd/MM/yyyy (พ.ศ.)
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParseExact(fromDate, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var f))
            {
                start = f.AddYears(-543);
            }
            else
            {
                start = DateTime.Today;
            }

            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParseExact(toDate, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var t))
            {
                end = t.AddYears(-543);
            }
            else
            {
                end = DateTime.Today;
            }

            var employees = await _context.Employees
                .Where(x => x.Status == "ACTIVE")
                .ToListAsync();

            var attendances = await _context.Attendances
                .Where(x => x.WorkDate >= start && x.WorkDate <= end)
                .ToListAsync();

            var dates = Enumerable.Range(0, (end - start).Days + 1)
                .Select(i => start.AddDays(i))
                .ToList();

            var data = (from emp in employees
                        from date in dates
                        join att in attendances
                            on new { emp.EmpId, WorkDate = date.Date }
                            equals new { att.EmpId, WorkDate = att.WorkDate.Date }
                            into gj
                        from a in gj.DefaultIfEmpty()
                        orderby date, emp.EmpName
                        select new
                        {
                            EmpId = emp.EmpId,
                            EmpName = emp.EmpName,
                            Position = emp.Position,
                            WorkDate = date,
                            CheckinLat = a?.CheckinLat,
                            CheckinLng = a?.CheckinLng,
                            CheckoutLat = a?.CheckoutLat,
                            CheckoutLng = a?.CheckoutLng,
                            CheckinTime = a?.CheckinTime,
                            CheckoutTime = a?.CheckoutTime,
                            DistanceKm = a?.DistanceKm
                        })
                        .ToList();

            // ส่งกลับเป็น พ.ศ.
            ViewBag.FromDate = start.AddYears(543).ToString("dd/MM/yyyy");
            ViewBag.ToDate = end.AddYears(543).ToString("dd/MM/yyyy");

            return View(data);
        }

    }
}
