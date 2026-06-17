using Microsoft.AspNetCore.Mvc;
using ProjectTracking.Data;
using ProjectTracking.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    [RequireMenu("Meetings.Index")]
    public class MeetingsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly MeetingNotificationService _meetingNotificationService;
        private readonly ILogger<MeetingsController> _logger;

        public MeetingsController(
            AppDbContext context,
            MeetingNotificationService meetingNotificationService,
            ILogger<MeetingsController> logger)
        {
            _context = context;
            _meetingNotificationService = meetingNotificationService;
            _logger = logger;
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
            var rows = await _context.Meetings
                .AsNoTracking()
                .Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.MeetingDate,
                    m.StartTime,
                    m.EndTime,
                    m.ProjectId,
                    ProjectName = m.Project == null
                        ? null
                        : ((m.Project.Coop != null ? m.Project.Coop.CoopName + " - " : "") + m.Project.ProjectName),
                    m.Location,
                    m.Description,
                    m.MeetingAudience
                })
                .ToListAsync();

            var meetings = rows.Select(x => new
            {
                id = x.Id,
                title = x.ProjectName ?? "",
                allDay = false,
                start = $"{x.MeetingDate:yyyy-MM-dd}T{x.StartTime.Hours:D2}:{x.StartTime.Minutes:D2}:{x.StartTime.Seconds:D2}",
                end   = $"{x.MeetingDate:yyyy-MM-dd}T{x.EndTime.Hours:D2}:{x.EndTime.Minutes:D2}:{x.EndTime.Seconds:D2}",
                extendedProps = new
                {
                    projectId = x.ProjectId,
                    projectName = x.ProjectName,
                    meetingTitle = x.Title,
                    description = x.Description,
                    location = x.Location,
                    meetingAudience = x.MeetingAudience
                }
            }).ToList();

            return Json(meetings);
        }

        [RequireMenu("Meetings.Index")]
        [HttpGet]
        public async Task<IActionResult> ViewOnly(int? projectId, DateTime? fromDate, DateTime? toDate, string? audience)
        {
            var projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var audienceList = await _context.Meetings
                .AsNoTracking()
                .Where(m => m.MeetingAudience != null && m.MeetingAudience != "")
                .Select(m => m.MeetingAudience!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var query = _context.Meetings
                .AsNoTracking()
                .Include(m => m.Project)
                    .ThenInclude(p => p!.Coop)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(m => m.ProjectId == projectId.Value);

            if (fromDate.HasValue)
                query = query.Where(m => m.MeetingDate.Date >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(m => m.MeetingDate.Date <= toDate.Value.Date);

            if (!string.IsNullOrWhiteSpace(audience))
                query = query.Where(m => m.MeetingAudience == audience);

            var meetings = await query
                .OrderBy(m => m.MeetingDate)
                .ThenBy(m => m.StartTime)
                .ThenBy(m => m.Title)
                .ToListAsync();

            var meetingIds = meetings.Select(m => m.Id).ToList();
            var attendeeMap = new Dictionary<int, List<MeetingReportAttendeeViewModel>>();

            if (meetingIds.Count > 0)
            {
                var attendees = await (
                    from a in _context.MeetingAttendees.AsNoTracking()
                    join e in _context.Employees.AsNoTracking()
                        on a.UserId equals e.EmpId into ej
                    from e in ej.DefaultIfEmpty()
                    where meetingIds.Contains(a.MeetingId)
                    orderby a.MeetingId, e != null ? e.EmpName : ""
                    select new
                    {
                        a.MeetingId,
                        EmpId = a.UserId,
                        EmpName = e != null ? e.EmpName : "",
                        Position = e != null ? e.Position : "",
                        a.Status
                    }
                ).ToListAsync();

                attendeeMap = attendees
                    .GroupBy(a => a.MeetingId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(a => new MeetingReportAttendeeViewModel
                        {
                            EmpId = a.EmpId,
                            EmpName = string.IsNullOrWhiteSpace(a.EmpName) ? $"Employee #{a.EmpId}" : a.EmpName,
                            Position = a.Position ?? "",
                            Status = a.Status ?? ""
                        }).ToList());
            }

            var employees = await _context.Employees
                .AsNoTracking()
                .Select(e => new { e.EmpId, e.EmpName, e.LoginUserId })
                .ToListAsync();

            var employeeNameByKey = new Dictionary<int, string>();
            foreach (var employee in employees)
            {
                employeeNameByKey[employee.EmpId] = employee.EmpName;
                if (employee.LoginUserId.HasValue && !employeeNameByKey.ContainsKey(employee.LoginUserId.Value))
                    employeeNameByKey[employee.LoginUserId.Value] = employee.EmpName;
            }

            var reportRows = meetings.Select(m => new MeetingReportRowViewModel
            {
                Id = m.Id,
                ProjectId = m.ProjectId,
                ProjectName = m.Project?.ProjectDisplayName ?? "ไม่ระบุโครงการ",
                Title = m.Title,
                Description = m.Description ?? "",
                MeetingDate = m.MeetingDate,
                StartTime = m.StartTime,
                EndTime = m.EndTime,
                Location = m.Location ?? "",
                Audience = m.MeetingAudience ?? "",
                CreatedAt = m.CreatedAt,
                CreatedByName = m.CreatedBy.HasValue && employeeNameByKey.TryGetValue(m.CreatedBy.Value, out var name)
                    ? name
                    : "-",
                Attendees = attendeeMap.TryGetValue(m.Id, out var attendees)
                    ? attendees
                    : new List<MeetingReportAttendeeViewModel>()
            }).ToList();

            ViewBag.Projects = projects;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.AudienceList = audienceList;
            ViewBag.SelectedAudience = audience ?? "";

            return View(reportRows);
        }

        [RequireMenu("Meetings.Create")]
        [HttpGet]
        public async Task<IActionResult> Create(DateTime? date)
        {
            ViewBag.Date = date?.ToString("yyyy-MM-dd");

            // ส่งรายการโครงการไปให้ View ทำ dropdown
            var projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();
            ViewBag.Projects = projects
                .Select(p => new { p.ProjectId, ProjectName = p.ProjectDisplayName })
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

        [RequireMenu("Meetings.Show")]
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var meeting = await _context.Meetings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (meeting == null)
                return NotFound();

            // Project info (optional)
            string? coopName = null;
            string? projectName = null;
            if (meeting.ProjectId.HasValue)
            {
                var project = await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .Where(p => p.ProjectId == meeting.ProjectId.Value)
                    .FirstOrDefaultAsync();
                coopName = project?.Coop?.CoopName;
                projectName = project?.ProjectName;
            }

            // JOIN employee เพื่อเอาชื่อมาแสดง
            var attendeeRows = await (
                from a in _context.MeetingAttendees.AsNoTracking()
                join e in _context.Employees.AsNoTracking()
                    on a.UserId equals e.EmpId into ej
                from e in ej.DefaultIfEmpty()
                where a.MeetingId == meeting.Id
                orderby a.UserId
                select new
                {
                    AttendeeId = a.Id,
                    EmpId = a.UserId,
                    EmpName = e != null ? e.EmpName : null,
                    Position = e != null ? e.Position : null
                }
            ).ToListAsync();

            var notificationStatuses = await _meetingNotificationService.GetAttendeeNotificationStatusesAsync(id);
            var attendees = attendeeRows.Select(a =>
            {
                notificationStatuses.TryGetValue(a.AttendeeId, out var status);
                return new
                {
                    a.AttendeeId,
                    a.EmpId,
                    a.EmpName,
                    a.Position,
                    EmailSent = status?.EmailSent ?? false,
                    LineSent = status?.LineSent ?? false
                };
            }).ToList();

            ViewBag.CoopName = coopName;
            ViewBag.ProjectName = projectName;
            ViewBag.Attendees = attendees;

            return View(meeting);
        }

        [RequireMenu("Meetings.Show")]
        [HttpGet]
        public async Task<IActionResult> Calendar(int id)
        {
            var attachment = await _meetingNotificationService.BuildCalendarAttachmentAsync(id);
            if (attachment == null)
                return NotFound();

            return File(attachment.Content, attachment.ContentType, attachment.FileName);
        }

        [RequireMenu("Meetings.Edit")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var meeting = await _context.Meetings.FindAsync(id);
            if (meeting == null)
                return NotFound();

            var projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();
            ViewBag.Projects = projects
                .Select(p => new { p.ProjectId, ProjectName = p.ProjectDisplayName })
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

        [RequireMenu("Meetings.Create")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Store(Meeting model, List<int>? users)
        {
            var now = DateTime.Now;

            // กัน CreatedAt เป็นค่า 0001-01-01 ที่อาจทำให้ insert เพี้ยน
            if (model.CreatedAt == default)
                model.CreatedAt = now;

            model.UpdatedAt = model.CreatedAt;
            model.CreatedBy = await GetCurrentEntryIdAsync();
            model.Status = NormalizeMeetingStatus(model.Status);

            var createdMeetingId = 0;
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Meetings.Add(model);
                await _context.SaveChangesAsync();
                createdMeetingId = model.Id;

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

                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Failed to create meeting");
                throw;
            }

            try
            {
                var emailResult = await _meetingNotificationService.SendCreatedEmailAsync(createdMeetingId);
                if (emailResult.FailedCount > 0)
                {
                    TempData["Error"] = $"สร้าง Meeting สำเร็จ แต่ส่งอีเมลไม่สำเร็จ {emailResult.FailedCount} รายการ";
                }
                else if (emailResult.SentCount > 0)
                {
                    TempData["Success"] = $"สร้าง Meeting สำเร็จ และส่งอีเมลเชิญประชุมแล้ว {emailResult.SentCount} รายการ";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting was created but created-email notification failed. MeetingId={MeetingId}", createdMeetingId);
                TempData["Error"] = "สร้าง Meeting สำเร็จ แต่ระบบส่งอีเมลเชิญประชุมไม่สำเร็จ";
            }

            return RedirectToAction("Index");
        }

        [RequireMenu("Meetings.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Meeting model, List<int>? users)
        {
            var meeting = await _context.Meetings.FindAsync(model.Id);
            if (meeting == null)
                return NotFound();

            var oldStatus = NormalizeMeetingStatus(meeting.Status);
            var newStatus = NormalizeMeetingStatus(model.Status);
            var shouldSendCancellationNotice =
                oldStatus != "CANCELLED" &&
                newStatus == "CANCELLED";

            meeting.Title = model.Title;
            meeting.Description = model.Description;
            meeting.MeetingDate = model.MeetingDate;
            meeting.StartTime = model.StartTime;
            meeting.EndTime = model.EndTime;
            meeting.Location = model.Location;
            meeting.MeetingAudience = model.MeetingAudience;
            meeting.ProjectId = model.ProjectId;
            meeting.Status = newStatus;
            meeting.UpdatedAt = DateTime.Now;

            if (!meeting.CreatedBy.HasValue)
                meeting.CreatedBy = await GetCurrentEntryIdAsync();

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

            if (shouldSendCancellationNotice)
            {
                try
                {
                    var result = await _meetingNotificationService.SendCancelledNotificationsAsync(meeting.Id);
                    if (result.FailedCount > 0)
                    {
                        TempData["Error"] = $"บันทึกสถานะยกเลิกแล้ว แต่ส่งแจ้งเตือนไม่สำเร็จ {result.FailedCount} รายการ";
                    }
                    else if (result.SentCount > 0)
                    {
                        TempData["Success"] = $"บันทึกสถานะยกเลิกแล้ว และส่งแจ้งเตือนแล้ว {result.SentCount} รายการ";
                    }
                    else
                    {
                        TempData["Success"] = "บันทึกสถานะยกเลิกแล้ว";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Meeting was cancelled but cancellation notification failed. MeetingId={MeetingId}", meeting.Id);
                    TempData["Error"] = "บันทึกสถานะยกเลิกแล้ว แต่ระบบส่งแจ้งเตือนไม่สำเร็จ";
                }
            }

            return RedirectToAction("Show", new { id = meeting.Id });
        }
        public class MoveRequest
        {
            public int id { get; set; }
            public string? start { get; set; }
            public string? end { get; set; }
        }

        private static string NormalizeMeetingStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized == "CANCELLED" ? "CANCELLED" : "ACTIVE";
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

            if (!meeting.CreatedBy.HasValue)
                meeting.CreatedBy = await GetCurrentEntryIdAsync();

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

            meeting.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [RequireMenu("Meetings.Delete")]
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

        private async Task<int?> GetCurrentEntryIdAsync()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue) return null;

            var empId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.LoginUserId == userId.Value)
                .Select(e => (int?)e.EmpId)
                .FirstOrDefaultAsync();

            if (empId.HasValue) return empId;

            return await _context.LoginUsers
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.EmpId)
                .FirstOrDefaultAsync();
        }
    }
}
