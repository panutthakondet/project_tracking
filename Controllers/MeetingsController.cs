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
        public async Task<IActionResult> Index()
        {
            var permissions = await GetCurrentMeetingPermissionsAsync();
            var groups = await _context.MeetingGroups
                .AsNoTracking().AsSplitQuery()
                .Where(x => x.IsActive)
                .Include(x => x.Calendars.Where(c => c.IsActive))
                    .ThenInclude(x => x.Meetings.Where(m => m.Status == null || m.Status != "CANCELLED"))
                .OrderBy(x => x.SortOrder).ThenBy(x => x.GroupName)
                .ToListAsync();

            foreach (var group in groups)
                group.Calendars = group.Calendars.OrderBy(x => x.SortOrder).ThenBy(x => x.CalendarName).ToList();

            return View("Groups", new MeetingHomeViewModel
            {
                Groups = groups,
                TotalCalendars = groups.Sum(x => x.Calendars.Count),
                TotalMeetings = groups.SelectMany(x => x.Calendars).Sum(x => x.Meetings.Count),
                CanCreateGroup = permissions.Contains("Meetings.GroupCreate"),
                CanEditGroup = permissions.Contains("Meetings.GroupEdit"),
                CanDeleteGroup = permissions.Contains("Meetings.GroupDelete"),
                CanCreateCalendar = permissions.Contains("Meetings.CalendarCreate"),
                CanEditCalendar = permissions.Contains("Meetings.CalendarEdit"),
                CanDeleteCalendar = permissions.Contains("Meetings.CalendarDelete")
            });
        }

        [HttpGet]
        public async Task<IActionResult> Schedule(int id)
        {
            await GetCurrentMeetingPermissionsAsync();
            var calendar = await _context.MeetingCalendars.AsNoTracking()
                .Include(x => x.Group)
                .FirstOrDefaultAsync(x => x.CalendarId == id && x.IsActive && x.Group != null && x.Group.IsActive);
            if (calendar == null) return NotFound();
            ViewBag.CalendarId = calendar.CalendarId;
            ViewBag.CalendarName = calendar.CalendarName;
            ViewBag.GroupName = calendar.Group!.GroupName;
            return View("Index");
        }

        [HttpGet]
        public async Task<IActionResult> List(int calendarId)
        {
            // โหลดข้อมูลดิบจาก DB ก่อน แล้วค่อย format เวลาใน memory (กัน All-day/FormatException)
            var rows = await _context.Meetings
                .AsNoTracking()
                .Where(m => m.CalendarId == calendarId && (m.Status == null || m.Status != "CANCELLED"))
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

            var meetings = rows.Select(x =>
            {
                var startAt = x.MeetingDate.Date.Add(x.StartTime);
                var endAt = x.MeetingDate.Date.Add(x.EndTime);
                if (endAt <= startAt) endAt = endAt.AddDays(1);
                return new
                {
                    id = x.Id,
                    title = x.ProjectName ?? "",
                    allDay = false,
                    start = startAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                    end = endAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                    extendedProps = new
                    {
                        projectId = x.ProjectId,
                        projectName = x.ProjectName,
                        meetingTitle = x.Title,
                        description = x.Description,
                        location = x.Location,
                        meetingAudience = x.MeetingAudience
                    }
                };
            }).ToList();

            return Json(meetings);
        }

        [RequireMenu("Meetings.Index")]
        [HttpGet]
        public async Task<IActionResult> ViewOnly(int? calendarId, int? projectId, DateTime? fromDate, DateTime? toDate, string? audience, int? departmentId)
        {
            departmentId = await ReportDepartmentSupport.LoadAsync(this, _context, departmentId);
            var projectQuery = _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .AsQueryable();
            if (departmentId.HasValue)
                projectQuery = projectQuery.Where(p => p.DepartmentId == departmentId.Value);
            var projects = await projectQuery
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var audienceList = await _context.Meetings
                .AsNoTracking()
                .Where(m => (!calendarId.HasValue || m.CalendarId == calendarId.Value) &&
                    (!departmentId.HasValue || (m.Project != null && m.Project.DepartmentId == departmentId.Value)) &&
                    m.MeetingAudience != null && m.MeetingAudience != "")
                .Select(m => m.MeetingAudience!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var query = _context.Meetings
                .AsNoTracking()
                .Include(m => m.Project)
                    .ThenInclude(p => p!.Coop)
                .AsQueryable();
            if (calendarId.HasValue)
                query = query.Where(m => m.CalendarId == calendarId.Value);

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(m => m.ProjectId == projectId.Value);
            if (departmentId.HasValue)
                query = query.Where(m => m.Project != null && m.Project.DepartmentId == departmentId.Value);

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
            ViewBag.CalendarId = calendarId;

            return View(reportRows);
        }

        [RequireMenu("Meetings.Create")]
        [HttpGet]
        public async Task<IActionResult> Create(int calendarId, DateTime? date)
        {
            var calendar = await _context.MeetingCalendars.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CalendarId == calendarId && x.IsActive);
            if (calendar == null) return RedirectToAction(nameof(Index));
            ViewBag.CalendarId = calendarId;
            ViewBag.CalendarName = calendar.CalendarName;
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

            IReadOnlyDictionary<int, MeetingAttendeeNotificationStatus> notificationStatuses;
            try
            {
                notificationStatuses = await _meetingNotificationService.GetAttendeeNotificationStatusesAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load meeting attendee notification statuses. MeetingId={MeetingId}", id);
                notificationStatuses = new Dictionary<int, MeetingAttendeeNotificationStatus>();
                TempData["Error"] = "โหลดสถานะการส่งแจ้งเตือนผู้เข้าร่วมไม่สำเร็จ";
            }

            var attendees = attendeeRows.Select(a =>
            {
                notificationStatuses.TryGetValue(a.AttendeeId, out var status);
                return new MeetingShowAttendeeViewModel
                {
                    AttendeeId = a.AttendeeId,
                    EmpId = a.EmpId,
                    EmpName = a.EmpName,
                    Position = a.Position,
                    EmailSent = status?.EmailSent ?? false,
                    LineSent = status?.LineSent ?? false,
                    TelegramSent = status?.TelegramSent ?? false
                };
            }).ToList();

            ViewBag.CoopName = coopName;
            ViewBag.ProjectName = projectName;
            ViewBag.Attendees = attendees;
            ViewBag.CalendarId = meeting.CalendarId;

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
            ViewBag.CalendarId = meeting.CalendarId;

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

            if (!await _context.MeetingCalendars.AnyAsync(x => x.CalendarId == model.CalendarId && x.IsActive))
                return BadRequest("Invalid meeting calendar");

            var createdMeetingId = 0;
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Meetings.Add(model);
                await _context.SaveChangesAsync();
                createdMeetingId = model.Id;

                var selectedUserIds = ReadSelectedMeetingUsers(users);
                _logger.LogInformation(
                    "Creating meeting with {AttendeeCount} attendee(s). RawUsers={RawUsers}",
                    selectedUserIds.Count,
                    string.Join(",", selectedUserIds));

                if (selectedUserIds.Count > 0)
                {
                    foreach (var uid in selectedUserIds)
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
                var notifyResult = await _meetingNotificationService.SendCreatedNotificationsAsync(createdMeetingId);
                if (notifyResult.SentCount > 0)
                {
                    TempData["Success"] = $"สร้าง Meeting สำเร็จ และส่งแจ้งเตือนแล้ว {notifyResult.SentCount} รายการ";
                    if (notifyResult.FailedCount > 0)
                    {
                        TempData["Error"] = string.IsNullOrWhiteSpace(notifyResult.Detail)
                            ? $"มีบางรายการส่งแจ้งเตือนไม่สำเร็จ {notifyResult.FailedCount} รายการ"
                            : $"มีบางรายการส่งแจ้งเตือนไม่สำเร็จ {notifyResult.FailedCount} รายการ ({notifyResult.Detail})";
                    }
                }
                else if (notifyResult.FailedCount > 0)
                {
                    TempData["Error"] = string.IsNullOrWhiteSpace(notifyResult.Detail)
                        ? $"สร้าง Meeting สำเร็จ แต่ส่งแจ้งเตือนไม่สำเร็จ {notifyResult.FailedCount} รายการ"
                        : $"สร้าง Meeting สำเร็จ แต่ส่งแจ้งเตือนไม่สำเร็จ {notifyResult.FailedCount} รายการ ({notifyResult.Detail})";
                }
                else if (notifyResult.SkippedCount > 0)
                {
                    TempData["Error"] = $"สร้าง Meeting สำเร็จ แต่ไม่มีรายการแจ้งเตือนที่ส่งออก ({notifyResult.SkippedCount} รายการถูกข้าม: อาจเคยส่งแล้วหรือยังไม่ได้ผูก Email/LINE/Telegram)";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting was created but created notification failed. MeetingId={MeetingId}", createdMeetingId);
                TempData["Error"] = "สร้าง Meeting สำเร็จ แต่ระบบส่งแจ้งเตือนไม่สำเร็จ";
            }

            return RedirectToAction(nameof(Schedule), new { id = model.CalendarId });
        }

        private List<int> ReadSelectedMeetingUsers(List<int>? boundUsers)
        {
            var userIds = new List<int>();

            if (boundUsers != null)
                userIds.AddRange(boundUsers);

            if (Request.HasFormContentType)
            {
                foreach (var raw in Request.Form["users"])
                {
                    if (int.TryParse(raw, out var userId))
                        userIds.Add(userId);
                }
            }

            return userIds
                .Where(uid => uid > 0)
                .Distinct()
                .ToList();
        }

        [RequireMenu("Meetings.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Meeting model, List<int>? users)
        {
            var meeting = await _context.Meetings.FindAsync(model.Id);
            if (meeting == null)
                return NotFound();

            var newStatus = NormalizeMeetingStatus(model.Status);
            var oldStatus = NormalizeMeetingStatus(meeting.Status);
            var selectedUserIds = (users ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            var existingAttendees = await _context.MeetingAttendees
                .Where(a => a.MeetingId == meeting.Id)
                .ToListAsync();

            var existingUserIdsBeforeUpdate = existingAttendees
                .Select(a => a.UserId)
                .ToHashSet();

            var detailsChanged =
                !string.Equals(NormalizeText(meeting.Title), NormalizeText(model.Title), StringComparison.Ordinal)
                || !string.Equals(NormalizeText(meeting.Description), NormalizeText(model.Description), StringComparison.Ordinal)
                || meeting.MeetingDate.Date != model.MeetingDate.Date
                || meeting.StartTime != model.StartTime
                || meeting.EndTime != model.EndTime
                || !string.Equals(NormalizeText(meeting.Location), NormalizeText(model.Location), StringComparison.Ordinal)
                || !string.Equals(NormalizeText(meeting.MeetingAudience), NormalizeText(model.MeetingAudience), StringComparison.Ordinal)
                || meeting.ProjectId != model.ProjectId;

            var statusChanged = !string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase);
            var attendeesChanged = !existingUserIdsBeforeUpdate.SetEquals(selectedUserIds);
            var shouldSendCancellationNotice = oldStatus != "CANCELLED" && newStatus == "CANCELLED";
            var shouldSendUpdateNotice = newStatus != "CANCELLED" && (detailsChanged || statusChanged || attendeesChanged);

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

            // อัปเดตรายชื่อผู้เข้าร่วมโดยรักษา attendee_id เดิมไว้ เพื่อให้ log แจ้งเตือนยังตรวจซ้ำได้ถูกต้อง
            var removeAttendees = existingAttendees
                .Where(a => !selectedUserIds.Contains(a.UserId))
                .ToList();

            if (removeAttendees.Count > 0)
                _context.MeetingAttendees.RemoveRange(removeAttendees);

            var existingUserIds = existingAttendees
                .Where(a => selectedUserIds.Contains(a.UserId))
                .Select(a => a.UserId)
                .ToHashSet();

            foreach (var uid in selectedUserIds.Where(id => !existingUserIds.Contains(id)))
            {
                _context.MeetingAttendees.Add(new MeetingAttendee
                {
                    MeetingId = meeting.Id,
                    UserId = uid
                });
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
            else if (shouldSendUpdateNotice)
            {
                try
                {
                    var result = await _meetingNotificationService.SendUpdatedNotificationsAsync(meeting.Id);
                    if (result.FailedCount > 0)
                    {
                        TempData["Error"] = $"บันทึกการแก้ไขแล้ว แต่ส่งแจ้งเตือนไม่สำเร็จ {result.FailedCount} รายการ";
                    }
                    else if (result.SentCount > 0)
                    {
                        TempData["Success"] = $"บันทึกการแก้ไขแล้ว และส่งแจ้งเตือนแล้ว {result.SentCount} รายการ";
                    }
                    else
                    {
                        TempData["Success"] = "บันทึกการแก้ไขแล้ว";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Meeting was updated but update notification failed. MeetingId={MeetingId}", meeting.Id);
                    TempData["Error"] = "บันทึกการแก้ไขแล้ว แต่ระบบส่งแจ้งเตือนไม่สำเร็จ";
                }
            }
            else
            {
                TempData["Success"] = "บันทึกการแก้ไขแล้ว";
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

        private static string NormalizeText(string? value)
            => (value ?? "").Trim();

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("Meetings.GroupCreate")]
        public async Task<IActionResult> CreateGroup(string groupName)
        {
            if (!await HasCurrentMeetingPermissionAsync("Meetings.GroupCreate"))
                return RedirectToAction("AccessDenied", "Auth", new { key = "Meetings.GroupCreate" });
            groupName = (groupName ?? "").Trim();
            if (groupName.Length == 0) return RedirectToAction(nameof(Index));
            var sort = await _context.MeetingGroups.Select(x => (int?)x.SortOrder).MaxAsync() ?? 0;
            _context.MeetingGroups.Add(new MeetingGroup { GroupName = groupName, SortOrder = sort + 1 });
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("Meetings.CalendarCreate")]
        public async Task<IActionResult> CreateCalendar(int groupId, string calendarName, string? coverColor)
        {
            if (!await HasCurrentMeetingPermissionAsync("Meetings.CalendarCreate"))
                return RedirectToAction("AccessDenied", "Auth", new { key = "Meetings.CalendarCreate" });
            calendarName = (calendarName ?? "").Trim();
            if (calendarName.Length == 0 || !await _context.MeetingGroups.AnyAsync(x => x.GroupId == groupId && x.IsActive))
                return RedirectToAction(nameof(Index));
            var sort = await _context.MeetingCalendars.Where(x => x.GroupId == groupId)
                .Select(x => (int?)x.SortOrder).MaxAsync() ?? 0;
            var safeColor = System.Text.RegularExpressions.Regex.IsMatch(coverColor ?? "", "^#[0-9A-Fa-f]{6}$")
                ? coverColor! : "#14b8a6";
            var calendar = new MeetingCalendar { GroupId = groupId, CalendarName = calendarName, CoverColor = safeColor, SortOrder = sort + 1 };
            _context.MeetingCalendars.Add(calendar);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Schedule), new { id = calendar.CalendarId });
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("Meetings.GroupEdit")]
        public async Task<IActionResult> RenameGroup(int groupId, string groupName)
        {
            if (!await HasCurrentMeetingPermissionAsync("Meetings.GroupEdit"))
                return RedirectToAction("AccessDenied", "Auth", new { key = "Meetings.GroupEdit" });
            var group = await _context.MeetingGroups.FirstOrDefaultAsync(x => x.GroupId == groupId && x.IsActive);
            groupName = (groupName ?? "").Trim();
            if (group != null && groupName.Length > 0) { group.GroupName = groupName; group.UpdatedAt = DateTime.Now; await _context.SaveChangesAsync(); }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("Meetings.CalendarEdit")]
        public async Task<IActionResult> RenameCalendar(int calendarId, string calendarName, string? coverColor)
        {
            if (!await HasCurrentMeetingPermissionAsync("Meetings.CalendarEdit"))
                return RedirectToAction("AccessDenied", "Auth", new { key = "Meetings.CalendarEdit" });
            var calendar = await _context.MeetingCalendars.FirstOrDefaultAsync(x => x.CalendarId == calendarId && x.IsActive);
            calendarName = (calendarName ?? "").Trim();
            if (calendar != null && calendarName.Length > 0)
            {
                calendar.CalendarName = calendarName;
                if (System.Text.RegularExpressions.Regex.IsMatch(coverColor ?? "", "^#[0-9A-Fa-f]{6}$")) calendar.CoverColor = coverColor!;
                calendar.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("Meetings.GroupDelete")]
        public async Task<IActionResult> DeleteGroup(int groupId)
        {
            if (!await HasCurrentMeetingPermissionAsync("Meetings.GroupDelete"))
                return RedirectToAction("AccessDenied", "Auth", new { key = "Meetings.GroupDelete" });
            var group = await _context.MeetingGroups.Include(x => x.Calendars).FirstOrDefaultAsync(x => x.GroupId == groupId && x.IsActive);
            if (group != null) { group.IsActive = false; foreach (var c in group.Calendars) c.IsActive = false; await _context.SaveChangesAsync(); }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken, RequireMenu("Meetings.CalendarDelete")]
        public async Task<IActionResult> DeleteCalendar(int calendarId)
        {
            if (!await HasCurrentMeetingPermissionAsync("Meetings.CalendarDelete"))
                return RedirectToAction("AccessDenied", "Auth", new { key = "Meetings.CalendarDelete" });
            var calendar = await _context.MeetingCalendars.FirstOrDefaultAsync(x => x.CalendarId == calendarId && x.IsActive);
            if (calendar != null) { calendar.IsActive = false; calendar.UpdatedAt = DateTime.Now; await _context.SaveChangesAsync(); }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Meetings.Edit")]
        public async Task<IActionResult> Move([FromBody] MoveRequest req)
        {
            if (req == null || req.id <= 0 || string.IsNullOrWhiteSpace(req.start))
                return Json(new { success = false, message = "invalid request" });

            var meeting = await _context.Meetings.FindAsync(req.id);
            if (meeting == null)
                return Json(new { success = false, message = "meeting not found" });
            if (NormalizeMeetingStatus(meeting.Status) == "CANCELLED")
                return Json(new { success = false, message = "meeting cancelled" });

            // Parse ISO8601 (FullCalendar sends startStr/endStr)
            if (!DateTimeOffset.TryParse(req.start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startDto))
                return Json(new { success = false, message = "invalid start" });

            // Keep original duration if end is missing or invalid
            var duration = meeting.EndTime - meeting.StartTime;
            if (duration <= TimeSpan.Zero)
                duration = duration.Add(TimeSpan.FromDays(1));

            if (!meeting.CreatedBy.HasValue)
                meeting.CreatedBy = await GetCurrentEntryIdAsync();

            DateTimeOffset endDto = default;
            var hasEnd = !string.IsNullOrWhiteSpace(req.end) &&
                         DateTimeOffset.TryParse(req.end, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out endDto);

            var oldDate = meeting.MeetingDate.Date;
            var oldStartTime = meeting.StartTime;
            var oldEndTime = meeting.EndTime;
            var newDate = startDto.Date;
            var newStartTime = startDto.TimeOfDay;
            TimeSpan newEndTime;

            if (hasEnd)
            {
                // Some views may send end earlier than start; fallback to duration
                if (endDto < startDto)
                    endDto = startDto.Add(duration);

                newEndTime = endDto.TimeOfDay;
            }
            else
            {
                newEndTime = (startDto.Add(duration)).TimeOfDay;
            }

            var scheduleChanged = oldDate != newDate.Date
                || oldStartTime != newStartTime
                || oldEndTime != newEndTime;
            if (!scheduleChanged)
                return Json(new { success = true });

            // Update date + times (use local date/time from the parsed value)
            meeting.MeetingDate = newDate;
            meeting.StartTime = newStartTime;
            meeting.EndTime = newEndTime;

            meeting.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            try
            {
                var result = await _meetingNotificationService.SendUpdatedNotificationsAsync(meeting.Id);
                var warning = result.FailedCount > 0
                    ? $"เปลี่ยนวันเวลาแล้ว แต่ส่งแจ้งเตือนไม่สำเร็จ {result.FailedCount} รายการ"
                    : null;
                return Json(new
                {
                    success = true,
                    warning,
                    sentCount = result.SentCount,
                    skippedCount = result.SkippedCount,
                    failedCount = result.FailedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting was moved but update notification failed. MeetingId={MeetingId}", meeting.Id);
                return Json(new
                {
                    success = true,
                    warning = "เปลี่ยนวันเวลาแล้ว แต่ระบบส่งแจ้งเตือนไม่สำเร็จ"
                });
            }
        }

        [RequireMenu("Meetings.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var meeting = await _context.Meetings.FindAsync(id);
            if (meeting == null)
                return NotFound();

            var calendarId = meeting.CalendarId;
            if (NormalizeMeetingStatus(meeting.Status) == "CANCELLED")
            {
                TempData["Success"] = "Meeting นี้ถูกยกเลิกแล้ว";
                return RedirectToAction(nameof(Schedule), new { id = calendarId });
            }

            meeting.Status = "CANCELLED";
            meeting.UpdatedAt = DateTime.Now;
            if (!meeting.CreatedBy.HasValue)
                meeting.CreatedBy = await GetCurrentEntryIdAsync();
            await _context.SaveChangesAsync();

            try
            {
                var result = await _meetingNotificationService.SendCancelledNotificationsAsync(meeting.Id);
                if (result.FailedCount > 0)
                {
                    TempData["Error"] = $"ยกเลิก Meeting แล้ว แต่ส่งแจ้งเตือนไม่สำเร็จ {result.FailedCount} รายการ";
                }
                else if (result.SentCount > 0)
                {
                    TempData["Success"] = $"ยกเลิก Meeting แล้ว และส่งแจ้งเตือนแล้ว {result.SentCount} รายการ";
                }
                else
                {
                    TempData["Success"] = "ยกเลิก Meeting แล้ว";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting was cancelled but cancellation notification failed. MeetingId={MeetingId}", meeting.Id);
                TempData["Error"] = "ยกเลิก Meeting แล้ว แต่ระบบส่งแจ้งเตือนไม่สำเร็จ";
            }

            return RedirectToAction(nameof(Schedule), new { id = calendarId });
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

        private bool CanMenu(string key)
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)) return true;
            var menus = HttpContext.Session.GetString("Menus") ?? "";
            return menus.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Contains(key, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<HashSet<string>> GetCurrentMeetingPermissionsAsync()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                return new HashSet<string>(new[]
                {
                    "Meetings.Index", "Meetings.Show", "Meetings.Create", "Meetings.Edit", "Meetings.Delete",
                    "Meetings.GroupCreate", "Meetings.GroupEdit", "Meetings.GroupDelete",
                    "Meetings.CalendarCreate", "Meetings.CalendarEdit", "Meetings.CalendarDelete"
                }, StringComparer.OrdinalIgnoreCase);
            }

            var username = (HttpContext.Session.GetString("Username") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var keys = await _context.UserMenus
                .AsNoTracking()
                .Where(x => x.Username != null && x.Username.Trim().ToLower() == username.ToLower())
                .Select(x => x.MenuKey)
                .ToListAsync();

            var permissions = new HashSet<string>(
                keys.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            // Keep Razor CanMenu(...) and authorization filters in sync with the
            // latest permissions, so revoked action buttons disappear immediately.
            HttpContext.Session.SetString("Menus", string.Join(',', permissions));
            return permissions;
        }

        private async Task<bool> HasCurrentMeetingPermissionAsync(string key)
            => (await GetCurrentMeetingPermissionsAsync()).Contains(key);
    }
}
