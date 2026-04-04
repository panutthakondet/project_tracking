using Microsoft.EntityFrameworkCore;
using ProjectTracking.Models;
using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Data;
using ProjectTracking.Attributes;

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
        public IActionResult Index()
        {
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

            // map user -> employee
            var emp = await _context.Employees
                .FirstOrDefaultAsync(x => x.LoginUserId == userId);

            if (emp == null)
                return Json(new { success = false, message = "ไม่พบข้อมูลพนักงาน" });

            var empId = emp.EmpId;
            var today = DateTime.Today;

            // find today's record
            var record = await _context.Attendances
                .FirstOrDefaultAsync(x => x.EmpId == empId && x.WorkDate == today);

            // CHECK-IN
            if (record == null)
            {
                var newRecord = new Attendance
                {
                    EmpId = empId,
                    WorkDate = today,
                    CheckinTime = DateTime.Now,
                    CheckinLat = (decimal)dto.Lat,
                    CheckinLng = (decimal)dto.Lng
                };

                _context.Attendances.Add(newRecord);
                await _context.SaveChangesAsync();

                return Json(new { success = true, type = "checkin" });
            }

            // CHECK-OUT
            if (record.CheckoutTime == null)
            {
                // 🔥 validate location (within 5 km from check-in)
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

                return Json(new {
                    success = true,
                    type = "checkout",
                    distanceKm = record.DistanceKm
                });
            }

            // already completed
            return Json(new { success = false, message = "วันนี้เช็คครบแล้ว" });
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

            var data = await (from a in _context.Attendances
                              join e in _context.Employees on a.EmpId equals e.EmpId
                              where a.WorkDate >= start && a.WorkDate <= end
                              orderby a.WorkDate, e.EmpName
                              select new
                              {
                                  a.EmpId,
                                  EmpName = e.EmpName,
                                  Position = e.Position,
                                  WorkDate = a.WorkDate,
                                  a.CheckinLat,
                                  a.CheckinLng,
                                  a.CheckoutLat,
                                  a.CheckoutLng,
                                  a.CheckinTime,
                                  a.CheckoutTime
                              })
                              .ToListAsync();

            // ส่งกลับเป็น พ.ศ.
            ViewBag.FromDate = start.AddYears(543).ToString("dd/MM/yyyy");
            ViewBag.ToDate = end.AddYears(543).ToString("dd/MM/yyyy");

            return View(data);
        }
    }
}