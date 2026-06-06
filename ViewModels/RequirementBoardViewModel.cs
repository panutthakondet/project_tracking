using ProjectTracking.Models;

namespace ProjectTracking.ViewModels
{
    public class RequirementBoardViewModel
    {
        public List<RequirementBoardColumn> Columns { get; set; } = new();
        public List<RequirementBoardOnlineUserViewModel> OnlineUsers { get; set; } = new();
        public int TotalCards { get; set; }
        public int TotalAttachments { get; set; }
    }

    public class RequirementBoardOnlineUserViewModel
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = "";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public string ColorClass { get; set; } = "c1";
        public DateTime? LastSeenAt { get; set; }
    }

    public class MoveRequirementCardRequest
    {
        public int CardId { get; set; }
        public int ColumnId { get; set; }
        public List<int> OrderedCardIds { get; set; } = new();
    }

    public class RequirementCardPhaseItemInput
    {
        public int? ItemId { get; set; }
        public string? PhaseName { get; set; }
        public string? PhaseType { get; set; }
        public int PhaseOrder { get; set; } = 1;
        public int PeriodOrder { get; set; } = 1;
        public string? PhaseStatus { get; set; }
        public string? PlanStart { get; set; }
        public string? PlanEnd { get; set; }
        public string? PeriodStartDate { get; set; }
        public string? PeriodEndDate { get; set; }
    }
}
