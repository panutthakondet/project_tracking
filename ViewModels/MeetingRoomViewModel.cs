namespace ProjectTracking.ViewModels
{
    public class MeetingRoomViewModel
    {
        public bool IsGuest { get; set; }
        public bool CanCustomize { get; set; }
        public int? FocusUserId { get; set; }
        public string ShareLink { get; set; } = "";
        public MeetingRoomPersonViewModel CurrentUser { get; set; } = new();
        public List<MeetingRoomPersonViewModel> People { get; set; } = new();
        public List<MeetingRoomZoneViewModel> Zones { get; set; } = new();
        public List<MeetingRoomObjectViewModel> Objects { get; set; } = new();
        public List<MeetingRoomTodayMeetingViewModel> TodayMeetings { get; set; } = new();
        public MeetingRoomStatsViewModel Stats { get; set; } = new();
    }

    public class MeetingRoomPersonViewModel
    {
        public int UserId { get; set; }
        public int? EmpId { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RoomDisplayName { get; set; } = "";
        public string Initial { get; set; } = "";
        public string Role { get; set; } = "";
        public string Position { get; set; } = "";
        public string AvatarPath { get; set; } = "/images/Profile/profile.png";
        public string Status { get; set; } = "AVAILABLE";
        public string StatusLabel { get; set; } = "Available";
        public string StatusText { get; set; } = "";
        public string CharacterPreset { get; set; } = "doraemon";
        public string AvatarColor { get; set; } = "#2d9cff";
        public string Zone { get; set; } = "Lobby";
        public int X { get; set; } = 50;
        public int Y { get; set; } = 50;
        public bool IsOnline { get; set; }
        public bool IsCurrentUser { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public int IssueCount { get; set; }
        public int SupportCount { get; set; }
        public int FollowupCount { get; set; }
        public int AssignCount { get; set; }
        public int WorkloadTotal => IssueCount + SupportCount + FollowupCount + AssignCount;
    }

    public class MeetingRoomZoneViewModel
    {
        public int AreaId { get; set; }
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string AreaType { get; set; } = "MEETING";
        public string Url { get; set; } = "/";
        public string Tone { get; set; } = "teal";
        public bool IsCustom { get; set; }
        public int Count { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    public class MeetingRoomTodayMeetingViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string TimeText { get; set; } = "";
        public string Location { get; set; } = "";
    }

    public class MeetingRoomObjectViewModel
    {
        public int ObjectId { get; set; }
        public string ObjectKey { get; set; } = "desk-basic";
        public string ObjectType { get; set; } = "DESK";
        public string Title { get; set; } = "Desk";
        public string Tone { get; set; } = "wood";
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public int Rotation { get; set; }
        public bool IsObstacle { get; set; } = true;
        public bool IsCustom { get; set; } = true;
    }

    public class MeetingRoomStatsViewModel
    {
        public int OnlineCount { get; set; }
        public int TotalPeople { get; set; }
        public int TodayMeetingCount { get; set; }
        public int OpenIssueCount { get; set; }
        public int OpenSupportCount { get; set; }
        public int OpenFollowupCount { get; set; }
    }
}
