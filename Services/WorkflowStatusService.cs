using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

namespace ProjectTracking.Services
{
    /// <summary>
    /// Reads the three independent status master tables while keeping the
    /// existing string status columns compatible during the transition.
    /// </summary>
    public sealed class WorkflowStatusService
    {
        private readonly AppDbContext _context;

        public WorkflowStatusService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<StatusDefinitionOption>> GetActiveAsync(
            string statusType,
            CancellationToken cancellationToken = default)
        {
            return statusType switch
            {
                WorkflowStatusTypes.Project => await _context.ProjectStatuses
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                    .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                    .ToListAsync(cancellationToken),
                WorkflowStatusTypes.ProjectPhase => await _context.ProjectPhaseStatuses
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                    .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                    .ToListAsync(cancellationToken),
                WorkflowStatusTypes.PhaseAssign => await _context.PhaseAssignStatuses
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                    .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                    .ToListAsync(cancellationToken),
                _ => new List<StatusDefinitionOption>()
            };
        }

        public async Task<StatusDefinitionOption?> FindAsync(
            string statusType,
            int? statusId,
            CancellationToken cancellationToken = default)
        {
            if (!statusId.HasValue || statusId.Value <= 0) return null;

            return statusType switch
            {
                WorkflowStatusTypes.Project => await _context.ProjectStatuses
                    .AsNoTracking()
                    .Where(x => x.StatusId == statusId.Value && x.IsActive)
                    .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                    .FirstOrDefaultAsync(cancellationToken),
                WorkflowStatusTypes.ProjectPhase => await _context.ProjectPhaseStatuses
                    .AsNoTracking()
                    .Where(x => x.StatusId == statusId.Value && x.IsActive)
                    .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                    .FirstOrDefaultAsync(cancellationToken),
                WorkflowStatusTypes.PhaseAssign => await _context.PhaseAssignStatuses
                    .AsNoTracking()
                    .Where(x => x.StatusId == statusId.Value && x.IsActive)
                    .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                    .FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        public async Task<int?> ResolveIdAsync(
            string statusType,
            string? value,
            CancellationToken cancellationToken = default)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return null;

            return statusType switch
            {
                WorkflowStatusTypes.Project => await _context.ProjectStatuses
                    .AsNoTracking()
                    .Where(x => x.StatusCode == normalized || x.StatusDesc == normalized)
                    .Select(x => (int?)x.StatusId)
                    .FirstOrDefaultAsync(cancellationToken),
                WorkflowStatusTypes.ProjectPhase => await _context.ProjectPhaseStatuses
                    .AsNoTracking()
                    .Where(x => x.StatusCode == normalized || x.StatusDesc == normalized)
                    .Select(x => (int?)x.StatusId)
                    .FirstOrDefaultAsync(cancellationToken),
                WorkflowStatusTypes.PhaseAssign => await _context.PhaseAssignStatuses
                    .AsNoTracking()
                    .Where(x => x.StatusCode == normalized || x.StatusDesc == normalized)
                    .Select(x => (int?)x.StatusId)
                    .FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        public async Task<(int? StatusId, string LegacyValue)> ResolveSelectionAsync(
            string statusType,
            int? statusId,
            string? fallbackValue,
            CancellationToken cancellationToken = default)
        {
            var selected = await FindAsync(statusType, statusId, cancellationToken);
            if (selected != null)
            {
                var compatibilityValue = statusType == WorkflowStatusTypes.ProjectPhase
                    ? selected.StatusDesc
                    : selected.StatusCode;
                return (selected.StatusId, compatibilityValue);
            }

            var fallback = (fallbackValue ?? string.Empty).Trim();
            var fallbackId = await ResolveIdAsync(statusType, fallback, cancellationToken);
            return (fallbackId, fallback);
        }

    }
}
