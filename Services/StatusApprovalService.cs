using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Services
{
    public class StatusApprovalService
    {
        public const string TargetProject = "PROJECT";
        public const string TargetProjectPhase = "PROJECT_PHASE";
        public const string TargetPhaseAssign = "PHASE_ASSIGN";

        public const string RequestPending = "PENDING";
        public const string RequestApproved = "APPROVED";
        public const string RequestRejected = "REJECTED";

        private const string SubmittedPhaseStatus = "ส่งงวดงานแล้ว";
        private const string ApprovedPaymentPhaseStatus = "อนุมัติจ่ายเงินแล้ว";

        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public StatusApprovalService(
            AppDbContext context,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public static bool IsProjectCompletionStatus(string? status)
            => NormalizeCodeStatus(status) == "DONE";

        public static bool IsProjectPhaseCompletionStatus(string? status)
        {
            var trimmed = (status ?? "").Trim();
            return string.Equals(trimmed, SubmittedPhaseStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, ApprovedPaymentPhaseStatus, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPhaseAssignCompletionStatus(string? status)
            => NormalizeCodeStatus(status) == "DONE";

        public bool IsCurrentUserAdmin()
            => string.Equals(
                _httpContextAccessor.HttpContext?.Session.GetString("Role")?.Trim(),
                "ADMIN",
                StringComparison.OrdinalIgnoreCase);

        public int? GetCurrentUserId()
            => _httpContextAccessor.HttpContext?.Session.GetInt32("UserId");

        public async Task<int?> GetCurrentEmpIdAsync()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            var sessionEmpId = session?.GetInt32("EmpId");
            if (sessionEmpId.HasValue) return sessionEmpId;

            var userId = GetCurrentUserId();
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

        public async Task<bool> CanApplyCompletionStatusImmediatelyAsync(int? projectId)
            => await CanReviewProjectAsync(projectId);

        public async Task<bool> CanReviewAsync(StatusApprovalRequest request)
            => await CanReviewProjectAsync(ResolveProjectId(request));

        public async Task<StatusApprovalRequest> QueueCompletionRequestAsync(
            string targetType,
            int targetId,
            int? projectId,
            string? projectName,
            string? targetTitle,
            string? currentStatus,
            string requestedStatus,
            string? requestNote = null)
        {
            var now = DateTime.Now;
            var requestedByUserId = GetCurrentUserId();
            var requestedByEmpId = await GetCurrentEmpIdAsync();

            var existing = await _context.StatusApprovalRequests
                .FirstOrDefaultAsync(x =>
                    x.TargetType == targetType
                    && x.TargetId == targetId
                    && x.RequestStatus == RequestPending);

            if (existing != null)
            {
                existing.ProjectId = projectId;
                existing.ProjectName = projectName;
                existing.TargetTitle = targetTitle;
                existing.CurrentStatus = currentStatus;
                existing.RequestedStatus = requestedStatus;
                existing.RequestNote = requestNote;
                existing.RequestedByUserId = requestedByUserId;
                existing.RequestedByEmpId = requestedByEmpId;
                existing.RequestedAt = now;
                existing.UpdatedAt = now;
                return existing;
            }

            var request = new StatusApprovalRequest
            {
                TargetType = targetType,
                TargetId = targetId,
                ProjectId = projectId,
                ProjectName = projectName,
                TargetTitle = targetTitle,
                CurrentStatus = currentStatus,
                RequestedStatus = requestedStatus,
                RequestStatus = RequestPending,
                RequestNote = requestNote,
                RequestedByUserId = requestedByUserId,
                RequestedByEmpId = requestedByEmpId,
                RequestedAt = now,
                UpdatedAt = now
            };

            _context.StatusApprovalRequests.Add(request);
            return request;
        }

        public async Task<StatusApprovalRequest> ApproveAsync(int requestId, string? note)
        {
            var request = await GetPendingRequestAsync(requestId);
            if (!await CanReviewAsync(request))
                throw new InvalidOperationException("คุณไม่มีสิทธิอนุมัติรายการนี้");

            var reviewerUserId = GetCurrentUserId();
            var reviewerEmpId = await GetCurrentEmpIdAsync();
            var now = DateTime.Now;

            await ApplyApprovedStatusAsync(request, reviewerEmpId, now);

            request.RequestStatus = RequestApproved;
            request.ReviewNote = CleanNote(note);
            request.ReviewedByUserId = reviewerUserId;
            request.ReviewedByEmpId = reviewerEmpId;
            request.ReviewedAt = now;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return request;
        }

        public async Task<StatusApprovalRequest> RejectAsync(int requestId, string? note)
        {
            var request = await GetPendingRequestAsync(requestId);
            if (!await CanReviewAsync(request))
                throw new InvalidOperationException("คุณไม่มีสิทธิปฏิเสธรายการนี้");

            var now = DateTime.Now;
            request.RequestStatus = RequestRejected;
            request.ReviewNote = CleanNote(note);
            request.ReviewedByUserId = GetCurrentUserId();
            request.ReviewedByEmpId = await GetCurrentEmpIdAsync();
            request.ReviewedAt = now;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return request;
        }

        private async Task<bool> CanReviewProjectAsync(int? projectId)
        {
            if (IsCurrentUserAdmin()) return true;
            if (!projectId.HasValue) return false;

            var currentEmpId = await GetCurrentEmpIdAsync();
            if (!currentEmpId.HasValue) return false;

            var trackedProject = _context.Projects.Local
                .FirstOrDefault(p => p.ProjectId == projectId.Value);
            if (trackedProject?.PmEmpId == currentEmpId.Value)
                return true;

            if (_context.ProjectTeamMembers.Local.Any(m =>
                    m.ProjectId == projectId.Value
                    && m.EmpId == currentEmpId.Value
                    && m.MemberRole == ProjectTeamRoles.ProjectManager
                    && _context.Entry(m).State != EntityState.Deleted))
            {
                return true;
            }

            return await _context.Projects
                .AsNoTracking()
                .AnyAsync(p => p.ProjectId == projectId.Value
                    && (p.PmEmpId == currentEmpId.Value
                        || p.TeamMembers.Any(m =>
                            m.EmpId == currentEmpId.Value
                            && m.MemberRole == ProjectTeamRoles.ProjectManager)));
        }

        private async Task<StatusApprovalRequest> GetPendingRequestAsync(int requestId)
        {
            var request = await _context.StatusApprovalRequests
                .FirstOrDefaultAsync(x => x.RequestId == requestId);

            if (request == null)
                throw new InvalidOperationException("ไม่พบคำขออนุมัติ");

            if (!string.Equals(request.RequestStatus, RequestPending, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("รายการนี้ถูกดำเนินการแล้ว");

            return request;
        }

        private async Task ApplyApprovedStatusAsync(StatusApprovalRequest request, int? reviewerEmpId, DateTime now)
        {
            if (string.Equals(request.TargetType, TargetProject, StringComparison.OrdinalIgnoreCase))
            {
                var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectId == request.TargetId);
                if (project == null)
                    throw new InvalidOperationException("ไม่พบโครงการที่ต้องอนุมัติ");

                project.Status = request.RequestedStatus;
                project.CreatedAt = now;
                project.EntryId = reviewerEmpId;
                await SyncRequirementCardColumnForProjectStatusAsync(project.RequirementCardId, project.Status);
                return;
            }

            if (string.Equals(request.TargetType, TargetProjectPhase, StringComparison.OrdinalIgnoreCase))
            {
                var phase = await _context.ProjectPhases.FirstOrDefaultAsync(p => p.PhaseId == request.TargetId);
                if (phase == null)
                    throw new InvalidOperationException("ไม่พบงวดงานที่ต้องอนุมัติ");

                phase.PhaseStatus = NormalizePhaseStatus(request.RequestedStatus);
                phase.CreatedAt = now;
                phase.EntryId = reviewerEmpId;
                return;
            }

            if (string.Equals(request.TargetType, TargetPhaseAssign, StringComparison.OrdinalIgnoreCase))
            {
                var assign = await _context.PhaseAssigns.FirstOrDefaultAsync(a => a.AssignId == request.TargetId);
                if (assign == null)
                    throw new InvalidOperationException("ไม่พบรายการมอบหมายที่ต้องอนุมัติ");

                assign.WorkStatus = request.RequestedStatus;
                assign.ActualStart = assign.PlanStart?.Date;
                assign.ActualEnd = IsPhaseAssignCompletionStatus(assign.WorkStatus)
                    ? assign.ActualEnd ?? now.Date
                    : null;
                assign.CreatedAt = now;
                assign.EntryId = reviewerEmpId;
                return;
            }

            throw new InvalidOperationException("ประเภทคำขออนุมัติไม่ถูกต้อง");
        }

        private int? ResolveProjectId(StatusApprovalRequest request)
        {
            if (request.ProjectId.HasValue) return request.ProjectId;

            return string.Equals(request.TargetType, TargetProject, StringComparison.OrdinalIgnoreCase)
                ? request.TargetId
                : null;
        }

        private async Task SyncRequirementCardColumnForProjectStatusAsync(int? requirementCardId, string? projectStatus)
        {
            if (!requirementCardId.HasValue) return;

            var card = await _context.RequirementCards
                .FirstOrDefaultAsync(c => c.CardId == requirementCardId.Value && !c.IsArchived);
            if (card == null) return;

            var sourceColumn = await _context.RequirementBoardColumns
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ColumnId == card.ColumnId);
            if (sourceColumn == null) return;

            var targetColumn = await FindRequirementBoardColumnForProjectStatusAsync(projectStatus, sourceColumn.BoardId);
            if (targetColumn == null || targetColumn.ColumnId == card.ColumnId) return;

            var cardsInTargetColumn = await _context.RequirementCards
                .Where(c => c.ColumnId == targetColumn.ColumnId && !c.IsArchived)
                .ToListAsync();

            foreach (var item in cardsInTargetColumn)
            {
                item.SortOrder += 1;
            }

            card.ColumnId = targetColumn.ColumnId;
            card.SortOrder = 1;
            card.UpdatedAt = DateTime.Now;
        }

        private async Task<RequirementBoardColumn?> FindRequirementBoardColumnForProjectStatusAsync(string? projectStatus, int boardId)
        {
            var candidates = NormalizeCodeStatus(projectStatus) switch
            {
                "DONE" => new[] { "Completed/Guaranteed", "Completed", "Complete", "Done", "DONE", "เสร็จสิ้น" },
                "IN_PROGRESS" => new[] { "In Progress", "IN_PROGRESS", "Doing", "กำลังดำเนินการ" },
                "PLAN" => new[] { "Pending", "To Do", "PLAN", "วางแผน" },
                _ => Array.Empty<string>()
            };

            if (candidates.Length == 0) return null;

            var columns = await _context.RequirementBoardColumns
                .Where(c => c.BoardId == boardId && c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.ColumnId)
                .ToListAsync();

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeBoardColumnName(candidate);
                var exactMatch = columns.FirstOrDefault(c =>
                    NormalizeBoardColumnName(c.ColumnName) == normalizedCandidate);
                if (exactMatch != null) return exactMatch;
            }

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeBoardColumnName(candidate);
                var containsMatch = columns.FirstOrDefault(c =>
                    NormalizeBoardColumnName(c.ColumnName).Contains(normalizedCandidate));
                if (containsMatch != null) return containsMatch;
            }

            return null;
        }

        private static string NormalizeCodeStatus(string? status)
        {
            return (status ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "_")
                .Replace("-", "_");
        }

        private static string NormalizeBoardColumnName(string? value)
        {
            return (value ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace("/", "");
        }

        private static string NormalizePhaseStatus(string? status)
        {
            return string.Equals((status ?? "").Trim(), ApprovedPaymentPhaseStatus, StringComparison.OrdinalIgnoreCase)
                ? SubmittedPhaseStatus
                : (status ?? "").Trim();
        }

        private static string? CleanNote(string? note)
        {
            var cleaned = (note ?? "").Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }
    }
}
