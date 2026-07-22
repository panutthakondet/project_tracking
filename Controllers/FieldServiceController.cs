using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers;

public class FieldServiceController : BaseController
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "PLANNED", "IN_PROGRESS", "COMPLETED", "CANCELLED" };

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".zip" };
    public FieldServiceController(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [RequireMenu("FieldService.Index")]
    public async Task<IActionResult> Index(string? q, string? status, string? from, string? to)
    {
        var query = _context.FieldServiceVisits.AsNoTracking()
            .Include(x => x.Coop)
            .Include(x => x.Assignees).ThenInclude(x => x.Employee)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(x => x.Title.Contains(q) ||
                (x.Coop != null && x.Coop.CoopName.Contains(q)));
        }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var fromDate = ParseDate(from);
        var toDate = ParseDate(to);
        if (fromDate.HasValue) query = query.Where(x => (x.EndVisitDate ?? x.VisitDate) >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(x => x.VisitDate <= toDate.Value.Date);

        ViewBag.Query = q;
        ViewBag.Status = status;
        ViewBag.From = ThaiDate(fromDate);
        ViewBag.To = ThaiDate(toDate);
        return View(await query.OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.VisitId).ToListAsync());
    }

    [RequireMenu("FieldService.Show")]
    public async Task<IActionResult> Show(int id)
    {
        var item = await _context.FieldServiceVisits.AsNoTracking()
            .Include(x => x.Coop)
            .Include(x => x.Assignees).ThenInclude(x => x.Employee).ThenInclude(x => x!.LoginUser)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.VisitId == id);
        if (item != null)
            ViewBag.EmployeeProfileImages = await LoadEmployeeProfilePathsAsync(
                item.Assignees.Select(x => x.EmpId));
        if (item?.CreatedBy is int creatorId)
        {
            ViewBag.CreatedByName = await _context.Employees.AsNoTracking()
                .Where(x => x.EmpId == creatorId).Select(x => x.EmpName).FirstOrDefaultAsync();
        }
        ViewBag.CanSubmitResult = item != null && await CanSubmitResultAsync(item.VisitId);
        return item == null ? NotFound() : View(item);
    }

    [HttpGet, RequireMenu("FieldService.Result")]
    public async Task<IActionResult> Results(string? q, string? status)
    {
        var query = _context.FieldServiceVisits.AsNoTracking()
            .Include(x => x.Coop)
            .Include(x => x.Assignees).ThenInclude(x => x.Employee).ThenInclude(x => x!.LoginUser)
            .Include(x => x.Attachments)
            .AsQueryable();
        if (!IsAdminUser())
        {
            var empId = HttpContext.Session.GetInt32("EmpId");
            if (!empId.HasValue)
                query = query.Where(x => false);
            else
                query = query.Where(x => x.Assignees.Any(a => a.EmpId == empId.Value));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(x => x.Title.Contains(q) || (x.Coop != null && x.Coop.CoopName.Contains(q)));
        }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        ViewBag.Query = q;
        ViewBag.Status = status;
        var items = await query
            .OrderBy(x => x.Status == "COMPLETED" ? 1 : 0)
            .ThenByDescending(x => x.VisitDate)
            .ToListAsync();
        ViewBag.EmployeeProfileImages = await LoadEmployeeProfilePathsAsync(
            items.SelectMany(x => x.Assignees).Select(x => x.EmpId));
        return View(items);
    }

    [HttpGet, RequireMenu("FieldService.Result")]
    public async Task<IActionResult> SubmitResult(int id)
    {
        if (!await CanSubmitResultAsync(id))
            return RedirectToAction("AccessDenied", "Auth", new { key = "FieldService.Result.AssignedOnly" });
        var item = await _context.FieldServiceVisits.AsNoTracking().Include(x => x.Coop).Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.VisitId == id);
        if (item == null) return NotFound();
        return View(new FieldServiceResultViewModel
        {
            VisitId = item.VisitId,
            Title = item.Title,
            CoopName = item.Coop?.CoopName ?? "-",
            ServiceResult = item.ServiceResult ?? string.Empty,
            Status = item.Status == "COMPLETED" ? "COMPLETED" : "COMPLETED",
            NextVisitDate = item.NextVisitDate,
            ExistingAttachments = item.Attachments.OrderByDescending(x => x.UploadedAt).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(55_000_000), RequireMenu("FieldService.Result")]
    public async Task<IActionResult> SubmitResult(FieldServiceResultViewModel model)
    {
        if (!await CanSubmitResultAsync(model.VisitId))
            return RedirectToAction("AccessDenied", "Auth", new { key = "FieldService.Result.AssignedOnly" });
        var item = await _context.FieldServiceVisits.Include(x => x.Coop).Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.VisitId == model.VisitId);
        if (item == null) return NotFound();
        model.Title = item.Title;
        model.CoopName = item.Coop?.CoopName ?? "-";
        model.ExistingAttachments = item.Attachments.OrderByDescending(x => x.UploadedAt).ToList();
        model.Status = model.Status is "COMPLETED" or "IN_PROGRESS" ? model.Status : "COMPLETED";

        const long maxFileSize = 20L * 1024 * 1024;
        if (model.Files.Count > 10) ModelState.AddModelError(nameof(model.Files), "แนบไฟล์ได้ไม่เกิน 10 ไฟล์ต่อครั้ง");
        foreach (var file in model.Files.Where(x => x.Length > 0))
        {
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedAttachmentExtensions.Contains(extension))
                ModelState.AddModelError(nameof(model.Files), $"ไม่รองรับไฟล์ {file.FileName}");
            if (file.Length > maxFileSize)
                ModelState.AddModelError(nameof(model.Files), $"ไฟล์ {file.FileName} มีขนาดเกิน 20 MB");
        }
        if (!ModelState.IsValid) return View(model);

        var folder = Path.Combine(_environment.WebRootPath, "uploads", "field-service", item.VisitId.ToString());
        Directory.CreateDirectory(folder);
        var savedPaths = new List<string>();
        var pathsToDelete = new List<string>();
        try
        {
            var removeIds = model.DeleteAttachmentIds.Distinct().ToHashSet();
            var attachmentsToRemove = item.Attachments.Where(x => removeIds.Contains(x.AttachmentId)).ToList();
            foreach (var attachment in attachmentsToRemove)
            {
                var relative = attachment.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var physical = Path.GetFullPath(Path.Combine(_environment.WebRootPath, relative));
                var allowedRoot = Path.GetFullPath(Path.Combine(_environment.WebRootPath, "uploads", "field-service")) + Path.DirectorySeparatorChar;
                if (physical.StartsWith(allowedRoot, StringComparison.Ordinal)) pathsToDelete.Add(physical);
            }
            _context.FieldServiceAttachments.RemoveRange(attachmentsToRemove);

            foreach (var file in model.Files.Where(x => x.Length > 0))
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var storedName = $"{Guid.NewGuid():N}{extension}";
                var physicalPath = Path.Combine(folder, storedName);
                await using (var stream = new FileStream(physicalPath, FileMode.CreateNew))
                    await file.CopyToAsync(stream);
                savedPaths.Add(physicalPath);
                item.Attachments.Add(new FieldServiceAttachment
                {
                    FileName = Path.GetFileName(file.FileName),
                    FilePath = $"/uploads/field-service/{item.VisitId}/{storedName}",
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    UploadedBy = HttpContext.Session.GetInt32("EmpId"),
                    UploadedAt = DateTime.Now
                });
            }
            item.ServiceResult = model.ServiceResult.Trim();
            item.Status = model.Status;
            item.NextVisitDate = model.NextVisitDate?.Date;
            item.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            foreach (var path in pathsToDelete) if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch
        {
            foreach (var path in savedPaths) if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
        TempData["Success"] = "ส่งผลการเข้าปฏิบัติงานเรียบร้อยแล้ว";
        return RedirectToAction(nameof(Show), new { id = item.VisitId });
    }

    [HttpGet, RequireMenu("FieldService.Create")]
    public async Task<IActionResult> Create(DateTime? start, DateTime? end)
    {
        var model = new FieldServiceFormViewModel
        {
            VisitDate = start?.Date ?? DateTime.Today,
            EndVisitDate = end?.Date ?? start?.Date ?? DateTime.Today
        };
        await FillOptions(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("FieldService.Create")]
    public async Task<IActionResult> Create(FieldServiceFormViewModel model)
    {
        ValidateForm(model);
        if (!ModelState.IsValid) { await FillOptions(model); return View(model); }

        var item = new FieldServiceVisit();
        Apply(model, item);
        item.CreatedBy = HttpContext.Session.GetInt32("EmpId");
        item.CreatedAt = item.UpdatedAt = DateTime.Now;
        item.Assignees = model.AssigneeIds.Distinct().Select(id => new FieldServiceAssignee { EmpId = id }).ToList();
        _context.Add(item);
        await _context.SaveChangesAsync();
        TempData["Success"] = "สร้างงานออกไซต์เรียบร้อยแล้ว";
        return RedirectToAction(nameof(Show), new { id = item.VisitId });
    }

    [HttpGet, RequireMenu("FieldService.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _context.FieldServiceVisits.AsNoTracking().Include(x => x.Assignees)
            .FirstOrDefaultAsync(x => x.VisitId == id);
        if (item == null) return NotFound();
        var model = ToForm(item);
        await FillOptions(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("FieldService.Edit")]
    public async Task<IActionResult> Edit(int id, FieldServiceFormViewModel model)
    {
        if (id != model.VisitId) return BadRequest();
        ValidateForm(model);
        if (!ModelState.IsValid) { await FillOptions(model); return View(model); }

        var item = await _context.FieldServiceVisits.Include(x => x.Assignees)
            .FirstOrDefaultAsync(x => x.VisitId == id);
        if (item == null) return NotFound();
        Apply(model, item);
        item.UpdatedAt = DateTime.Now;
        _context.FieldServiceAssignees.RemoveRange(item.Assignees);
        item.Assignees = model.AssigneeIds.Distinct().Select(empId => new FieldServiceAssignee { EmpId = empId }).ToList();
        await _context.SaveChangesAsync();
        TempData["Success"] = "บันทึกการแก้ไขเรียบร้อยแล้ว";
        return RedirectToAction(nameof(Show), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireMenu("FieldService.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.FieldServiceVisits.FindAsync(id);
        if (item == null) return NotFound();
        _context.Remove(item);
        await _context.SaveChangesAsync();
        TempData["Success"] = "ลบงานออกไซต์เรียบร้อยแล้ว";
        return RedirectToAction(nameof(Index));
    }

    [RequireMenu("FieldService.Calendar")]
    public async Task<IActionResult> Calendar(DateTime? month)
    {
        var selected = month ?? DateTime.Today;
        var first = new DateTime(selected.Year, selected.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        ViewBag.Month = first;
        return View(await _context.FieldServiceVisits.AsNoTracking().Include(x => x.Coop)
            .Where(x => x.VisitDate <= last && (x.EndVisitDate ?? x.VisitDate) >= first)
            .OrderBy(x => x.VisitDate).ThenBy(x => x.StartTime).ToListAsync());
    }

    [RequireMenu("FieldService.Calendar")]
    public async Task<IActionResult> Schedule()
    {
        ViewBag.Employees = await _context.Employees.AsNoTracking()
            .Where(x => x.Status == "ACTIVE")
            .OrderBy(x => x.EmpName)
            .Select(x => new { x.EmpId, x.EmpName, x.Position })
            .ToListAsync();
        return View();
    }

    [RequireMenu("FieldService.Calendar")]
    public async Task<IActionResult> AnnualCalendar(int? fromYear, int? toYear)
    {
        var currentYear = DateTime.Today.Year;
        var startYear = fromYear ?? currentYear;
        var endYear = toYear ?? startYear;
        if (startYear > 2400) startYear -= 543;
        if (endYear > 2400) endYear -= 543;
        startYear = Math.Clamp(startYear, currentYear - 10, currentYear + 10);
        endYear = Math.Clamp(endYear, currentYear - 10, currentYear + 10);
        if (startYear > endYear) (startYear, endYear) = (endYear, startYear);

        var currentEmpId = HttpContext.Session.GetInt32("EmpId");
        var workDates = new HashSet<string>();
        var workDateVisits = new Dictionary<string, List<int>>();
        if (currentEmpId.HasValue)
        {
            ViewBag.CurrentEmployeeName = await _context.Employees.AsNoTracking()
                .Where(x => x.EmpId == currentEmpId.Value)
                .Select(x => x.EmpName)
                .FirstOrDefaultAsync();
            var rangeStart = new DateTime(startYear, 1, 1);
            var rangeEnd = new DateTime(endYear, 12, 31);
            var ranges = await _context.FieldServiceVisits.AsNoTracking()
                .Where(x => x.Assignees.Any(a => a.EmpId == currentEmpId.Value)
                    && x.VisitDate <= rangeEnd
                    && (x.EndVisitDate ?? x.VisitDate) >= rangeStart
                    && x.Status != "CANCELLED")
                .OrderBy(x => x.VisitDate)
                .ThenBy(x => x.StartTime)
                .ThenBy(x => x.VisitId)
                .Select(x => new { x.VisitId, x.VisitDate, x.EndVisitDate })
                .ToListAsync();
            foreach (var range in ranges)
            {
                var start = range.VisitDate.Date < rangeStart ? rangeStart : range.VisitDate.Date;
                var end = (range.EndVisitDate ?? range.VisitDate).Date > rangeEnd
                    ? rangeEnd
                    : (range.EndVisitDate ?? range.VisitDate).Date;
                for (var date = start; date <= end; date = date.AddDays(1))
                {
                    var dateKey = date.ToString("yyyy-MM-dd");
                    workDates.Add(dateKey);
                    if (!workDateVisits.TryGetValue(dateKey, out var visits))
                    {
                        visits = new List<int>();
                        workDateVisits[dateKey] = visits;
                    }
                    if (!visits.Contains(range.VisitId)) visits.Add(range.VisitId);
                }
            }
        }

        ViewBag.FromYear = startYear;
        ViewBag.ToYear = endYear;
        ViewBag.WorkDates = workDates;
        ViewBag.WorkDateVisits = workDateVisits;
        ViewBag.YearOptions = Enumerable.Range(currentYear - 10, 21).ToArray();
        return View();
    }

    [HttpGet, RequireMenu("FieldService.Calendar")]
    public async Task<IActionResult> CalendarEvents(DateTime? start, DateTime? end, int? employeeId)
    {
        var rangeStart = start?.Date ?? DateTime.Today.AddMonths(-1);
        var rangeEnd = end?.Date ?? DateTime.Today.AddMonths(2);
        var query = _context.FieldServiceVisits.AsNoTracking()
            .Include(x => x.Coop)
            .Include(x => x.Assignees).ThenInclude(x => x.Employee).ThenInclude(x => x!.LoginUser)
            .Where(x => x.VisitDate < rangeEnd && (x.EndVisitDate ?? x.VisitDate) >= rangeStart)
            .AsQueryable();
        if (employeeId.HasValue && employeeId.Value > 0)
            query = query.Where(x => x.Assignees.Any(a => a.EmpId == employeeId.Value));
        var rows = await query
            .OrderBy(x => x.VisitDate).ThenBy(x => x.StartTime)
            .ToListAsync();
        var employeeProfileImages = await LoadEmployeeProfilePathsAsync(
            rows.SelectMany(x => x.Assignees).Select(x => x.EmpId));

        return Json(rows.Select(x =>
        {
            var finalDate = (x.EndVisitDate ?? x.VisitDate).Date;
            var isMultiDay = finalDate > x.VisitDate.Date;
            var isAllDay = isMultiDay || !x.StartTime.HasValue;
            var eventEnd = isAllDay
                ? finalDate.AddDays(1).ToString("yyyy-MM-dd")
                : x.VisitDate.ToString("yyyy-MM-dd") + (x.EndTime.HasValue ? $"T{x.EndTime:hh\\:mm\\:ss}" : "T23:59:00");
            return new
            {
                id = x.VisitId,
                title = x.Coop != null ? x.Coop.CoopName : x.Title,
                start = x.VisitDate.ToString("yyyy-MM-dd") + (isAllDay ? "" : $"T{x.StartTime:hh\\:mm\\:ss}"),
                end = eventEnd,
                allDay = isAllDay,
                color = x.Status switch
                {
                    "COMPLETED" => "#16a34a",
                    "IN_PROGRESS" => "#f59e0b",
                    "CANCELLED" => "#64748b",
                    _ => "#0ea5e9"
                },
                url = Url.Action(nameof(Show), new { id = x.VisitId }),
                extendedProps = new
                {
                    coopName = x.Coop != null ? x.Coop.CoopName : "",
                    serviceTitle = x.Title,
                    status = x.Status,
                    employees = x.Assignees
                        .Where(a => a.Employee != null)
                        .OrderBy(a => a.Employee!.EmpName)
                        .Select(a => new
                        {
                            name = a.Employee!.EmpName,
                            image = employeeProfileImages.GetValueOrDefault(a.EmpId, "/images/Profile/profile.png")
                        })
                        .ToList()
                }
            };
        }));
    }

    [RequireMenu("FieldService.Report")]
    public async Task<IActionResult> Report(string? from, string? to)
    {
        var start = ParseDate(from)?.Date ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var end = ParseDate(to)?.Date ?? DateTime.Today;
        ViewBag.From = ThaiDate(start);
        ViewBag.To = ThaiDate(end);
        return View(await _context.FieldServiceVisits.AsNoTracking().Include(x => x.Coop)
            .Where(x => x.VisitDate <= end && (x.EndVisitDate ?? x.VisitDate) >= start)
            .OrderByDescending(x => x.VisitDate).ToListAsync());
    }

    private void ValidateForm(FieldServiceFormViewModel model)
    {
        var requestedStatus = model.Status ?? string.Empty;
        model.Status = AllowedStatuses.Contains(requestedStatus) ? requestedStatus.ToUpperInvariant() : "PLANNED";
        var effectiveEndDate = model.EndVisitDate?.Date ?? model.VisitDate.Date;
        if (effectiveEndDate == model.VisitDate.Date && model.EndTime.HasValue && model.StartTime.HasValue && model.EndTime <= model.StartTime)
            ModelState.AddModelError(nameof(model.EndTime), "เวลาสิ้นสุดต้องมากกว่าเวลาเริ่ม");
        if (model.EndVisitDate.HasValue && model.EndVisitDate.Value.Date < model.VisitDate.Date)
            ModelState.AddModelError(nameof(model.EndVisitDate), "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่ม");
        if (model.CoopId > 0 && !_context.CntMCoops.Any(x => x.CoopId == model.CoopId))
            ModelState.AddModelError(nameof(model.CoopId), "ไม่พบข้อมูลสหกรณ์");
    }

    private async Task FillOptions(FieldServiceFormViewModel model)
    {
        model.Cooperatives = await _context.CntMCoops.AsNoTracking().OrderBy(x => x.CoopName)
            .Select(x => new SelectListItem(x.CoopName, x.CoopId.ToString())).ToListAsync();
        model.Employees = await _context.Employees.AsNoTracking().Where(x => x.Status == "ACTIVE")
            .OrderBy(x => x.EmpName)
            .Select(x => new SelectListItem(
                x.EmpName + (x.Position != null && x.Position != "" ? " (" + x.Position + ")" : ""),
                x.EmpId.ToString(), model.AssigneeIds.Contains(x.EmpId)))
            .ToListAsync();
    }

    private static void Apply(FieldServiceFormViewModel m, FieldServiceVisit x)
    {
        x.CoopId = m.CoopId; x.Title = m.Title.Trim(); x.ServiceType = (m.ServiceType ?? "MA").Trim();
        x.VisitDate = m.VisitDate.Date; x.EndVisitDate = m.EndVisitDate?.Date ?? m.VisitDate.Date;
        x.StartTime = m.StartTime; x.EndTime = m.EndTime;
        x.ContactName = m.ContactName?.Trim(); x.ContactPhone = m.ContactPhone?.Trim();
        x.Description = m.Description?.Trim(); x.ServiceResult = m.ServiceResult?.Trim();
        x.Status = m.Status; x.NextVisitDate = m.NextVisitDate?.Date;
    }

    private static FieldServiceFormViewModel ToForm(FieldServiceVisit x) => new()
    {
        VisitId = x.VisitId, CoopId = x.CoopId, Title = x.Title, ServiceType = x.ServiceType,
        VisitDate = x.VisitDate, EndVisitDate = x.EndVisitDate ?? x.VisitDate,
        StartTime = x.StartTime, EndTime = x.EndTime,
        ContactName = x.ContactName, ContactPhone = x.ContactPhone, Description = x.Description,
        ServiceResult = x.ServiceResult, Status = x.Status, NextVisitDate = x.NextVisitDate,
        AssigneeIds = x.Assignees.Select(a => a.EmpId).ToList()
    };

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var iso)) return iso;
        var parts = value.Trim().Split('/');
        if (parts.Length == 3 && int.TryParse(parts[0], out var day) && int.TryParse(parts[1], out var month) && int.TryParse(parts[2], out var year))
        {
            if (year > 2400) year -= 543;
            try { return new DateTime(year, month, day); } catch { return null; }
        }
        return null;
    }

    private static string ThaiDate(DateTime? value) => value.HasValue
        ? $"{value.Value:dd/MM/}{value.Value.Year + 543}"
        : string.Empty;

    private string NormalizeProfilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/images/Profile/profile.png";
        path = path.Trim().Replace("\\", "/");
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return path;
        if (path.StartsWith("~/", StringComparison.Ordinal)) path = path[1..];
        if (path.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            path = path["wwwroot".Length..];
        if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path.TrimStart('/');

        var webRoot = _environment.WebRootPath
            ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var physicalPath = Path.Combine(webRoot, path.TrimStart('/'));
        return System.IO.File.Exists(physicalPath) ? path : "/images/Profile/profile.png";
    }

    private async Task<Dictionary<int, string>> LoadEmployeeProfilePathsAsync(IEnumerable<int> employeeIds)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();

        var employees = await _context.Employees.AsNoTracking()
            .Where(x => ids.Contains(x.EmpId))
            .Select(x => new { x.EmpId, x.LoginUserId })
            .ToListAsync();
        var loginUserIds = employees.Where(x => x.LoginUserId.HasValue)
            .Select(x => x.LoginUserId!.Value).Distinct().ToList();
        var users = await _context.LoginUsers.AsNoTracking()
            .Where(x => loginUserIds.Contains(x.UserId) || (x.EmpId.HasValue && ids.Contains(x.EmpId.Value)))
            .Select(x => new { x.UserId, x.EmpId, x.ProfileImagePath })
            .ToListAsync();

        var result = new Dictionary<int, string>();
        foreach (var employee in employees)
        {
            var user = employee.LoginUserId.HasValue
                ? users.FirstOrDefault(x => x.UserId == employee.LoginUserId.Value)
                : null;
            user ??= users.FirstOrDefault(x => x.EmpId == employee.EmpId);
            result[employee.EmpId] = NormalizeProfilePath(user?.ProfileImagePath);
        }
        return result;
    }

    private bool IsAdminUser() => string.Equals(
        HttpContext.Session.GetString("Role"), "ADMIN", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> CanSubmitResultAsync(int visitId)
    {
        if (IsAdminUser()) return true;
        var empId = HttpContext.Session.GetInt32("EmpId");
        return empId.HasValue && await _context.FieldServiceAssignees.AsNoTracking()
            .AnyAsync(x => x.VisitId == visitId && x.EmpId == empId.Value);
    }
}
