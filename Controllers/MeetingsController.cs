using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Data;
using ProjectTracking.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class MeetingsController : Controller
    {
        private readonly AppDbContext _context;

        public MeetingsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            // โหลดข้อมูลดิบจาก DB ก่อน แล้วค่อย format เวลาใน memory (กัน All-day/FormatException)
            var rows = await (
                from m in _context.Meetings.AsNoTracking()
                join p in _context.Projects.AsNoTracking() on m.ProjectId equals p.ProjectId into pj
                from p in pj.DefaultIfEmpty()
                select new
                {
                    m.Id,
                    m.Title,
                    m.MeetingDate,
                    m.StartTime,
                    m.EndTime,
                    m.ProjectId,
                    ProjectName = p == null ? null : p.ProjectName,
                    m.Location
                }
            ).ToListAsync();

            var meetings = rows.Select(x => new
            {
                id = x.Id,
                title = (x.ProjectName == null ? x.Title : $"[{x.ProjectName}] {x.Title}"),
                allDay = false,
                start = $"{x.MeetingDate:yyyy-MM-dd}T{x.StartTime.Hours:D2}:{x.StartTime.Minutes:D2}:{x.StartTime.Seconds:D2}",
                end   = $"{x.MeetingDate:yyyy-MM-dd}T{x.EndTime.Hours:D2}:{x.EndTime.Minutes:D2}:{x.EndTime.Seconds:D2}",
                extendedProps = new
                {
                    projectId = x.ProjectId,
                    projectName = x.ProjectName,
                    location = x.Location
                }
            }).ToList();

            return Json(meetings);
        }

        [HttpGet]
        public IActionResult Create(DateTime? date)
        {
            ViewBag.Date = date?.ToString("yyyy-MM-dd");

            // ส่งรายการโครงการไปให้ View ทำ dropdown
            ViewBag.Projects = _context.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .Select(p => new { p.ProjectId, p.ProjectName })
                .ToList();

            // รายชื่อพนักงานทั้งหมด (ACTIVE) สำหรับเลือกผู้เข้าร่วม
            ViewBag.Employees = _context.Employees
                .AsNoTracking()
                .Where(e => e.Status == "ACTIVE")
                .OrderBy(e => e.EmpName)
                .Select(e => new { e.EmpId, e.EmpName, e.Position })
                .ToList();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var meeting = await _context.Meetings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (meeting == null)
                return NotFound();

            // Project name (optional)
            string? projectName = null;
            if (meeting.ProjectId.HasValue)
            {
                projectName = await _context.Projects
                    .AsNoTracking()
                    .Where(p => p.ProjectId == meeting.ProjectId.Value)
                    .Select(p => p.ProjectName)
                    .FirstOrDefaultAsync();
            }

            // JOIN employee เพื่อเอาชื่อมาแสดง
            var attendees = await (
                from a in _context.MeetingAttendees.AsNoTracking()
                join e in _context.Employees.AsNoTracking()
                    on a.UserId equals e.EmpId into ej
                from e in ej.DefaultIfEmpty()
                where a.MeetingId == meeting.Id
                orderby a.UserId
                select new
                {
                    EmpId = a.UserId,
                    EmpName = e != null ? e.EmpName : null,
                    Position = e != null ? e.Position : null,
                    Status = a.Status
                }
            ).ToListAsync();

            ViewBag.ProjectName = projectName;
            ViewBag.Attendees = attendees;

            return View(meeting);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var meeting = await _context.Meetings.FindAsync(id);
            if (meeting == null)
                return NotFound();

            ViewBag.Projects = _context.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .Select(p => new { p.ProjectId, p.ProjectName })
                .ToList();

            // รายชื่อพนักงานทั้งหมด (ACTIVE)
            ViewBag.Employees = _context.Employees
                .AsNoTracking()
                .Where(e => e.Status == "ACTIVE")
                .OrderBy(e => e.EmpName)
                .Select(e => new { e.EmpId, e.EmpName, e.Position })
                .ToList();

            // ผู้เข้าร่วมของ meeting นี้ (emp_id ที่ถูกเลือก)
            ViewBag.SelectedUsers = _context.MeetingAttendees
                .AsNoTracking()
                .Where(a => a.MeetingId == id)
                .Select(a => a.UserId)
                .ToList();

            return View(meeting);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Store(Meeting model, List<int>? users)
        {
            // กัน CreatedAt เป็นค่า 0001-01-01 ที่อาจทำให้ insert เพี้ยน
            if (model.CreatedAt == default)
                model.CreatedAt = DateTime.Now;

            using var tx = _context.Database.BeginTransaction();
            try
            {
                _context.Meetings.Add(model);
                _context.SaveChanges();

                if (users != null && users.Count > 0)
                {
                    foreach (var uid in users)
                    {
                        _context.MeetingAttendees.Add(new MeetingAttendee
                        {
                            MeetingId = model.Id,
                            UserId = uid
                        });
                    }

                    _context.SaveChanges();
                }

                tx.Commit();
                return RedirectToAction("Index");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Meeting model, List<int>? users)
        {
            var meeting = await _context.Meetings.FindAsync(model.Id);
            if (meeting == null)
                return NotFound();

            meeting.Title = model.Title;
            meeting.Description = model.Description;
            meeting.MeetingDate = model.MeetingDate;
            meeting.StartTime = model.StartTime;
            meeting.EndTime = model.EndTime;
            meeting.Location = model.Location;
            meeting.ProjectId = model.ProjectId;

            // อัปเดตรายชื่อผู้เข้าร่วม
            var existing = _context.MeetingAttendees.Where(a => a.MeetingId == meeting.Id);
            _context.MeetingAttendees.RemoveRange(existing);

            if (users != null && users.Count > 0)
            {
                foreach (var uid in users)
                {
                    _context.MeetingAttendees.Add(new MeetingAttendee
                    {
                        MeetingId = meeting.Id,
                        UserId = uid
                    });
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Show", new { id = meeting.Id });
        }
        public class MoveRequest
        {
            public int id { get; set; }
            public string? start { get; set; }
            public string? end { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Move([FromBody] MoveRequest req)
        {
            if (req == null || req.id <= 0 || string.IsNullOrWhiteSpace(req.start))
                return Json(new { success = false, message = "invalid request" });

            var meeting = await _context.Meetings.FindAsync(req.id);
            if (meeting == null)
                return Json(new { success = false, message = "meeting not found" });

            // Parse ISO8601 (FullCalendar sends startStr/endStr)
            if (!DateTimeOffset.TryParse(req.start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startDto))
                return Json(new { success = false, message = "invalid start" });

            // Keep original duration if end is missing or invalid
            var duration = meeting.EndTime - meeting.StartTime;
            if (duration <= TimeSpan.Zero)
                duration = TimeSpan.FromHours(1);

            DateTimeOffset endDto = default;
            var hasEnd = !string.IsNullOrWhiteSpace(req.end) &&
                         DateTimeOffset.TryParse(req.end, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out endDto);

            // Update date + times (use local date/time from the parsed value)
            meeting.MeetingDate = startDto.Date;
            meeting.StartTime = startDto.TimeOfDay;

            if (hasEnd)
            {
                // Some views may send end earlier than start; fallback to duration
                if (endDto < startDto)
                    endDto = startDto.Add(duration);

                meeting.EndTime = endDto.TimeOfDay;
            }
            else
            {
                meeting.EndTime = (startDto.Add(duration)).TimeOfDay;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var meeting = await _context.Meetings.FindAsync(id);
            if (meeting == null)
                return NotFound();

            // ลบผู้เข้าร่วม (เผื่อ FK ไม่ cascade)
            var attendees = _context.MeetingAttendees.Where(a => a.MeetingId == id);
            _context.MeetingAttendees.RemoveRange(attendees);

            _context.Meetings.Remove(meeting);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}