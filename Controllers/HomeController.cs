using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Attributes;

namespace ProjectTracking.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> Index()
        {
            // ===============================
            // ส่งข้อมูลที่จำเป็นให้ View
            // ===============================
            ViewBag.Username = HttpContext.Session.GetString("Username") ?? "-";

            // ===============================
            // ⏰ เวลาเข้า-ออกวันนี้
            // ===============================
            var username = HttpContext.Session.GetString("Username");

            var today = DateTime.Today;
            // 🔍 หา emp_id จาก username
            var emp = await _context.Employees
                .FirstOrDefaultAsync(e => e.LoginUser != null && e.LoginUser.Username == username);

            var todayAttendance = await _context.Attendances
                .Where(x => x.WorkDate == today && emp != null && x.EmpId == emp.EmpId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (todayAttendance != null)
            {
                ViewBag.CheckInTime = todayAttendance.CheckinTime.HasValue
                    ? todayAttendance.CheckinTime.Value.ToString("HH:mm")
                    : "-";

                ViewBag.CheckOutTime = todayAttendance.CheckoutTime.HasValue
                    ? todayAttendance.CheckoutTime.Value.ToString("HH:mm")
                    : "-";
            }
            else
            {
                ViewBag.CheckInTime = "-";
                ViewBag.CheckOutTime = "-";
            }

            var next7days = today.AddDays(7);
            var fromDate = today.AddMonths(-1);

            // 🔴 งานเลยกำหนด
            var overdue = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into empJoin
                from e in empJoin.DefaultIfEmpty()
                where f.NextFollowupDate != null
                    && f.NextFollowupDate < today
                    && f.Status != "DONE"
                orderby f.NextFollowupDate
                select new {
                    FollowupId = f.FollowupId,
                    f.TaskTitle,
                    f.PartnerName,
                    f.NextFollowupDate,
                    ProjectName = p.ProjectName,
                    OwnerName = e != null ? e.EmpName : "-"
                }
            ).Take(10).ToListAsync();

            // 🟡 งานวันนี้
            var todayList = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into empJoin
                from e in empJoin.DefaultIfEmpty()
                where f.NextFollowupDate != null
                    && f.NextFollowupDate == today
                    && f.Status != "DONE"
                orderby f.NextFollowupDate
                select new {
                    FollowupId = f.FollowupId,
                    f.TaskTitle,
                    f.PartnerName,
                    f.NextFollowupDate,
                    ProjectName = p.ProjectName,
                    OwnerName = e != null ? e.EmpName : "-"
                }
            ).Take(10).ToListAsync();

            // 🟢 งานใน 7 วันข้างหน้า
            var upcoming = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into empJoin
                from e in empJoin.DefaultIfEmpty()
                where f.NextFollowupDate != null
                    && f.NextFollowupDate > today
                    && f.NextFollowupDate <= next7days
                    && f.Status != "DONE"
                orderby f.NextFollowupDate
                select new {
                    FollowupId = f.FollowupId,
                    f.TaskTitle,
                    f.PartnerName,
                    f.NextFollowupDate,
                    ProjectName = p.ProjectName,
                    OwnerName = e != null ? e.EmpName : "-"
                }
            ).Take(10).ToListAsync();

            // ส่งข้อมูลไป View
            ViewBag.OverdueFollowups = overdue;
            ViewBag.TodayFollowups = todayList;
            ViewBag.UpcomingFollowups = upcoming;

            ViewBag.OverdueCount = overdue.Count;
            ViewBag.TodayFollowupCount = todayList.Count;
            ViewBag.UpcomingCount = upcoming.Count;
            ViewBag.FollowupAlertCount = await _context.ProjectFollowups
                .Where(x => x.Status == "OPEN")
                .CountAsync();

            ViewBag.FollowupDoneCount = await _context.ProjectFollowups
                .Where(x => x.Status == "DONE")
                .CountAsync();

            ViewBag.FollowupAckCount = await _context.ProjectFollowups
                .Where(x => x.Status == "ACK" && x.LastContactDate != null && x.LastContactDate >= fromDate)
                .CountAsync();

            // 📅 จำนวนการประชุมตั้งแต่วันนี้เป็นต้นไป
            ViewBag.MeetingCount = await _context.Meetings
                .Where(x => x.MeetingDate >= today)
                .CountAsync();

            return View();
        }

        // ===============================
        // Logout
        // ===============================
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Auth");
        }
    }
}