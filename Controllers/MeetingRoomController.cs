using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class MeetingRoomController : BaseController
    {
        private const string DefaultProfileImagePath = "/images/Profile/profile.png";
        private const string DefaultCharacterPreset = "doraemon";
        private const string DefaultAvatarColor = "#2d9cff";
        private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(10);
        private static readonly string[] ClosedIssueStatuses = { "PASS", "REJECT", "DONE", "CLOSED", "RESOLVED" };
        private static readonly string[] ClosedSupportStatuses = { "PASS", "REJECT", "DONE", "CLOSED", "RESOLVED" };
        private static readonly string[] ClosedFollowupStatuses = { "DONE", "ACK", "CLOSED", "CANCELLED", "CANCELED" };
        private static readonly string[] ClosedAssignStatuses = { "DONE", "PASS", "COMPLETE", "COMPLETED", "CLOSED", "CANCELLED", "CANCELED" };
        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "AVAILABLE",
            "BUSY",
            "DND",
            "AWAY",
            "MEETING"
        };
        private static readonly HashSet<string> AllowedCharacters = new(StringComparer.OrdinalIgnoreCase)
        {
            "doraemon",
            "domita",
            "jaian",
            "dorami",
            "sunio"
        };
        private static readonly HashSet<string> AllowedAreaTones = new(StringComparer.OrdinalIgnoreCase)
        {
            "blue",
            "teal",
            "violet",
            "rose",
            "orange",
            "green",
            "cyan"
        };
        private static readonly HashSet<string> AllowedObjectKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "desk-basic",
            "chair-office",
            "sofa-blue",
            "table-round",
            "plant-pot",
            "plant-tall",
            "rug-blue",
            "rug-round",
            "partition",
            "screen",
            "board"
        };
        private static readonly HashSet<string> AllowedObjectTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "DESK",
            "CHAIR",
            "SOFA",
            "TABLE",
            "PLANT",
            "RUG",
            "DIVIDER",
            "SCREEN",
            "BOARD"
        };
        private static readonly HashSet<string> AllowedObjectTones = new(StringComparer.OrdinalIgnoreCase)
        {
            "wood",
            "blue",
            "teal",
            "violet",
            "green",
            "orange",
            "rose",
            "gray"
        };

        private static readonly (int X, int Y, string Zone)[] DeskSlots =
        {
            (18, 60, "Support Desk"),
            (25, 43, "Developer Area"),
            (34, 61, "Developer Area"),
            (44, 43, "Project Room"),
            (52, 61, "Project Room"),
            (62, 43, "PM Room"),
            (70, 61, "Issue Desk"),
            (79, 43, "Meeting Room"),
            (84, 70, "Followup Corner"),
            (14, 31, "Lobby"),
            (33, 28, "War Table"),
            (55, 28, "Design Review"),
            (73, 28, "Report Wall"),
            (88, 36, "Garden Talk")
        };

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public MeetingRoomController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> Index()
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
                return RedirectToAction("Login", "Auth", new { returnUrl = "/MeetingRoom" });

            await TouchCurrentUserAsync(currentUserId.Value);
            await EnsureCurrentProfileAsync(currentUserId.Value);

            var model = await BuildViewModelAsync(currentUserId.Value);
            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Guest(int? desk = null)
        {
            var model = await BuildViewModelAsync(null, isGuest: true, focusUserId: desk);
            return View("Index", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> UpdateDisplayName(string? displayName = null)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
                return Unauthorized();

            var profile = await EnsureCurrentProfileAsync(currentUserId.Value);
            profile.DisplayName = NormalizeRoomDisplayName(displayName);
            profile.UpdatedAt = DateTime.Now;

            await TouchCurrentUserAsync(currentUserId.Value, saveChanges: false);
            await _context.SaveChangesAsync();

            var resolvedDisplayName = await ResolveDisplayNameAsync(currentUserId.Value);
            return Json(new
            {
                ok = true,
                displayName = resolvedDisplayName,
                roomDisplayName = profile.DisplayName ?? "",
                initial = Initial(resolvedDisplayName)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> UpdatePresence(string status, string? statusText = null)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
                return Unauthorized();

            status = NormalizeStatus(status);
            if (!AllowedStatuses.Contains(status))
                return BadRequest(new { ok = false, message = "Status is invalid." });

            statusText = (statusText ?? "").Trim();
            if (statusText.Length > 120)
                statusText = statusText[..120];

            var profile = await EnsureCurrentProfileAsync(currentUserId.Value);
            profile.Status = status;
            profile.StatusText = string.IsNullOrWhiteSpace(statusText) ? null : statusText;
            profile.UpdatedAt = DateTime.Now;

            await TouchCurrentUserAsync(currentUserId.Value, saveChanges: false);
            await _context.SaveChangesAsync();

            return Json(new
            {
                ok = true,
                status,
                label = StatusLabel(status),
                statusText = profile.StatusText ?? ""
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> UpdateCharacter(string character, string? avatarColor = null)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
                return Unauthorized();

            character = (character ?? "").Trim().ToLowerInvariant();
            if (!AllowedCharacters.Contains(character))
                return BadRequest(new { ok = false, message = "Character is invalid." });

            var profile = await EnsureCurrentProfileAsync(currentUserId.Value);
            profile.CharacterPreset = character;
            profile.AvatarColor = NormalizeAvatarColor(avatarColor ?? profile.AvatarColor);
            profile.UpdatedAt = DateTime.Now;

            await TouchCurrentUserAsync(currentUserId.Value, saveChanges: false);
            await _context.SaveChangesAsync();

            return Json(new { ok = true, character, avatarColor = profile.AvatarColor });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> SaveArea(
            int? areaId,
            string title,
            string? areaType,
            string? tone,
            int x,
            int y,
            int w,
            int h)
        {
            if (!IsMeetingRoomAdmin())
                return Forbid();

            title = NormalizeAreaTitle(title);
            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { ok = false, message = "Area name is required." });

            areaType = NormalizeAreaType(areaType);
            tone = NormalizeAreaTone(tone);
            var bounds = NormalizeAreaBounds(x, y, w, h);
            var now = DateTime.Now;

            MeetingRoomArea area;
            if (areaId.HasValue && areaId.Value > 0)
            {
                var existingArea = await _context.MeetingRoomAreas.FirstOrDefaultAsync(item => item.AreaId == areaId.Value);
                if (existingArea == null)
                    return NotFound(new { ok = false, message = "Area not found." });

                area = existingArea;
            }
            else
            {
                area = new MeetingRoomArea
                {
                    AreaKey = await CreateUniqueAreaKeyAsync(title),
                    CreatedByUserId = HttpContext.Session.GetInt32("UserId"),
                    CreatedAt = now
                };
                _context.MeetingRoomAreas.Add(area);
            }

            area.Title = title;
            area.AreaType = areaType;
            area.Tone = tone;
            area.X = bounds.X;
            area.Y = bounds.Y;
            area.W = bounds.W;
            area.H = bounds.H;
            area.IsActive = true;
            area.UpdatedAt = now;

            if (area.SortOrder <= 0)
            {
                area.SortOrder = await _context.MeetingRoomAreas
                    .Select(item => (int?)item.SortOrder)
                    .MaxAsync() ?? 0;
                area.SortOrder += 10;
            }

            await _context.SaveChangesAsync();

            return Json(new { ok = true, area = ToZone(area) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> DeleteArea(int areaId)
        {
            if (!IsMeetingRoomAdmin())
                return Forbid();

            var area = await _context.MeetingRoomAreas.FirstOrDefaultAsync(item => item.AreaId == areaId);
            if (area == null)
                return NotFound(new { ok = false, message = "Area not found." });

            area.IsActive = false;
            area.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new { ok = true, areaId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> SaveObject(
            int? objectId,
            string objectKey,
            string? objectType,
            string? title,
            string? tone,
            int x,
            int y,
            int w,
            int h,
            int rotation,
            bool isObstacle = true)
        {
            if (!IsMeetingRoomAdmin())
                return Forbid();

            objectKey = NormalizeObjectKey(objectKey);
            objectType = NormalizeObjectType(objectType, objectKey);
            title = NormalizeObjectTitle(title, objectKey);
            tone = NormalizeObjectTone(tone);
            var bounds = NormalizeObjectBounds(x, y, w, h);
            var now = DateTime.Now;

            MeetingRoomObject roomObject;
            if (objectId.HasValue && objectId.Value > 0)
            {
                var existingObject = await _context.MeetingRoomObjects.FirstOrDefaultAsync(item => item.ObjectId == objectId.Value);
                if (existingObject == null)
                    return NotFound(new { ok = false, message = "Object not found." });

                roomObject = existingObject;
            }
            else
            {
                roomObject = new MeetingRoomObject
                {
                    CreatedByUserId = HttpContext.Session.GetInt32("UserId"),
                    CreatedAt = now
                };
                _context.MeetingRoomObjects.Add(roomObject);
            }

            roomObject.ObjectKey = objectKey;
            roomObject.ObjectType = objectType;
            roomObject.Title = title;
            roomObject.Tone = tone;
            roomObject.X = bounds.X;
            roomObject.Y = bounds.Y;
            roomObject.W = bounds.W;
            roomObject.H = bounds.H;
            roomObject.Rotation = NormalizeObjectRotation(rotation);
            roomObject.IsObstacle = objectType != "RUG" && isObstacle;
            roomObject.IsActive = true;
            roomObject.UpdatedAt = now;

            if (roomObject.SortOrder <= 0)
            {
                roomObject.SortOrder = await _context.MeetingRoomObjects
                    .Select(item => (int?)item.SortOrder)
                    .MaxAsync() ?? 0;
                roomObject.SortOrder += 10;
            }

            await _context.SaveChangesAsync();

            return Json(new { ok = true, item = ToObject(roomObject) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> DeleteObject(int objectId)
        {
            if (!IsMeetingRoomAdmin())
                return Forbid();

            var roomObject = await _context.MeetingRoomObjects.FirstOrDefaultAsync(item => item.ObjectId == objectId);
            if (roomObject == null)
                return NotFound(new { ok = false, message = "Object not found." });

            roomObject.IsActive = false;
            roomObject.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new { ok = true, objectId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> Wave(int targetUserId)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
                return Unauthorized();

            if (targetUserId == currentUserId.Value)
                return BadRequest(new { ok = false, message = "Cannot wave yourself." });

            var target = await _context.LoginUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.UserId == targetUserId && user.Status == "ACTIVE");

            if (target == null)
                return NotFound(new { ok = false, message = "User not found." });

            var sourceName = await ResolveDisplayNameAsync(currentUserId.Value);
            var targetEmpId = await ResolveEmployeeIdAsync(target);
            var now = DateTime.Now;

            _context.UserNotifications.Add(new UserNotification
            {
                RecipientUserId = target.UserId,
                RecipientEmpId = targetEmpId,
                SourceType = "MEETING_ROOM_WAVE",
                SourceId = CreateSourceId(),
                Title = $"{sourceName} waved at you",
                Message = $"{sourceName} เรียกคุณจาก Meeting Room",
                TargetUrl = "/MeetingRoom",
                Severity = "INFO",
                CreatedAt = now,
                UpdatedAt = now
            });

            await TouchCurrentUserAsync(currentUserId.Value, saveChanges: false);
            await _context.SaveChangesAsync();

            var targetName = await ResolveDisplayNameAsync(target.UserId);
            return Json(new { ok = true, message = $"ส่ง Wave หา {targetName} แล้ว" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("MeetingRoom.Index")]
        public async Task<IActionResult> Message(int targetUserId, string message)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (!currentUserId.HasValue)
                return Unauthorized();

            message = (message ?? "").Trim();
            if (string.IsNullOrWhiteSpace(message))
                return BadRequest(new { ok = false, message = "Message is required." });

            if (message.Length > 500)
                message = message[..500];

            var target = await _context.LoginUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.UserId == targetUserId && user.Status == "ACTIVE");

            if (target == null)
                return NotFound(new { ok = false, message = "User not found." });

            var sourceName = await ResolveDisplayNameAsync(currentUserId.Value);
            var targetEmpId = await ResolveEmployeeIdAsync(target);
            var now = DateTime.Now;

            _context.UserNotifications.Add(new UserNotification
            {
                RecipientUserId = target.UserId,
                RecipientEmpId = targetEmpId,
                SourceType = "MEETING_ROOM_MESSAGE",
                SourceId = CreateSourceId(),
                Title = $"Message from {sourceName}",
                Message = message,
                TargetUrl = "/MeetingRoom",
                Severity = "INFO",
                CreatedAt = now,
                UpdatedAt = now
            });

            await TouchCurrentUserAsync(currentUserId.Value, saveChanges: false);
            await _context.SaveChangesAsync();

            var targetName = await ResolveDisplayNameAsync(target.UserId);
            return Json(new { ok = true, message = $"ส่ง Message หา {targetName} แล้ว" });
        }

        private async Task<MeetingRoomViewModel> BuildViewModelAsync(
            int? currentUserId,
            bool isGuest = false,
            int? focusUserId = null)
        {
            var now = DateTime.Now;
            var onlineCutoff = now.Subtract(OnlineWindow);
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            var users = await _context.LoginUsers
                .AsNoTracking()
                .Where(user => user.Status == "ACTIVE")
                .OrderByDescending(user => user.LastSeenAt)
                .ThenBy(user => user.Username)
                .ToListAsync();

            var employees = await _context.Employees
                .AsNoTracking()
                .Where(employee => employee.Status == "ACTIVE")
                .OrderBy(employee => employee.EmpName)
                .ToListAsync();

            var profiles = await _context.MeetingRoomProfiles
                .AsNoTracking()
                .ToDictionaryAsync(profile => profile.UserId);

            var issueCounts = await _context.ProjectIssues
                .AsNoTracking()
                .Where(issue => !ClosedIssueStatuses.Contains(issue.IssueStatus.ToUpper()))
                .GroupBy(issue => issue.AssignTo)
                .Select(group => new { EmpId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.EmpId, row => row.Count);

            var supportCounts = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Where(order => order.AssignTo.HasValue &&
                    !ClosedSupportStatuses.Contains((order.Status ?? "").ToUpper()))
                .GroupBy(order => order.AssignTo!.Value)
                .Select(group => new { EmpId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.EmpId, row => row.Count);

            var followupCounts = await _context.ProjectFollowups
                .AsNoTracking()
                .Where(followup => followup.OwnerEmpId.HasValue &&
                    !ClosedFollowupStatuses.Contains((followup.Status ?? "").ToUpper()))
                .GroupBy(followup => followup.OwnerEmpId!.Value)
                .Select(group => new { EmpId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.EmpId, row => row.Count);

            var assignCounts = await _context.PhaseAssigns
                .AsNoTracking()
                .Where(assign => !ClosedAssignStatuses.Contains((assign.WorkStatus ?? "").ToUpper()))
                .GroupBy(assign => assign.EmpId)
                .Select(group => new { EmpId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.EmpId, row => row.Count);

            var todayMeetings = await _context.Meetings
                .AsNoTracking()
                .Include(meeting => meeting.Project)
                    .ThenInclude(project => project!.Coop)
                .Where(meeting => meeting.Status == "ACTIVE" &&
                    meeting.MeetingDate >= today &&
                    meeting.MeetingDate < tomorrow)
                .OrderBy(meeting => meeting.StartTime)
                .ThenBy(meeting => meeting.Title)
                .Take(8)
                .ToListAsync();

            var customAreas = await _context.MeetingRoomAreas
                .AsNoTracking()
                .Where(area => area.IsActive)
                .OrderBy(area => area.SortOrder)
                .ThenBy(area => area.Title)
                .ToListAsync();

            var roomObjects = await _context.MeetingRoomObjects
                .AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Title)
                .ToListAsync();

            var employeesByUserId = employees
                .Where(employee => employee.LoginUserId.HasValue)
                .GroupBy(employee => employee.LoginUserId!.Value)
                .ToDictionary(group => group.Key, group => group.First());

            var employeesByEmpId = employees
                .GroupBy(employee => employee.EmpId)
                .ToDictionary(group => group.Key, group => group.First());

            var people = users
                .Select((user, index) =>
                {
                    Employee? employee = null;
                    if (user.EmpId.HasValue)
                        employeesByEmpId.TryGetValue(user.EmpId.Value, out employee);

                    if (employee == null)
                        employeesByUserId.TryGetValue(user.UserId, out employee);

                    profiles.TryGetValue(user.UserId, out var profile);
                    var slot = DeskSlots[Math.Abs(user.UserId + index) % DeskSlots.Length];
                    var isOnline = (currentUserId.HasValue && user.UserId == currentUserId.Value) ||
                        (user.LastSeenAt.HasValue && user.LastSeenAt.Value >= onlineCutoff);
                    var status = NormalizeStatus(profile?.Status ?? "AVAILABLE");
                    var empId = employee?.EmpId ?? user.EmpId;
                    var isCurrentUser = currentUserId.HasValue && user.UserId == currentUserId.Value;
                    var realDisplayName = DisplayName(user, employee);
                    var customDisplayName = NormalizeRoomDisplayName(profile?.DisplayName) ?? "";
                    var roomDisplayName = RoomDisplayName(customDisplayName, realDisplayName);

                    return new MeetingRoomPersonViewModel
                    {
                        UserId = user.UserId,
                        EmpId = empId,
                        Username = user.Username ?? "",
                        DisplayName = roomDisplayName,
                        RoomDisplayName = customDisplayName,
                        Initial = Initial(roomDisplayName),
                        Role = user.Role ?? "",
                        Position = employee?.Position ?? (user.Role ?? "Team"),
                        AvatarPath = ResolveProfileImagePath(user.ProfileImagePath),
                        Status = status,
                        StatusLabel = isOnline ? StatusLabel(status) : "Offline",
                        StatusText = profile?.StatusText ?? "",
                        CharacterPreset = NormalizeCharacter(profile?.CharacterPreset),
                        AvatarColor = NormalizeAvatarColor(profile?.AvatarColor),
                        Zone = profile?.HomeZone ?? slot.Zone,
                        X = ClampPercent(profile?.DeskX ?? slot.X),
                        Y = ClampPercent(profile?.DeskY ?? slot.Y),
                        IsOnline = isOnline,
                        IsCurrentUser = isCurrentUser,
                        LastSeenAt = user.LastSeenAt,
                        IssueCount = empId.HasValue && issueCounts.TryGetValue(empId.Value, out var issues) ? issues : 0,
                        SupportCount = empId.HasValue && supportCounts.TryGetValue(empId.Value, out var support) ? support : 0,
                        FollowupCount = empId.HasValue && followupCounts.TryGetValue(empId.Value, out var followups) ? followups : 0,
                        AssignCount = empId.HasValue && assignCounts.TryGetValue(empId.Value, out var assigns) ? assigns : 0
                    };
                })
                .Where(person => person.IsOnline)
                .OrderByDescending(person => person.IsCurrentUser)
                .ThenBy(person => person.DisplayName)
                .ToList();

            var currentUser = currentUserId.HasValue
                ? people.FirstOrDefault(person => person.UserId == currentUserId.Value)
                : null;

            if (currentUser == null)
            {
                currentUser = isGuest
                    ? new MeetingRoomPersonViewModel
                    {
                        UserId = 0,
                        DisplayName = "Guest",
                        Initial = "G",
                        Position = "Visitor",
                        Status = "AVAILABLE",
                        StatusLabel = "Guest view",
                        CharacterPreset = DefaultCharacterPreset,
                        AvatarColor = DefaultAvatarColor,
                        Zone = "Lobby",
                        IsOnline = true
                    }
                    : new MeetingRoomPersonViewModel
                    {
                        UserId = currentUserId ?? 0,
                        DisplayName = HttpContext.Session.GetString("Username") ?? "Me",
                        Initial = Initial(HttpContext.Session.GetString("Username") ?? "M"),
                        CharacterPreset = DefaultCharacterPreset,
                        AvatarColor = DefaultAvatarColor,
                        IsCurrentUser = true,
                        IsOnline = true
                    };
            }

            if (isGuest)
            {
                foreach (var person in people)
                {
                    person.IssueCount = 0;
                    person.SupportCount = 0;
                    person.FollowupCount = 0;
                    person.AssignCount = 0;
                }
            }

            var exposedTodayMeetings = isGuest
                ? new List<MeetingRoomTodayMeetingViewModel>()
                : todayMeetings.Select(meeting => new MeetingRoomTodayMeetingViewModel
                {
                    Id = meeting.Id,
                    Title = meeting.Title,
                    ProjectName = meeting.Project?.ProjectDisplayName ?? "General",
                    TimeText = $"{meeting.StartTime:hh\\:mm} - {meeting.EndTime:hh\\:mm}",
                    Location = string.IsNullOrWhiteSpace(meeting.Location) ? "Meeting Room" : meeting.Location
                }).ToList();

            var exposedMeetingCount = isGuest ? 0 : todayMeetings.Count;
            var exposedIssueCount = isGuest ? 0 : people.Sum(person => person.IssueCount);
            var exposedSupportCount = isGuest ? 0 : people.Sum(person => person.SupportCount);
            var exposedFollowupCount = isGuest ? 0 : people.Sum(person => person.FollowupCount);

            return new MeetingRoomViewModel
            {
                IsGuest = isGuest,
                CanCustomize = !isGuest && IsMeetingRoomAdmin(),
                FocusUserId = focusUserId,
                ShareLink = BuildGuestLink(isGuest ? focusUserId : currentUserId),
                CurrentUser = currentUser,
                People = people,
                Objects = roomObjects.Select(ToObject).ToList(),
                TodayMeetings = exposedTodayMeetings,
                Stats = new MeetingRoomStatsViewModel
                {
                    OnlineCount = people.Count(person => person.IsOnline),
                    TotalPeople = people.Count,
                    TodayMeetingCount = exposedMeetingCount,
                    OpenIssueCount = exposedIssueCount,
                    OpenSupportCount = exposedSupportCount,
                    OpenFollowupCount = exposedFollowupCount
                },
                Zones = BuildZones(exposedMeetingCount, exposedIssueCount, exposedSupportCount, exposedFollowupCount, customAreas)
            };
        }

        private static List<MeetingRoomZoneViewModel> BuildZones(
            int todayMeetingCount,
            int issueCount,
            int supportCount,
            int followupCount,
            IReadOnlyList<MeetingRoomArea>? customAreas = null)
        {
            var zones = new List<MeetingRoomZoneViewModel>
            {
                new() { Key = "lobby", Title = "Lobby", Subtitle = "จุดรวมทีม", Url = "/Home", Tone = "blue", Count = 0, X = 5, Y = 6, W = 18, H = 20 },
                new() { Key = "meeting", Title = "Meeting Room", Subtitle = "ประชุมวันนี้", Url = "/Meetings", Tone = "teal", Count = todayMeetingCount, X = 28, Y = 6, W = 28, H = 20 },
                new() { Key = "project", Title = "Project Room", Subtitle = "สถานะโครงการ", Url = "/ProjectStatus", Tone = "violet", Count = 0, X = 61, Y = 6, W = 23, H = 20 },
                new() { Key = "issue", Title = "Issue Desk", Subtitle = "Issue ที่ยังเปิด", Url = "/ProjectIssues", Tone = "rose", Count = issueCount, X = 68, Y = 37, W = 20, H = 22 },
                new() { Key = "support", Title = "Support Desk", Subtitle = "Support ที่ยังเปิด", Url = "/SupportOrders", Tone = "orange", Count = supportCount, X = 7, Y = 38, W = 18, H = 22 },
                new() { Key = "followup", Title = "Followup Corner", Subtitle = "งานติดตาม", Url = "/Followups", Tone = "green", Count = followupCount, X = 74, Y = 68, W = 18, H = 20 },
                new() { Key = "board", Title = "Project Board", Subtitle = "Requirement Board", Url = "/RequirementBoard", Tone = "cyan", Count = 0, X = 35, Y = 70, W = 24, H = 18 }
            };

            if (customAreas != null)
            {
                zones.AddRange(customAreas.Select(ToZone));
            }

            return zones;
        }

        private static MeetingRoomZoneViewModel ToZone(MeetingRoomArea area)
        {
            var x = Math.Clamp(area.X, 0, 98);
            var y = Math.Clamp(area.Y, 0, 98);
            var w = Math.Clamp(area.W, 2, 100);
            var h = Math.Clamp(area.H, 2, 100);

            if (x + w > 100)
                w = Math.Max(2, 100 - x);

            if (y + h > 100)
                h = Math.Max(2, 100 - y);

            return new MeetingRoomZoneViewModel
            {
                AreaId = area.AreaId,
                Key = string.IsNullOrWhiteSpace(area.AreaKey) ? NormalizeAreaKey(area.Title) : area.AreaKey,
                Title = area.Title,
                Subtitle = AreaTypeLabel(area.AreaType),
                AreaType = NormalizeAreaType(area.AreaType),
                Url = "#",
                Tone = NormalizeAreaTone(area.Tone),
                IsCustom = true,
                Count = 0,
                X = x,
                Y = y,
                W = w,
                H = h
            };
        }

        private static MeetingRoomObjectViewModel ToObject(MeetingRoomObject roomObject)
        {
            return new MeetingRoomObjectViewModel
            {
                ObjectId = roomObject.ObjectId,
                ObjectKey = NormalizeObjectKey(roomObject.ObjectKey),
                ObjectType = NormalizeObjectType(roomObject.ObjectType, roomObject.ObjectKey),
                Title = NormalizeObjectTitle(roomObject.Title, roomObject.ObjectKey),
                Tone = NormalizeObjectTone(roomObject.Tone),
                X = ClampPercent(roomObject.X),
                Y = ClampPercent(roomObject.Y),
                W = Math.Clamp(roomObject.W, 2, 24),
                H = Math.Clamp(roomObject.H, 2, 20),
                Rotation = NormalizeObjectRotation(roomObject.Rotation),
                IsObstacle = roomObject.IsObstacle,
                IsCustom = true
            };
        }

        private async Task<string> CreateUniqueAreaKeyAsync(string title)
        {
            var baseKey = NormalizeAreaKey(title);
            var key = baseKey;
            var suffix = 2;

            while (await _context.MeetingRoomAreas.AnyAsync(area => area.AreaKey == key))
            {
                key = $"{baseKey}-{suffix}";
                suffix++;
            }

            return key;
        }

        private bool IsMeetingRoomAdmin()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            return string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAreaTitle(string? title)
        {
            title = (title ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            while (title.Contains("  ", StringComparison.Ordinal))
                title = title.Replace("  ", " ");

            return title.Length > 100 ? title[..100] : title;
        }

        private static string NormalizeAreaKey(string? title)
        {
            title = NormalizeAreaTitle(title).ToLowerInvariant();
            var chars = title
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray();
            var key = new string(chars).Trim('-');

            while (key.Contains("--", StringComparison.Ordinal))
                key = key.Replace("--", "-");

            return string.IsNullOrWhiteSpace(key) ? "meeting-area" : key[..Math.Min(key.Length, 70)];
        }

        private static string NormalizeAreaType(string? areaType)
        {
            areaType = (areaType ?? "MEETING").Trim().ToUpperInvariant();
            return areaType switch
            {
                "DESK" => "DESK",
                "LOUNGE" => "LOUNGE",
                "WORK" => "WORK",
                "QUIET" => "QUIET",
                _ => "MEETING"
            };
        }

        private static string AreaTypeLabel(string? areaType)
        {
            return NormalizeAreaType(areaType) switch
            {
                "DESK" => "พื้นที่โต๊ะทำงาน",
                "LOUNGE" => "พื้นที่คุยสบายๆ",
                "WORK" => "พื้นที่ทำงานร่วมกัน",
                "QUIET" => "พื้นที่เงียบ",
                _ => "พื้นที่ประชุม"
            };
        }

        private static string NormalizeAreaTone(string? tone)
        {
            tone = (tone ?? "teal").Trim().ToLowerInvariant();
            return AllowedAreaTones.Contains(tone) ? tone : "teal";
        }

        private static (int X, int Y, int W, int H) NormalizeAreaBounds(int x, int y, int w, int h)
        {
            x = Math.Clamp(x, 0, 98);
            y = Math.Clamp(y, 0, 98);
            w = Math.Clamp(w, 2, 100);
            h = Math.Clamp(h, 2, 100);

            if (x + w > 100)
                w = Math.Max(2, 100 - x);

            if (y + h > 100)
                h = Math.Max(2, 100 - y);

            return (x, y, w, h);
        }

        private static string NormalizeObjectKey(string? objectKey)
        {
            objectKey = (objectKey ?? "desk-basic").Trim().ToLowerInvariant();
            return AllowedObjectKeys.Contains(objectKey) ? objectKey : "desk-basic";
        }

        private static string NormalizeObjectType(string? objectType, string? objectKey = null)
        {
            objectType = (objectType ?? "").Trim().ToUpperInvariant();
            if (AllowedObjectTypes.Contains(objectType))
                return objectType;

            return NormalizeObjectKey(objectKey) switch
            {
                "chair-office" => "CHAIR",
                "sofa-blue" => "SOFA",
                "table-round" => "TABLE",
                "plant-pot" => "PLANT",
                "plant-tall" => "PLANT",
                "rug-blue" => "RUG",
                "rug-round" => "RUG",
                "partition" => "DIVIDER",
                "screen" => "SCREEN",
                "board" => "BOARD",
                _ => "DESK"
            };
        }

        private static string NormalizeObjectTitle(string? title, string? objectKey = null)
        {
            title = NormalizeAreaTitle(title);
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            return NormalizeObjectKey(objectKey) switch
            {
                "chair-office" => "Office chair",
                "sofa-blue" => "Sofa",
                "table-round" => "Round table",
                "plant-pot" => "Plant",
                "plant-tall" => "Tall plant",
                "rug-blue" => "Rug",
                "rug-round" => "Round rug",
                "partition" => "Partition",
                "screen" => "Screen",
                "board" => "Board",
                _ => "Desk"
            };
        }

        private static string NormalizeObjectTone(string? tone)
        {
            tone = (tone ?? "wood").Trim().ToLowerInvariant();
            return AllowedObjectTones.Contains(tone) ? tone : "wood";
        }

        private static int NormalizeObjectRotation(int rotation)
        {
            rotation %= 360;
            if (rotation < 0)
                rotation += 360;

            return rotation switch
            {
                < 45 => 0,
                < 135 => 90,
                < 225 => 180,
                < 315 => 270,
                _ => 0
            };
        }

        private static (int X, int Y, int W, int H) NormalizeObjectBounds(int x, int y, int w, int h)
        {
            x = Math.Clamp(x, 4, 96);
            y = Math.Clamp(y, 7, 86);
            w = Math.Clamp(w, 2, 24);
            h = Math.Clamp(h, 2, 20);

            if (x + w > 97)
                w = Math.Max(2, 97 - x);

            if (y + h > 88)
                h = Math.Max(2, 88 - y);

            return (x, y, w, h);
        }

        private async Task<MeetingRoomProfile> EnsureCurrentProfileAsync(int userId)
        {
            var profile = await _context.MeetingRoomProfiles.FirstOrDefaultAsync(x => x.UserId == userId);
            if (profile != null)
                return profile;

            var slot = DeskSlots[Math.Abs(userId) % DeskSlots.Length];
            profile = new MeetingRoomProfile
            {
                UserId = userId,
                Status = "AVAILABLE",
                CharacterPreset = DefaultCharacterPreset,
                AvatarColor = DefaultAvatarColor,
                DeskX = slot.X,
                DeskY = slot.Y,
                HomeZone = slot.Zone,
                UpdatedAt = DateTime.Now
            };

            _context.MeetingRoomProfiles.Add(profile);
            await _context.SaveChangesAsync();
            return profile;
        }

        private async Task TouchCurrentUserAsync(int userId, bool saveChanges = true)
        {
            var user = await _context.LoginUsers.FirstOrDefaultAsync(x => x.UserId == userId);
            if (user == null)
                return;

            user.LastSeenAt = DateTime.Now;
            if (saveChanges)
                await _context.SaveChangesAsync();
        }

        private async Task<string> ResolveDisplayNameAsync(int userId)
        {
            var user = await _context.LoginUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (user == null)
                return "Someone";

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LoginUserId == user.UserId ||
                    (user.EmpId.HasValue && x.EmpId == user.EmpId.Value));

            var profile = await _context.MeetingRoomProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == user.UserId);

            return RoomDisplayName(profile?.DisplayName, DisplayName(user, employee));
        }

        private async Task<int?> ResolveEmployeeIdAsync(LoginUser user)
        {
            if (user.EmpId.HasValue)
                return user.EmpId.Value;

            return await _context.Employees
                .AsNoTracking()
                .Where(employee => employee.LoginUserId == user.UserId)
                .Select(employee => (int?)employee.EmpId)
                .FirstOrDefaultAsync();
        }

        private static string DisplayName(LoginUser user, Employee? employee = null)
        {
            var name = employee?.EmpName;
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            return string.IsNullOrWhiteSpace(user.Username) ? $"User #{user.UserId}" : user.Username.Trim();
        }

        private static string RoomDisplayName(string? roomName, string fallbackName)
        {
            roomName = NormalizeRoomDisplayName(roomName);
            return string.IsNullOrWhiteSpace(roomName) ? fallbackName : roomName;
        }

        private static string? NormalizeRoomDisplayName(string? displayName)
        {
            var name = (displayName ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            while (name.Contains("  ", StringComparison.Ordinal))
                name = name.Replace("  ", " ");

            if (string.IsNullOrWhiteSpace(name))
                return null;

            return name.Length > 50 ? name[..50] : name;
        }

        private static string Initial(string value)
        {
            value = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(value)
                ? "?"
                : value[..1].ToUpperInvariant();
        }

        private string ResolveProfileImagePath(string? profileImagePath)
        {
            if (string.IsNullOrWhiteSpace(profileImagePath))
                return DefaultProfileImagePath;

            var path = profileImagePath.Trim().Replace("\\", "/");
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return path;

            if (path.StartsWith("~/", StringComparison.Ordinal))
                path = path[1..];

            if (path.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
                path = path["wwwroot".Length..];

            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;

            if (path.Length <= 1)
                return DefaultProfileImagePath;

            var webRootPath = _webHostEnvironment.WebRootPath
                ?? System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot");
            var physicalPath = System.IO.Path.Combine(webRootPath, path.TrimStart('/'));
            return System.IO.File.Exists(physicalPath) ? path : DefaultProfileImagePath;
        }

        private static int ClampPercent(int value)
            => Math.Clamp(value, 4, 96);

        private static string NormalizeStatus(string? status)
        {
            status = (status ?? "AVAILABLE").Trim().ToUpperInvariant();
            return AllowedStatuses.Contains(status) ? status : "AVAILABLE";
        }

        private static string NormalizeCharacter(string? character)
        {
            character = (character ?? DefaultCharacterPreset).Trim().ToLowerInvariant();
            return AllowedCharacters.Contains(character) ? character : DefaultCharacterPreset;
        }

        private static string NormalizeAvatarColor(string? avatarColor)
        {
            avatarColor = (avatarColor ?? "").Trim();
            if (avatarColor.Length != 7 || avatarColor[0] != '#')
                return DefaultAvatarColor;

            for (var index = 1; index < avatarColor.Length; index++)
            {
                var value = avatarColor[index];
                var isHex = value is >= '0' and <= '9' ||
                    value is >= 'a' and <= 'f' ||
                    value is >= 'A' and <= 'F';

                if (!isHex)
                    return DefaultAvatarColor;
            }

            return avatarColor.ToLowerInvariant();
        }

        private static string StatusLabel(string status)
        {
            return NormalizeStatus(status) switch
            {
                "BUSY" => "Busy",
                "DND" => "Do Not Disturb",
                "AWAY" => "Away",
                "MEETING" => "In Meeting",
                _ => "Available"
            };
        }

        private static int CreateSourceId()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
        }

        private string BuildGuestLink(int? deskUserId = null)
        {
            object? routeValues = deskUserId.HasValue ? new { desk = deskUserId.Value } : null;
            return Url.Action(nameof(Guest), "MeetingRoom", routeValues, Request.Scheme)
                ?? Url.Content("~/MeetingRoom/Guest");
        }
    }
}
