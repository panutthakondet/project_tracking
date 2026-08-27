using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    public class StatusApprovalsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly StatusApprovalService _statusApprovalService;

        public StatusApprovalsController(
            AppDbContext context,
            StatusApprovalService statusApprovalService)
        {
            _context = context;
            _statusApprovalService = statusApprovalService;
        }

        [RequireMenu("StatusApprovals.Index")]
        public async Task<IActionResult> Index(string? status)
        {
            var selectedStatus = string.IsNullOrWhiteSpace(status)
                ? StatusApprovalService.RequestPending
                : status.Trim().ToUpperInvariant();

            var baseQuery = _context.StatusApprovalRequests.AsNoTracking();

            if (!_statusApprovalService.IsCurrentUserAdmin())
            {
                var currentEmpId = await _statusApprovalService.GetCurrentEmpIdAsync();
                if (currentEmpId.HasValue)
                {
                    var pmProjectIds = _context.Projects
                        .AsNoTracking()
                        .Where(p => p.PmEmpId == currentEmpId.Value
                            || p.TeamMembers.Any(m =>
                                m.EmpId == currentEmpId.Value
                                && m.MemberRole == ProjectTeamRoles.ProjectManager))
                        .Select(p => (int?)p.ProjectId);

                    baseQuery = baseQuery.Where(r => r.ProjectId.HasValue && pmProjectIds.Contains(r.ProjectId));
                }
                else
                {
                    baseQuery = baseQuery.Where(r => false);
                }
            }

            ViewBag.SelectedStatus = selectedStatus;
            ViewBag.PendingCount = await baseQuery.CountAsync(r => r.RequestStatus == StatusApprovalService.RequestPending);

            var query = selectedStatus == "ALL"
                ? baseQuery
                : baseQuery.Where(r => r.RequestStatus == selectedStatus);

            var requests = await query
                .OrderBy(r => r.RequestStatus == StatusApprovalService.RequestPending ? 0 : 1)
                .ThenByDescending(r => r.RequestedAt)
                .ThenByDescending(r => r.RequestId)
                .ToListAsync();

            var employeeIds = requests
                .SelectMany(r => new[] { r.RequestedByEmpId, r.ReviewedByEmpId })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            ViewBag.EmployeeNames = employeeIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Employees
                    .AsNoTracking()
                    .Where(e => employeeIds.Contains(e.EmpId))
                    .ToDictionaryAsync(e => e.EmpId, e => e.EmpName ?? $"Emp #{e.EmpId}");

            var statusDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void AddDescriptions(string type, IEnumerable<(string Code, string Desc)> rows)
            {
                foreach (var row in rows)
                {
                    statusDescriptions[$"{type}:{row.Code}"] = row.Desc;
                    statusDescriptions[$"{type}:{row.Desc}"] = row.Desc;
                }
            }

            var projectStatuses = await _context.ProjectStatuses.AsNoTracking()
                .Select(x => new { x.StatusCode, x.StatusDesc }).ToListAsync();
            var phaseStatuses = await _context.ProjectPhaseStatuses.AsNoTracking()
                .Select(x => new { x.StatusCode, x.StatusDesc }).ToListAsync();
            var assignStatuses = await _context.PhaseAssignStatuses.AsNoTracking()
                .Select(x => new { x.StatusCode, x.StatusDesc }).ToListAsync();
            AddDescriptions("PROJECT", projectStatuses.Select(x => (x.StatusCode, x.StatusDesc)));
            AddDescriptions("PROJECT_PHASE", phaseStatuses.Select(x => (x.StatusCode, x.StatusDesc)));
            AddDescriptions("PHASE_ASSIGN", assignStatuses.Select(x => (x.StatusCode, x.StatusDesc)));
            ViewBag.WorkflowStatusDescriptions = statusDescriptions;

            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("StatusApprovals.Index")]
        public async Task<IActionResult> Approve(int id, string? note)
        {
            try
            {
                await _statusApprovalService.ApproveAsync(id, note);
                TempData["Success"] = "อนุมัติสถานะเสร็จสิ้นเรียบร้อยแล้ว";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("StatusApprovals.Index")]
        public async Task<IActionResult> Reject(int id, string? note)
        {
            try
            {
                await _statusApprovalService.RejectAsync(id, note);
                TempData["Success"] = "ปฏิเสธคำขออนุมัติแล้ว";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
