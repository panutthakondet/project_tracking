using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // ===============================
            // ส่งข้อมูลที่จำเป็นให้ View
            // ===============================
            ViewBag.Username = HttpContext.Session.GetString("Username") ?? "-";

            var today = DateTime.Today;
            var next7days = today.AddDays(7);

            // 🔴 งานเลยกำหนด
            var overdue = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into emp
                from e in emp.DefaultIfEmpty()
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
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into emp
                from e in emp.DefaultIfEmpty()
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
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into emp
                from e in emp.DefaultIfEmpty()
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
                .Where(x => x.Status != "DONE")
                .CountAsync();

            ViewBag.FollowupDoneCount = await _context.ProjectFollowups
                .Where(x => x.Status == "DONE")
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