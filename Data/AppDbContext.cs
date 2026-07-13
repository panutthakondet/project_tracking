using Microsoft.EntityFrameworkCore;
using ProjectTracking.Models;

namespace ProjectTracking.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // ======================
        // ===== TABLES =====
        // ======================
        public DbSet<Employee> Employees { get; set; }
        public DbSet<CntMCoop> CntMCoops { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectDocument> ProjectDocuments { get; set; }
        public DbSet<ProjectPhase> ProjectPhases { get; set; }
        public DbSet<PhaseAssign> PhaseAssigns { get; set; }
        public DbSet<LoginUser> LoginUsers { get; set; }
        public DbSet<UserMenu> UserMenus { get; set; }
        public DbSet<ThemePreset> ThemePresets { get; set; }
        public DbSet<UserThemePreference> UserThemePreferences { get; set; }

        // ===== Meetings =====
        public DbSet<Meeting> Meetings { get; set; }
        public DbSet<MeetingAttendee> MeetingAttendees { get; set; }
        public DbSet<MeetingEmailNotification> MeetingEmailNotifications { get; set; }

        // ===== Issues =====
        public DbSet<ProjectIssue> ProjectIssues { get; set; }
        public DbSet<ProjectIssueImage> ProjectIssueImages { get; set; }
        public DbSet<ProjectIssueFixImage> ProjectIssueFixImages { get; set; }
        public DbSet<ProjectIssueGitHistory> ProjectIssueGitHistories { get; set; }

        // ===== Support Orders (Warranty / Maintenance) =====
        public DbSet<ProjectSupportOrder> ProjectSupportOrders { get; set; }
        public DbSet<ProjectSupportImage> ProjectSupportImages { get; set; }
        public DbSet<ProjectSupportFixImage> ProjectSupportFixImages { get; set; }
        public DbSet<ProjectSupportOrderStatusHistory> ProjectSupportOrderStatusHistories { get; set; }
        public DbSet<ProjectSupportOrderGitHistory> ProjectSupportOrderGitHistories { get; set; }

        // ===== Test Scenarios =====
        public DbSet<TestScenario> TestScenarios { get; set; }
        public DbSet<TestScenarioAttachment> TestScenarioAttachments { get; set; }

        // ===== Test Scenario Templates =====
        public DbSet<TestScenarioTemplate> TestScenarioTemplates { get; set; }
        public DbSet<TestTemplateGroup> TestTemplateGroups { get; set; }

        // ✅ Issue Status History (สำหรับ Yesterday snapshot)
        public DbSet<ProjectIssueStatusHistory> ProjectIssueStatusHistories { get; set; }

        // ===== Email =====
        public DbSet<EmailSendLog> EmailSendLogs { get; set; }

        // ===== Follow-up Tracking =====
        public DbSet<ProjectFollowup> ProjectFollowups { get; set; }
        public DbSet<ProjectFollowupLog> ProjectFollowupLogs { get; set; }
        public DbSet<PhaseAssignLog> PhaseAssignLogs { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<SystemConfig> SystemConfigs { get; set; }
        public DbSet<SystemUpdateAnnouncement> SystemUpdateAnnouncements { get; set; }
        public DbSet<SystemUpdateRead> SystemUpdateReads { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }
        public DbSet<MeetingRoomProfile> MeetingRoomProfiles { get; set; }
        public DbSet<MeetingRoomArea> MeetingRoomAreas { get; set; }
        public DbSet<MeetingRoomObject> MeetingRoomObjects { get; set; }
        public DbSet<MeetingRoomFileShare> MeetingRoomFileShares { get; set; }
        public DbSet<NotificationSendLog> NotificationSendLogs { get; set; }
        public DbSet<StatusApprovalRequest> StatusApprovalRequests { get; set; }
        public DbSet<LineRecipient> LineRecipients { get; set; }
        public DbSet<TelegramRecipient> TelegramRecipients { get; set; }
        public DbSet<WeeklyReport> WeeklyReports { get; set; }
        public DbSet<WeeklyReportAttachment> WeeklyReportAttachments { get; set; }
        public DbSet<RequirementBoardGroup> RequirementBoardGroups { get; set; }
        public DbSet<RequirementBoard> RequirementBoards { get; set; }
        public DbSet<RequirementBoardColumn> RequirementBoardColumns { get; set; }
        public DbSet<RequirementCard> RequirementCards { get; set; }
        public DbSet<RequirementCardAttachment> RequirementCardAttachments { get; set; }
        public DbSet<RequirementCardPhaseItem> RequirementCardPhaseItems { get; set; }
        public DbSet<RequirementBoardLabel> RequirementBoardLabels { get; set; }
        public DbSet<RequirementCardLabel> RequirementCardLabels { get; set; }

        // ======================
        // ===== VIEWS =====
        // ======================
        public DbSet<VwPhaseOwnerStatus> VwPhaseOwnerStatuses { get; set; }

        // ======================
        // ===== MODEL CONFIG =====
        // ======================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================
            // MEETINGS
            // =========================
            modelBuilder.Entity<Meeting>(entity =>
            {
                entity.ToTable("meetings");
                entity.HasKey(m => m.Id);

                // MySQL DATE/TIME mappings
                entity.Property(m => m.MeetingDate).HasColumnType("date");
                entity.Property(m => m.StartTime).HasColumnType("time");
                entity.Property(m => m.EndTime).HasColumnType("time");

                entity.Property(m => m.Title)
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(m => m.Location)
                    .HasColumnType("varchar(255)")
                    .IsRequired(false);

                entity.Property(m => m.MeetingAudience)
                    .HasColumnName("meeting_audience")
                    .HasColumnType("varchar(50)")
                    .IsRequired(false);

                entity.Property(m => m.Status)
                    .HasColumnName("status")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("ACTIVE")
                    .IsRequired();

                entity.Property(m => m.ProjectId)
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(m => m.CreatedBy)
                    .HasColumnName("created_by")
                    .HasColumnType("int")
                    .IsRequired(false);

                // created_at may be managed by DB default
                entity.Property(m => m.CreatedAt)
                    .HasColumnType("timestamp");

                entity.Property(m => m.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.HasMany(m => m.Attendees)
                    .WithOne(a => a.Meeting!)
                    .HasForeignKey(a => a.MeetingId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Link meeting -> project (project.project_id)
                entity.HasOne(m => m.Project)
                    .WithMany()
                    .HasForeignKey(m => m.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(m => m.ProjectId);

                entity.HasIndex(m => m.MeetingDate);
            });

            modelBuilder.Entity<MeetingAttendee>(entity =>
            {
                entity.ToTable("meeting_attendees");
                entity.HasKey(a => a.Id);

                entity.Property(a => a.Status)
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("pending")
                    .IsRequired();

                entity.HasIndex(a => a.MeetingId);
                entity.HasIndex(a => a.UserId);
                entity.HasIndex(a => new { a.MeetingId, a.UserId });
            });

            modelBuilder.Entity<MeetingEmailNotification>(entity =>
            {
                entity.ToTable("meeting_email_notifications");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.MeetingId)
                    .HasColumnName("meeting_id")
                    .HasColumnType("int")
                    .IsRequired();

                entity.Property(x => x.AttendeeId)
                    .HasColumnName("attendee_id")
                    .HasColumnType("int")
                    .IsRequired();

                entity.Property(x => x.Kind)
                    .HasColumnName("kind")
                    .HasColumnType("varchar(50)")
                    .IsRequired();

                entity.Property(x => x.SentAt)
                    .HasColumnName("sent_at")
                    .HasColumnType("datetime")
                    .IsRequired();

                entity.HasIndex(x => new { x.MeetingId, x.AttendeeId, x.Kind })
                    .IsUnique()
                    .HasDatabaseName("uq_meeting_attendee_kind");

                entity.HasIndex(x => x.MeetingId)
                    .HasDatabaseName("idx_meeting");
            });

            modelBuilder.Entity<NotificationSendLog>(entity =>
            {
                entity.ToTable("notification_send_logs");
                entity.HasKey(x => x.LogId);

                entity.Property(x => x.LogId)
                    .HasColumnName("log_id");

                entity.Property(x => x.Channel)
                    .HasColumnName("channel")
                    .HasColumnType("varchar(20)")
                    .IsRequired();

                entity.Property(x => x.RecipientEmpId)
                    .HasColumnName("recipient_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.RecipientAddress)
                    .HasColumnName("recipient_address")
                    .HasColumnType("varchar(255)")
                    .IsRequired(false);

                entity.Property(x => x.Title)
                    .HasColumnName("title")
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(x => x.Message)
                    .HasColumnName("message")
                    .HasColumnType("text")
                    .IsRequired(false);

                entity.Property(x => x.TargetUrl)
                    .HasColumnName("target_url")
                    .HasColumnType("varchar(500)")
                    .IsRequired(false);

                entity.Property(x => x.SentAt)
                    .HasColumnName("sent_at")
                    .HasColumnType("datetime")
                    .IsRequired();

                entity.HasIndex(x => new { x.Channel, x.SentAt })
                    .HasDatabaseName("idx_notification_send_logs_channel_sent");

                entity.HasIndex(x => x.RecipientEmpId)
                    .HasDatabaseName("idx_notification_send_logs_emp");
            });

            // =========================
            // LOGIN USER
            // =========================
            modelBuilder.Entity<LoginUser>(entity =>
            {
                entity.HasKey(u => u.UserId);

                entity.Property(u => u.ProfileImagePath)
                    .HasColumnName("profile_image_path")
                    .HasColumnType("varchar(500)")
                    .IsRequired(false);

                entity.Property(u => u.LastSeenAt)
                    .HasColumnName("last_seen_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);
            });

            // =========================
            // USER MENU PERMISSIONS
            // =========================
            modelBuilder.Entity<UserMenu>(entity =>
            {
                entity.ToTable("UserMenus");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Username)
                    .HasColumnName("Username")
                    .HasColumnType("varchar(50)")
                    .IsRequired();

                entity.Property(x => x.MenuKey)
                    .HasColumnName("MenuKey")
                    .HasColumnType("varchar(100)")
                    .IsRequired();

                entity.HasIndex(x => new { x.Username, x.MenuKey }).IsUnique();
            });

            // =========================
            // USER THEME PREFERENCES
            // =========================
            modelBuilder.Entity<ThemePreset>(entity =>
            {
                entity.ToTable("theme_presets");
                entity.HasKey(x => x.ThemeId);

                entity.Property(x => x.ThemeId).HasColumnName("theme_id");
                entity.Property(x => x.ThemeKey).HasColumnName("theme_key").HasColumnType("varchar(80)").IsRequired();
                entity.Property(x => x.ThemeName).HasColumnName("theme_name").HasColumnType("varchar(120)").IsRequired();
                entity.Property(x => x.IsSystem).HasColumnName("is_system").HasColumnType("tinyint(1)");
                entity.Property(x => x.IsDefault).HasColumnName("is_default").HasColumnType("tinyint(1)");
                entity.Property(x => x.IsActive).HasColumnName("is_active").HasColumnType("tinyint(1)");
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.AccentHex).HasColumnName("accent_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.AccentDarkHex).HasColumnName("accent_dark_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.AccentDeepHex).HasColumnName("accent_deep_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.SidebarHex).HasColumnName("sidebar_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.SidebarDeepHex).HasColumnName("sidebar_deep_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.BodyBgHex).HasColumnName("body_bg_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.ChartPanelHex).HasColumnName("chart_panel_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.SurfaceHex).HasColumnName("surface_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.TextHex).HasColumnName("text_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.MutedHex).HasColumnName("muted_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.ContrastHex).HasColumnName("contrast_hex").HasColumnType("varchar(7)").IsRequired();
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasIndex(x => x.ThemeKey).IsUnique().HasDatabaseName("uq_theme_presets_key");
                entity.HasIndex(x => new { x.IsActive, x.SortOrder }).HasDatabaseName("idx_theme_presets_active_sort");
            });

            modelBuilder.Entity<UserThemePreference>(entity =>
            {
                entity.ToTable("user_theme_preferences");
                entity.HasKey(x => x.UserId);

                entity.Property(x => x.UserId).HasColumnName("user_id");
                entity.Property(x => x.ThemeId).HasColumnName("theme_id");
                entity.Property(x => x.UseCustom).HasColumnName("use_custom").HasColumnType("tinyint(1)");
                entity.Property(x => x.CustomAccentHex).HasColumnName("custom_accent_hex").HasColumnType("varchar(7)");
                entity.Property(x => x.CustomSidebarHex).HasColumnName("custom_sidebar_hex").HasColumnType("varchar(7)");
                entity.Property(x => x.CustomBodyBgHex).HasColumnName("custom_body_bg_hex").HasColumnType("varchar(7)");
                entity.Property(x => x.CustomChartPanelHex).HasColumnName("custom_chart_panel_hex").HasColumnType("varchar(7)");
                entity.Property(x => x.FontScale).HasColumnName("font_scale").HasColumnType("decimal(4,2)");
                entity.Property(x => x.ProfileBallEnabled).HasColumnName("profile_ball_enabled").HasColumnType("tinyint(1)").HasDefaultValue(false);
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasOne(x => x.ThemePreset)
                    .WithMany(x => x.UserPreferences)
                    .HasForeignKey(x => x.ThemeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // SYSTEM UPDATE ANNOUNCEMENTS
            // =========================
            modelBuilder.Entity<SystemUpdateAnnouncement>(entity =>
            {
                entity.ToTable("system_update_announcements");
                entity.HasKey(x => x.UpdateId);

                entity.Property(x => x.UpdateId)
                    .HasColumnName("update_id");

                entity.Property(x => x.Version)
                    .HasColumnName("version")
                    .HasColumnType("varchar(50)")
                    .IsRequired(false);

                entity.Property(x => x.Title)
                    .HasColumnName("title")
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(x => x.Summary)
                    .HasColumnName("summary")
                    .HasColumnType("varchar(500)")
                    .IsRequired(false);

                entity.Property(x => x.Details)
                    .HasColumnName("details")
                    .HasColumnType("text")
                    .IsRequired(false);

                entity.Property(x => x.PublishedAt)
                    .HasColumnName("published_at")
                    .HasColumnType("datetime");

                entity.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true);

                entity.HasIndex(x => new { x.IsActive, x.PublishedAt });
            });

            modelBuilder.Entity<SystemUpdateRead>(entity =>
            {
                entity.ToTable("system_update_reads");
                entity.HasKey(x => x.ReadId);

                entity.Property(x => x.ReadId)
                    .HasColumnName("read_id");

                entity.Property(x => x.UpdateId)
                    .HasColumnName("update_id")
                    .IsRequired();

                entity.Property(x => x.UserId)
                    .HasColumnName("user_id")
                    .IsRequired();

                entity.Property(x => x.ReadAt)
                    .HasColumnName("read_at")
                    .HasColumnType("datetime");

                entity.HasIndex(x => new { x.UpdateId, x.UserId })
                    .IsUnique()
                    .HasDatabaseName("uq_system_update_reads_update_user");

                entity.HasIndex(x => x.UserId);

                entity.HasOne(x => x.Update)
                    .WithMany(x => x.Reads)
                    .HasForeignKey(x => x.UpdateId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // USER NOTIFICATIONS
            // =========================
            modelBuilder.Entity<UserNotification>(entity =>
            {
                entity.ToTable("user_notifications");
                entity.HasKey(x => x.NotificationId);

                entity.Property(x => x.NotificationId)
                    .HasColumnName("notification_id");

                entity.Property(x => x.RecipientUserId)
                    .HasColumnName("recipient_user_id")
                    .IsRequired(false);

                entity.Property(x => x.RecipientEmpId)
                    .HasColumnName("recipient_emp_id")
                    .IsRequired(false);

                entity.Property(x => x.SourceType)
                    .HasColumnName("source_type")
                    .HasColumnType("varchar(50)")
                    .IsRequired();

                entity.Property(x => x.SourceId)
                    .HasColumnName("source_id")
                    .IsRequired();

                entity.Property(x => x.Title)
                    .HasColumnName("title")
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(x => x.Message)
                    .HasColumnName("message")
                    .HasColumnType("text")
                    .IsRequired(false);

                entity.Property(x => x.TargetUrl)
                    .HasColumnName("target_url")
                    .HasColumnType("varchar(500)")
                    .IsRequired(false);

                entity.Property(x => x.Severity)
                    .HasColumnName("severity")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("WARNING")
                    .IsRequired();

                entity.Property(x => x.IsRead)
                    .HasColumnName("is_read")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(x => x.ReadAt)
                    .HasColumnName("read_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(x => x.IsResolved)
                    .HasColumnName("is_resolved")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(x => x.ResolvedAt)
                    .HasColumnName("resolved_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(x => new { x.SourceType, x.SourceId, x.RecipientEmpId })
                    .IsUnique()
                    .HasDatabaseName("uq_user_notifications_source_emp");

                entity.HasIndex(x => new { x.RecipientUserId, x.IsRead, x.IsResolved, x.CreatedAt })
                    .HasDatabaseName("idx_user_notifications_recipient");

                entity.HasIndex(x => x.RecipientEmpId)
                    .HasDatabaseName("idx_user_notifications_emp");

                entity.HasOne(x => x.RecipientUser)
                    .WithMany()
                    .HasForeignKey(x => x.RecipientUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(x => x.RecipientEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.RecipientEmpId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =========================
            // SO-AT MEETING ROOM
            // =========================
            modelBuilder.Entity<MeetingRoomProfile>(entity =>
            {
                entity.ToTable("meeting_room_profiles");
                entity.HasKey(x => x.UserId);

                entity.Property(x => x.UserId)
                    .HasColumnName("user_id");

                entity.Property(x => x.Status)
                    .HasColumnName("status")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("AVAILABLE")
                    .IsRequired();

                entity.Property(x => x.DisplayName)
                    .HasColumnName("display_name")
                    .HasColumnType("varchar(50)")
                    .IsRequired(false);

                entity.Property(x => x.StatusText)
                    .HasColumnName("status_text")
                    .HasColumnType("varchar(120)")
                    .IsRequired(false);

                entity.Property(x => x.CharacterPreset)
                    .HasColumnName("character_preset")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("human")
                    .IsRequired();

                entity.Property(x => x.AvatarColor)
                    .HasColumnName("avatar_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#3b82f6")
                    .IsRequired();

                entity.Property(x => x.SkinTone)
                    .HasColumnName("skin_tone")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#f2c19b")
                    .IsRequired();

                entity.Property(x => x.HairStyle)
                    .HasColumnName("hair_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("short")
                    .IsRequired();

                entity.Property(x => x.HairColor)
                    .HasColumnName("hair_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#2f3137")
                    .IsRequired();

                entity.Property(x => x.FacialHairStyle)
                    .HasColumnName("facial_hair_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("none")
                    .IsRequired();

                entity.Property(x => x.TopStyle)
                    .HasColumnName("top_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("shirt")
                    .IsRequired();

                entity.Property(x => x.TopColor)
                    .HasColumnName("top_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#3b82f6")
                    .IsRequired();

                entity.Property(x => x.JacketStyle)
                    .HasColumnName("jacket_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("none")
                    .IsRequired();

                entity.Property(x => x.JacketColor)
                    .HasColumnName("jacket_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#111827")
                    .IsRequired();

                entity.Property(x => x.BottomStyle)
                    .HasColumnName("bottom_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("pants")
                    .IsRequired();

                entity.Property(x => x.BottomColor)
                    .HasColumnName("bottom_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#1f2937")
                    .IsRequired();

                entity.Property(x => x.ShoesStyle)
                    .HasColumnName("shoes_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("sneakers")
                    .IsRequired();

                entity.Property(x => x.ShoesColor)
                    .HasColumnName("shoes_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#e5e7eb")
                    .IsRequired();

                entity.Property(x => x.HatStyle)
                    .HasColumnName("hat_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("none")
                    .IsRequired();

                entity.Property(x => x.HatColor)
                    .HasColumnName("hat_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#3b82f6")
                    .IsRequired();

                entity.Property(x => x.GlassesStyle)
                    .HasColumnName("glasses_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("none")
                    .IsRequired();

                entity.Property(x => x.GlassesColor)
                    .HasColumnName("glasses_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#111827")
                    .IsRequired();

                entity.Property(x => x.OtherStyle)
                    .HasColumnName("other_style")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("none")
                    .IsRequired();

                entity.Property(x => x.OtherColor)
                    .HasColumnName("other_color")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("#ef4444")
                    .IsRequired();

                entity.Property(x => x.DeskX)
                    .HasColumnName("desk_x")
                    .HasDefaultValue(50)
                    .IsRequired();

                entity.Property(x => x.DeskY)
                    .HasColumnName("desk_y")
                    .HasDefaultValue(50)
                    .IsRequired();

                entity.Property(x => x.CurrentX)
                    .HasColumnName("current_x")
                    .IsRequired(false);

                entity.Property(x => x.CurrentY)
                    .HasColumnName("current_y")
                    .IsRequired(false);

                entity.Property(x => x.HomeZone)
                    .HasColumnName("home_zone")
                    .HasColumnType("varchar(80)")
                    .HasDefaultValue("Lobby")
                    .IsRequired();

                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(x => x.Status)
                    .HasDatabaseName("idx_meeting_room_profiles_status");

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MeetingRoomArea>(entity =>
            {
                entity.ToTable("meeting_room_areas");
                entity.HasKey(x => x.AreaId);

                entity.Property(x => x.AreaId)
                    .HasColumnName("area_id");

                entity.Property(x => x.AreaKey)
                    .HasColumnName("area_key")
                    .HasColumnType("varchar(80)")
                    .IsRequired();

                entity.Property(x => x.Title)
                    .HasColumnName("title")
                    .HasColumnType("varchar(100)")
                    .IsRequired();

                entity.Property(x => x.AreaType)
                    .HasColumnName("area_type")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("MEETING")
                    .IsRequired();

                entity.Property(x => x.Tone)
                    .HasColumnName("tone")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("teal")
                    .IsRequired();

                entity.Property(x => x.X)
                    .HasColumnName("x")
                    .HasDefaultValue(10)
                    .IsRequired();

                entity.Property(x => x.Y)
                    .HasColumnName("y")
                    .HasDefaultValue(10)
                    .IsRequired();

                entity.Property(x => x.W)
                    .HasColumnName("w")
                    .HasDefaultValue(20)
                    .IsRequired();

                entity.Property(x => x.H)
                    .HasColumnName("h")
                    .HasDefaultValue(15)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true)
                    .IsRequired();

                entity.Property(x => x.SortOrder)
                    .HasColumnName("sort_order")
                    .HasDefaultValue(0)
                    .IsRequired();

                entity.Property(x => x.CreatedByUserId)
                    .HasColumnName("created_by_user_id")
                    .IsRequired(false);

                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .IsRequired();

                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(x => x.AreaKey)
                    .IsUnique()
                    .HasDatabaseName("uq_meeting_room_areas_key");

                entity.HasIndex(x => x.IsActive)
                    .HasDatabaseName("idx_meeting_room_areas_active");
            });

            modelBuilder.Entity<MeetingRoomObject>(entity =>
            {
                entity.ToTable("meeting_room_objects");
                entity.HasKey(x => x.ObjectId);

                entity.Property(x => x.ObjectId)
                    .HasColumnName("object_id");

                entity.Property(x => x.ObjectKey)
                    .HasColumnName("object_key")
                    .HasColumnType("varchar(80)")
                    .HasDefaultValue("desk-basic")
                    .IsRequired();

                entity.Property(x => x.ObjectType)
                    .HasColumnName("object_type")
                    .HasColumnType("varchar(30)")
                    .HasDefaultValue("DESK")
                    .IsRequired();

                entity.Property(x => x.Title)
                    .HasColumnName("title")
                    .HasColumnType("varchar(100)")
                    .HasDefaultValue("Desk")
                    .IsRequired();

                entity.Property(x => x.Tone)
                    .HasColumnName("tone")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("wood")
                    .IsRequired();

                entity.Property(x => x.X)
                    .HasColumnName("x")
                    .HasDefaultValue(20)
                    .IsRequired();

                entity.Property(x => x.Y)
                    .HasColumnName("y")
                    .HasDefaultValue(20)
                    .IsRequired();

                entity.Property(x => x.W)
                    .HasColumnName("w")
                    .HasDefaultValue(8)
                    .IsRequired();

                entity.Property(x => x.H)
                    .HasColumnName("h")
                    .HasDefaultValue(6)
                    .IsRequired();

                entity.Property(x => x.Rotation)
                    .HasColumnName("rotation")
                    .HasDefaultValue(0)
                    .IsRequired();

                entity.Property(x => x.IsObstacle)
                    .HasColumnName("is_obstacle")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true)
                    .IsRequired();

                entity.Property(x => x.SortOrder)
                    .HasColumnName("sort_order")
                    .HasDefaultValue(0)
                    .IsRequired();

                entity.Property(x => x.CreatedByUserId)
                    .HasColumnName("created_by_user_id")
                    .IsRequired(false);

                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .IsRequired();

                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(x => new { x.IsActive, x.ObjectType })
                    .HasDatabaseName("idx_meeting_room_objects_active_type");
            });

            modelBuilder.Entity<MeetingRoomFileShare>(entity =>
            {
                entity.ToTable("meeting_room_file_shares");
                entity.HasKey(x => x.ShareId);

                entity.Property(x => x.ShareId)
                    .HasColumnName("share_id");

                entity.Property(x => x.AreaKey)
                    .HasColumnName("area_key")
                    .HasColumnType("varchar(80)")
                    .IsRequired();

                entity.Property(x => x.AreaTitle)
                    .HasColumnName("area_title")
                    .HasColumnType("varchar(100)")
                    .IsRequired();

                entity.Property(x => x.OriginalFileName)
                    .HasColumnName("original_file_name")
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(x => x.StoredFileName)
                    .HasColumnName("stored_file_name")
                    .HasColumnType("varchar(120)")
                    .IsRequired();

                entity.Property(x => x.ContentType)
                    .HasColumnName("content_type")
                    .HasColumnType("varchar(120)")
                    .HasDefaultValue("application/octet-stream")
                    .IsRequired();

                entity.Property(x => x.FileSize)
                    .HasColumnName("file_size")
                    .IsRequired();

                entity.Property(x => x.FilePath)
                    .HasColumnName("file_path")
                    .HasColumnType("varchar(500)")
                    .IsRequired();

                entity.Property(x => x.UploadedByUserId)
                    .HasColumnName("uploaded_by_user_id")
                    .IsRequired();

                entity.Property(x => x.UploadedByName)
                    .HasColumnName("uploaded_by_name")
                    .HasColumnType("varchar(100)")
                    .IsRequired();

                entity.Property(x => x.UploadedAt)
                    .HasColumnName("uploaded_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .IsRequired();

                entity.Property(x => x.IsDeleted)
                    .HasColumnName("is_deleted")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false)
                    .IsRequired();

                entity.HasIndex(x => new { x.AreaKey, x.IsDeleted, x.UploadedAt })
                    .HasDatabaseName("idx_meeting_room_file_shares_area");

                entity.HasOne(x => x.UploadedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedByUserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // LINE RECIPIENTS
            // =========================
            modelBuilder.Entity<LineRecipient>(entity =>
            {
                entity.ToTable("line_recipients");
                entity.HasKey(x => x.LineRecipientId);

                entity.Property(x => x.LineRecipientId).HasColumnName("line_recipient_id");
                entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired(false);
                entity.Property(x => x.EmpId).HasColumnName("emp_id").IsRequired(false);
                entity.Property(x => x.RecipientType).HasColumnName("recipient_type").HasColumnType("varchar(20)").HasDefaultValue("USER").IsRequired();
                entity.Property(x => x.LineUserId).HasColumnName("line_user_id").HasColumnType("varchar(100)").IsRequired(false);
                entity.Property(x => x.LineGroupId).HasColumnName("line_group_id").HasColumnType("varchar(100)").IsRequired(false);
                entity.Property(x => x.LineDisplayName).HasColumnName("line_display_name").HasColumnType("varchar(255)").IsRequired(false);
                entity.Property(x => x.IsActive).HasColumnName("is_active").HasColumnType("tinyint(1)").HasDefaultValue(true);
                entity.Property(x => x.LastFollowedAt).HasColumnName("last_followed_at").HasColumnType("datetime").IsRequired(false);
                entity.Property(x => x.LastWebhookAt).HasColumnName("last_webhook_at").HasColumnType("datetime").IsRequired(false);
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(x => x.LineUserId)
                    .IsUnique()
                    .HasDatabaseName("uq_line_recipients_user_id");

                entity.HasIndex(x => x.LineGroupId)
                    .IsUnique()
                    .HasDatabaseName("uq_line_recipients_group_id");

                entity.HasIndex(x => new { x.UserId, x.IsActive })
                    .HasDatabaseName("idx_line_recipients_user");

                entity.HasIndex(x => new { x.EmpId, x.IsActive })
                    .HasDatabaseName("idx_line_recipients_emp");

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(x => x.Employee)
                    .WithMany()
                    .HasForeignKey(x => x.EmpId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =========================
            // TELEGRAM RECIPIENTS
            // =========================
            modelBuilder.Entity<TelegramRecipient>(entity =>
            {
                entity.ToTable("telegram_recipients");
                entity.HasKey(x => x.TelegramRecipientId);

                entity.Property(x => x.TelegramRecipientId).HasColumnName("telegram_recipient_id");
                entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired(false);
                entity.Property(x => x.EmpId).HasColumnName("emp_id").IsRequired(false);
                entity.Property(x => x.RecipientType).HasColumnName("recipient_type").HasColumnType("varchar(20)").HasDefaultValue("USER").IsRequired();
                entity.Property(x => x.TelegramUserId).HasColumnName("telegram_user_id").HasColumnType("varchar(100)").IsRequired(false);
                entity.Property(x => x.TelegramChatId).HasColumnName("telegram_chat_id").HasColumnType("varchar(100)").IsRequired(false);
                entity.Property(x => x.TelegramDisplayName).HasColumnName("telegram_display_name").HasColumnType("varchar(255)").IsRequired(false);
                entity.Property(x => x.IsActive).HasColumnName("is_active").HasColumnType("tinyint(1)").HasDefaultValue(true);
                entity.Property(x => x.LastStartedAt).HasColumnName("last_started_at").HasColumnType("datetime").IsRequired(false);
                entity.Property(x => x.LastWebhookAt).HasColumnName("last_webhook_at").HasColumnType("datetime").IsRequired(false);
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAddOrUpdate();

                entity.HasIndex(x => x.TelegramUserId)
                    .IsUnique()
                    .HasDatabaseName("uq_telegram_recipients_user_id");

                entity.HasIndex(x => x.TelegramChatId)
                    .IsUnique()
                    .HasDatabaseName("uq_telegram_recipients_chat_id");

                entity.HasIndex(x => new { x.UserId, x.IsActive })
                    .HasDatabaseName("idx_telegram_recipients_user");

                entity.HasIndex(x => new { x.EmpId, x.IsActive })
                    .HasDatabaseName("idx_telegram_recipients_emp");

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(x => x.Employee)
                    .WithMany()
                    .HasForeignKey(x => x.EmpId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =========================
            // WEEKLY REPORTS + MAILBOX
            // =========================
            modelBuilder.Entity<WeeklyReport>(entity =>
            {
                entity.ToTable("weekly_reports");
                entity.HasKey(x => x.ReportId);

                entity.Property(x => x.ReportId).HasColumnName("report_id");
                entity.Property(x => x.WeekStart).HasColumnName("week_start").HasColumnType("date").IsRequired(false);
                entity.Property(x => x.WeekEnd).HasColumnName("week_end").HasColumnType("date").IsRequired(false);
                entity.Property(x => x.Subject).HasColumnName("subject").HasColumnType("varchar(255)").IsRequired();
                entity.Property(x => x.Summary).HasColumnName("summary").HasColumnType("text").IsRequired(false);
                entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(30)").HasDefaultValue("DRAFT").IsRequired();
                entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(false);
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id").IsRequired(false);
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAddOrUpdate();
                entity.Property(x => x.SentToPmAt).HasColumnName("sent_to_pm_at").HasColumnType("datetime").IsRequired(false);
                entity.Property(x => x.SentToBdmAt).HasColumnName("sent_to_bdm_at").HasColumnType("datetime").IsRequired(false);

                entity.HasIndex(x => new { x.CreatedByUserId, x.Status, x.CreatedAt })
                    .HasDatabaseName("idx_weekly_reports_creator");

                entity.HasMany(x => x.Attachments)
                    .WithOne(x => x.Report)
                    .HasForeignKey(x => x.ReportId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<WeeklyReportAttachment>(entity =>
            {
                entity.ToTable("weekly_report_attachments");
                entity.HasKey(x => x.AttachmentId);

                entity.Property(x => x.AttachmentId).HasColumnName("attachment_id");
                entity.Property(x => x.ReportId).HasColumnName("report_id").IsRequired();
                entity.Property(x => x.FileName).HasColumnName("file_name").HasColumnType("varchar(255)").IsRequired();
                entity.Property(x => x.FilePath).HasColumnName("file_path").HasColumnType("varchar(500)").IsRequired();
                entity.Property(x => x.ContentType).HasColumnName("content_type").HasColumnType("varchar(150)").IsRequired(false);
                entity.Property(x => x.FileSize).HasColumnName("file_size").HasColumnType("bigint").HasDefaultValue(0);
                entity.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired(false);
                entity.Property(x => x.UploadedAt).HasColumnName("uploaded_at").HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(x => x.ReportId).HasDatabaseName("idx_weekly_report_attachments_report");
            });

            // =========================
            // EMPLOYEE (เพิ่มให้ชัดเจน + กันชื่อซ้ำ/encoding)
            // =========================
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.HasKey(e => e.EmpId);

                // ไม่ลบของเดิม แค่เสริม mapping เฉยๆ (ถ้าใน Model มี EmpName)
                entity.Property(e => e.EmpName)
                    .HasColumnType("varchar(255)")
                    .IsRequired(false);

                // ถ้า Employee ของคุณมี FullName จริงค่อยเปิดใช้ (ถ้าไม่มี EF จะมองไม่เห็นอยู่แล้ว)
                // entity.Property(e => e.FullName)
                //     .HasColumnType("varchar(255)")
                //     .IsRequired(false);
            });

            // =========================
            // COOPERATIVE MASTER
            // =========================
            modelBuilder.Entity<CntMCoop>(entity =>
            {
                entity.ToTable("cnt_m_coop");
                entity.HasKey(c => c.CoopId);

                entity.Property(c => c.CoopId)
                    .HasColumnName("coop_id")
                    .HasColumnType("int");

                entity.Property(c => c.CoopName)
                    .HasColumnName("coop_name")
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.HasIndex(c => c.CoopName)
                    .HasDatabaseName("idx_cnt_m_coop_name");
            });

            // =========================
            // PROJECT
            // =========================
            modelBuilder.Entity<Project>(entity =>
            {
                entity.ToTable("project");
                entity.HasKey(p => p.ProjectId);

                entity.Property(p => p.ProjectId)
                    .HasColumnName("project_id")
                    .HasColumnType("int");

                entity.Property(p => p.CoopId)
                    .HasColumnName("coop_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(p => p.ProjectName)
                    .HasColumnName("project_name")
                    .HasColumnType("varchar(150)");

                // 👤 Business Analyst
                entity.Property(p => p.BaEmpId)
                    .HasColumnName("ba_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                // 👤 Project Manager
                entity.Property(p => p.PmEmpId)
                    .HasColumnName("pm_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(p => p.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(p => p.EntryId)
                    .HasColumnName("entry_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(p => p.RequirementCardId)
                    .HasColumnName("requirement_card_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(p => p.ProjectDetail)
                    .HasColumnName("project_detail")
                    .HasColumnType("text")
                    .IsRequired(false);

                // 👤 Business Analyst relationship
                entity.HasOne(p => p.BA)
                    .WithMany()
                    .HasForeignKey(p => p.BaEmpId)
                    .OnDelete(DeleteBehavior.SetNull);

                // 👤 Project Manager relationship
                entity.HasOne(p => p.PM)
                    .WithMany()
                    .HasForeignKey(p => p.PmEmpId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(p => p.RequirementCard)
                    .WithMany()
                    .HasForeignKey(p => p.RequirementCardId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(p => p.Coop)
                    .WithMany(c => c.Projects)
                    .HasForeignKey(p => p.CoopId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(p => p.ProjectName);
                entity.HasIndex(p => p.CoopId).HasDatabaseName("idx_project_coop_id");
                entity.HasIndex(p => p.PmEmpId).HasDatabaseName("idx_project_pm_emp_id");
                entity.HasIndex(p => p.RequirementCardId).HasDatabaseName("idx_project_requirement_card_id");
            });

            modelBuilder.Entity<StatusApprovalRequest>(entity =>
            {
                entity.ToTable("status_approval_requests");
                entity.HasKey(x => x.RequestId);

                entity.Property(x => x.RequestId)
                    .HasColumnName("request_id")
                    .HasColumnType("int");

                entity.Property(x => x.TargetType)
                    .HasColumnName("target_type")
                    .HasColumnType("varchar(30)")
                    .IsRequired();

                entity.Property(x => x.TargetId)
                    .HasColumnName("target_id")
                    .HasColumnType("int")
                    .IsRequired();

                entity.Property(x => x.ProjectId)
                    .HasColumnName("project_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.ProjectName)
                    .HasColumnName("project_name")
                    .HasColumnType("varchar(255)")
                    .IsRequired(false);

                entity.Property(x => x.TargetTitle)
                    .HasColumnName("target_title")
                    .HasColumnType("varchar(500)")
                    .IsRequired(false);

                entity.Property(x => x.CurrentStatus)
                    .HasColumnName("current_status")
                    .HasColumnType("varchar(50)")
                    .IsRequired(false);

                entity.Property(x => x.RequestedStatus)
                    .HasColumnName("requested_status")
                    .HasColumnType("varchar(50)")
                    .IsRequired();

                entity.Property(x => x.RequestStatus)
                    .HasColumnName("request_status")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("PENDING")
                    .IsRequired();

                entity.Property(x => x.RequestNote)
                    .HasColumnName("request_note")
                    .HasColumnType("varchar(1000)")
                    .IsRequired(false);

                entity.Property(x => x.RequestedByUserId)
                    .HasColumnName("requested_by_user_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.RequestedByEmpId)
                    .HasColumnName("requested_by_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.RequestedAt)
                    .HasColumnName("requested_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .IsRequired();

                entity.Property(x => x.ReviewedByUserId)
                    .HasColumnName("reviewed_by_user_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.ReviewedByEmpId)
                    .HasColumnName("reviewed_by_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.ReviewedAt)
                    .HasColumnName("reviewed_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(x => x.ReviewNote)
                    .HasColumnName("review_note")
                    .HasColumnType("varchar(1000)")
                    .IsRequired(false);

                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .IsRequired();

                entity.HasIndex(x => new { x.TargetType, x.TargetId, x.RequestStatus })
                    .HasDatabaseName("idx_status_approval_target_status");

                entity.HasIndex(x => new { x.ProjectId, x.RequestStatus })
                    .HasDatabaseName("idx_status_approval_project_status");

                entity.HasIndex(x => x.RequestedAt)
                    .HasDatabaseName("idx_status_approval_requested_at");
            });

            // =========================
            // PROJECT DOCUMENTS
            // =========================
            modelBuilder.Entity<ProjectDocument>(entity =>
            {
                entity.ToTable("project_documents");

                entity.HasKey(d => d.DocumentId);

                entity.Property(d => d.DocumentId)
                    .HasColumnName("document_id");

                entity.Property(d => d.ProjectId)
                    .HasColumnName("project_id");

                entity.Property(d => d.DocumentType)
                    .HasColumnName("document_type")
                    .HasColumnType("varchar(20)");

                entity.Property(d => d.FileName)
                    .HasColumnName("file_name")
                    .HasColumnType("varchar(255)");

                entity.Property(d => d.FilePath)
                    .HasColumnName("file_path")
                    .HasColumnType("varchar(500)");

                entity.Property(d => d.UploadedBy)
                    .HasColumnName("uploaded_by")
                    .HasColumnType("varchar(100)")
                    .IsRequired(false);

                entity.Property(d => d.UploadedAt)
                    .HasColumnName("uploaded_at")
                    .HasColumnType("datetime");

                entity.HasIndex(d => d.ProjectId);

                entity.HasOne(d => d.Project)
                    .WithMany()
                    .HasForeignKey(d => d.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // TEST SCENARIOS
            // =========================
            modelBuilder.Entity<TestScenario>(entity =>
            {
                entity.ToTable("project_test_scenarios");
                entity.HasKey(x => x.scenario_id);

                entity.Property(x => x.scenario_id)
                    .HasColumnName("scenario_id");

                entity.Property(x => x.project_id)
                    .HasColumnName("project_id")
                    .IsRequired();

                entity.Property(x => x.title)
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(x => x.precondition)
                    .HasColumnType("text")
                    .IsRequired(false);

                entity.Property(x => x.steps)
                    .HasColumnType("text")
                    .IsRequired();

                entity.Property(x => x.expected_result)
                    .HasColumnType("text")
                    .IsRequired();

                entity.Property(x => x.remark)
                    .HasColumnType("text")
                    .IsRequired(false);

                entity.Property(x => x.priority)
                    .HasColumnType("varchar(10)")
                    .HasDefaultValue("MEDIUM");

                entity.Property(x => x.status)
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("READY");

                entity.Property(x => x.created_by)
                    .HasColumnType("varchar(50)")
                    .IsRequired(false);

                entity.Property(x => x.created_at)
                    .HasColumnType("datetime");

                entity.Property(x => x.updated_at)
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.HasIndex(x => x.project_id);

                entity.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(x => x.project_id)
                    .HasPrincipalKey(p => p.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // TEST SCENARIO TEMPLATES
            // =========================
            modelBuilder.Entity<TestScenarioTemplate>(entity =>
            {
                entity.ToTable("test_scenario_templates");
                entity.HasKey(x => x.template_id);

                entity.Property(x => x.template_id)
                    .HasColumnName("template_id");

                entity.Property(x => x.group_id)
                    .HasColumnName("group_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(x => x.title)
                    .HasColumnType("varchar(255)")
                    .IsRequired();

                entity.Property(x => x.precondition)
                    .HasColumnType("text")
                    .IsRequired(false);

                entity.Property(x => x.steps)
                    .HasColumnType("text")
                    .IsRequired();

                entity.Property(x => x.expected_result)
                    .HasColumnType("text")
                    .IsRequired();

                entity.Property(x => x.priority_default)
                    .HasColumnType("varchar(10)")
                    .HasDefaultValue("MEDIUM");

                entity.Property(x => x.status_default)
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("READY");

                entity.Property(x => x.is_active)
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true);

                entity.Property(x => x.created_at)
                    .HasColumnType("datetime");

                entity.Property(x => x.updated_at)
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.HasOne(x => x.Group)
                    .WithMany(g => g.Templates)
                    .HasForeignKey(x => x.group_id)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // =========================
            // TEST TEMPLATE GROUPS
            // =========================
            modelBuilder.Entity<TestTemplateGroup>(entity =>
            {
                entity.ToTable("test_template_groups");
                entity.HasKey(x => x.group_id);

                entity.Property(x => x.group_id)
                    .HasColumnName("group_id");

                entity.Property(x => x.group_name)
                    .HasColumnType("varchar(200)")
                    .IsRequired();

                entity.Property(x => x.sort_order)
                    .HasColumnName("sort_order")
                    .HasColumnType("int")
                    .HasDefaultValue(0);

                entity.Property(x => x.is_active)
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true);

                entity.Property(x => x.created_at)
                    .HasColumnType("datetime");
            });

            // =========================
            // PROJECT PHASE
            // =========================
            modelBuilder.Entity<ProjectPhase>(entity =>
            {
                entity.ToTable("project_phase");

                entity.HasKey(p => p.PhaseId);

                entity.Property(p => p.PhaseId)
                    .HasColumnName("phase_id");

                entity.Property(p => p.ProjectId)
                    .HasColumnName("project_id")
                    .IsRequired();

                entity.Property(p => p.PhaseName)
                    .HasColumnName("phase_name")
                    .HasColumnType("varchar(500)");

                entity.Property(p => p.PhaseStatus)
                    .HasColumnName("phase_status")
                    .HasColumnType("varchar(50)")
                    .IsRequired(false);

                entity.Property(p => p.PhaseOrder)
                    .HasColumnName("phase_order")
                    .HasColumnType("int");

                entity.Property(p => p.PeriodOrder)
                    .HasColumnName("period_order")
                    .HasColumnType("int");

                entity.Property(p => p.PlanStart)
                    .HasColumnName("plan_start")
                    .IsRequired(false);

                entity.Property(p => p.PlanEnd)
                    .HasColumnName("plan_end")
                    .IsRequired(false);

                entity.Property(p => p.SubmittedDate)
                    .HasColumnName("submitted_date")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(p => p.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(p => p.EntryId)
                    .HasColumnName("entry_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.Property(p => p.PeriodEndDate)
                    .HasColumnName("period_end_date")
                    .IsRequired(false);

                // ✅ Relation ProjectPhase -> Project
                entity.HasOne(p => p.Project)
                    .WithMany()
                    .HasForeignKey(p => p.ProjectId)
                    .HasPrincipalKey(p => p.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(p => p.ProjectId);
                entity.HasIndex(p => p.PlanStart);
                entity.HasIndex(p => p.PlanEnd);
                entity.HasIndex(p => new { p.ProjectId, p.PlanStart, p.PlanEnd });
            });

            // =========================
            // PHASE ASSIGN
            // =========================
            modelBuilder.Entity<PhaseAssign>(entity =>
            {
                // ❗ ไม่ไปยุ่ง PK เดิมของคุณ เพื่อ "ไม่กระทบของเดิม"
                // ถ้า PhaseAssign มี PK อยู่แล้ว ให้ Model/Convention เป็นตัวกำหนดตามเดิม
                // (ห้ามบังคับ composite key เอง เพราะจะทำให้ schema/pk เปลี่ยน)

                // ✅ Index (ปลอดภัย ไม่กระทบ schema หลักมาก และช่วย query)
                entity.HasIndex(a => a.EmpId);
                entity.HasIndex(a => a.PhaseId);
                entity.HasIndex(a => new { a.EmpId, a.PhaseId });

                entity.Property(a => a.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(a => a.EntryId)
                    .HasColumnName("entry_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                // =========================
                // ✅ FIX WARNING PhaseId1 (Shadow FK)
                // =========================
                // EF สร้าง PhaseId1 เพราะมันเห็น "ความสัมพันธ์ชนกัน" รอบ PhaseId
                // วิธีแก้ที่ไม่กระทบของเดิม:
                // 1) บังคับให้ใช้ FK: PhaseAssign.PhaseId -> ProjectPhase.PhaseId แบบ explicit
                // 2) ถ้ามี shadow property "PhaseId1" โผล่ ให้ ignore ไปเลย (กัน EF สร้าง/ใช้มัน)
                //
                // หมายเหตุ: เรา "ไม่บังคับ navigation" เพื่อไม่ให้ชนกับของเดิม
                // ใช้ HasOne<ProjectPhase>() แบบ no-navigation

                // ✅ (2) ignore shadow FK ถ้าเคยถูกสร้าง
                entity.Ignore("PhaseId1");
                // ✅ ignore shadow FK ที่ทำให้ไป SELECT/JOIN คอลัมน์ที่ไม่มีจริงใน DB
                entity.Ignore("PhaseId2");
                // ✅ ignore shadow FK เพิ่มเติม (ถ้า EF สร้างต่อเนื่อง)
                entity.Ignore("PhaseId3");

                // ✅ (1) บังคับ FK ให้ชัด (ใช้ navigation a.Phase เพื่อตัดปัญหา relationship ซ้ำ)
                entity.HasOne(a => a.Phase)
                    .WithMany()
                    .HasForeignKey(a => a.PhaseId)
                    .HasPrincipalKey(p => p.PhaseId)
                    .OnDelete(DeleteBehavior.Cascade);

                // ✅ FK -> Employee (EmpId)
                // คงของเดิมไว้ (ใช้ navigation a.Employee ตามที่คุณมี)
                entity.HasOne(a => a.Employee)
                    .WithMany()
                    .HasForeignKey(a => a.EmpId)
                    .OnDelete(DeleteBehavior.Restrict);

                // ✅ ถ้า PhaseAssign ของคุณ "มี" navigation ที่ทำให้ชน เช่น a.ProjectPhase
                // และคุณไม่ได้ใช้มันจริง ๆ ให้เปิด ignore บรรทัดนี้เพื่อตัดปัญหาชน (ไม่ลบ property ใน model)
                // entity.Ignore(a => a.ProjectPhase);
            });

            // =========================
            // PHASE ASSIGN LOGS
            // =========================
            modelBuilder.Entity<PhaseAssignLog>(entity =>
            {
                entity.ToTable("phase_assign_logs");

                entity.HasKey(x => x.LogId);

                entity.Property(x => x.AssignId)
                    .HasColumnName("assign_id")
                    .IsRequired();

                entity.Property(x => x.Status)
                    .HasColumnName("status")
                    .HasColumnType("varchar(10)")
                    .IsRequired();

                entity.Property(x => x.Remark)
                    .HasColumnName("remark")
                    .HasColumnType("varchar(1000)")
                    .IsRequired(false);

                entity.Property(x => x.RoundNo)
                    .HasColumnName("round_no")
                    .HasDefaultValue(1);

                entity.Property(x => x.CreatedBy)
                    .HasColumnName("created_by")
                    .IsRequired(false);

                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime");

                entity.HasIndex(x => x.AssignId);

                entity.HasOne(x => x.PhaseAssign)
                    .WithMany(p => p.Logs)
                    .HasForeignKey(x => x.AssignId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // PROJECT ISSUE
            // =========================
            modelBuilder.Entity<ProjectIssue>(entity =>
            {
                entity.HasKey(i => i.IssueId);
                // ✅ IssueStatus (Business status)
                entity.Property(i => i.IssueStatus)
                    .HasColumnName("IssueStatus")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("OPEN")
                    .IsRequired();

                entity.HasOne(i => i.Project)
                    .WithMany()
                    .HasForeignKey(i => i.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Employee)
                    .WithMany(e => e.ProjectIssues)
                    .HasForeignKey(i => i.AssignTo)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(i => i.AssignTo)
                    .HasColumnName("assign_to")
                    .HasColumnType("int")
                    .IsRequired();

                entity.Property(i => i.CreatedBy)
                    .HasColumnName("created_by")
                    .HasColumnType("int")
                    .IsRequired(false);

                // 🔁 REOPEN FIELD MAPPING (สำคัญมากสำหรับ MySQL)
                entity.Property(i => i.IsReopen)
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(i => i.ReopenCount)
                    .HasDefaultValue(0);

                entity.Property(i => i.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAddOrUpdate();

                // ✅ DevStatus (Programmer status)
                entity.Property(i => i.DevStatus)
                    .HasColumnName("DevStatus")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("WIP")
                    .IsRequired();

                // BEFORE IMAGES
                entity.HasMany(i => i.Images)
                    .WithOne(img => img.Issue!)
                    .HasForeignKey(img => img.IssueId)
                    .OnDelete(DeleteBehavior.Cascade);

                // AFTER FIX IMAGES
                entity.HasMany(i => i.FixImages)
                    .WithOne(img => img.Issue!)
                    .HasForeignKey(img => img.IssueId)
                    .OnDelete(DeleteBehavior.Cascade);

                // ✅ ถ้าลบ Issue ให้ลบ History ตาม
                // (ไม่จำเป็นต้องมี navigation ก็ใช้ FK ของ History ได้อยู่แล้ว)
            });

            // =========================
            // ISSUE STATUS HISTORY
            // =========================
            modelBuilder.Entity<ProjectIssueStatusHistory>(entity =>
            {
                // ✅ ชื่อตารางให้ตรงกับที่คุณจะสร้างใน MySQL
                entity.ToTable("ProjectIssueStatusHistories");

                // ✅ PK
                entity.HasKey(x => x.Id);

                // ✅ Columns
                entity.Property(x => x.IssueId)
                    .IsRequired();

                entity.Property(x => x.OldStatus)
                    .HasColumnType("varchar(20)")
                    .HasMaxLength(20)
                    .IsRequired(false);

                entity.Property(x => x.NewStatus)
                    .HasColumnType("varchar(20)")
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.IsReopen)
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(x => x.ReopenCount)
                    .HasColumnType("int")
                    .HasDefaultValue(0);

                entity.Property(x => x.ChangedAt)
                    .HasColumnType("datetime")
                    .IsRequired();

                // ✅ ถ้า Model ของคุณมี ChangedByEmpId (ตาม Controller ที่ insert)
                // ให้ map ไว้ด้วย (ถ้าใน Model ไม่มี ก็ไม่เป็นไร EF จะ ignore)
                entity.Property(x => x.ChangedByEmpId)
                    .HasColumnType("int")
                    .IsRequired(false);

                // ✅ Indexes (ช่วย query Yesterday snapshot)
                entity.HasIndex(x => x.IssueId);
                entity.HasIndex(x => x.ChangedAt);
                entity.HasIndex(x => new { x.IssueId, x.ChangedAt });

                // ✅ FK -> ProjectIssue (Cascade เมื่อ Issue ถูกลบ)
                entity.HasOne(x => x.Issue)
                    .WithMany() // ไม่บังคับให้มี navigation ใน ProjectIssue
                    .HasForeignKey(x => x.IssueId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // ISSUE GIT HISTORY
            // =========================
            modelBuilder.Entity<ProjectIssueGitHistory>(entity =>
            {
                entity.ToTable("project_issue_git_histories");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.IssueId).HasColumnName("issue_id").IsRequired();
                entity.Property(x => x.GitType).HasColumnName("git_type").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
                entity.Property(x => x.GitId).HasColumnName("git_id").HasColumnType("varchar(80)").HasMaxLength(80).IsRequired();
                entity.Property(x => x.EntryDate).HasColumnName("entry_date").HasColumnType("datetime").IsRequired();
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id").HasColumnType("int").IsRequired(false);

                entity.HasIndex(x => x.IssueId);
                entity.HasIndex(x => x.EntryDate);
                entity.HasIndex(x => new { x.IssueId, x.EntryDate });

                entity.HasOne(x => x.Issue)
                    .WithMany()
                    .HasForeignKey(x => x.IssueId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // SUPPORT ORDER STATUS HISTORY
            // =========================
            modelBuilder.Entity<ProjectSupportOrderStatusHistory>(entity =>
            {
                entity.ToTable("project_support_order_status_histories");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id)
                    .HasColumnName("id");

                entity.Property(x => x.OrderId)
                    .HasColumnName("order_id")
                    .IsRequired();

                entity.Property(x => x.OldStatus)
                    .HasColumnName("old_status")
                    .HasColumnType("varchar(20)")
                    .HasMaxLength(20)
                    .IsRequired(false);

                entity.Property(x => x.NewStatus)
                    .HasColumnName("new_status")
                    .HasColumnType("varchar(20)")
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.IsReopen)
                    .HasColumnName("is_reopen")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(x => x.ReopenCount)
                    .HasColumnName("reopen_count")
                    .HasColumnType("int")
                    .HasDefaultValue(0);

                entity.Property(x => x.ChangedAt)
                    .HasColumnName("changed_at")
                    .HasColumnType("datetime")
                    .IsRequired();

                entity.Property(x => x.ChangedByEmpId)
                    .HasColumnName("changed_by_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.ChangedAt);
                entity.HasIndex(x => new { x.OrderId, x.ChangedAt });

                entity.HasOne(x => x.Order)
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // SUPPORT ORDER GIT HISTORY
            // =========================
            modelBuilder.Entity<ProjectSupportOrderGitHistory>(entity =>
            {
                entity.ToTable("project_support_order_git_histories");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
                entity.Property(x => x.GitType).HasColumnName("git_type").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
                entity.Property(x => x.GitId).HasColumnName("git_id").HasColumnType("varchar(80)").HasMaxLength(80).IsRequired();
                entity.Property(x => x.EntryDate).HasColumnName("entry_date").HasColumnType("datetime").IsRequired();
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id").HasColumnType("int").IsRequired(false);

                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.EntryDate);
                entity.HasIndex(x => new { x.OrderId, x.EntryDate });

                entity.HasOne(x => x.Order)
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // ISSUE IMAGE (BEFORE)
            // =========================
            modelBuilder.Entity<ProjectIssueImage>()
                .HasKey(img => img.ImageId);

            // =========================
            // ISSUE IMAGE (AFTER FIX)
            // =========================
            modelBuilder.Entity<ProjectIssueFixImage>()
                .HasKey(img => img.ImageId);

            // =========================
            // EMAIL SEND LOG
            // =========================
            modelBuilder.Entity<EmailSendLog>(entity =>
            {
                // ถ้าคุณมี PK อยู่แล้วก็ไม่ต้องแก้
                // ถ้าไม่มี PK EF จะ error ตอนทำ migration/ใช้งาน
                // ตัวอย่างนี้เสริมไว้เฉย ๆ แบบปลอดภัย (คุณปรับตาม model จริงได้)
                // entity.HasKey(x => x.Id);
            });

            // =========================
            // TEST SCENARIO ATTACHMENTS
            // =========================
            modelBuilder.Entity<TestScenarioAttachment>(entity =>
            {
                entity.ToTable("test_scenario_attachments");

                entity.HasKey(e => e.AttachmentId);

                entity.Property(e => e.AttachmentId).HasColumnName("attachment_id");
                entity.Property(e => e.ScenarioId).HasColumnName("scenario_id");
                entity.Property(e => e.FileName).HasColumnName("file_name");
                entity.Property(e => e.FilePath).HasColumnName("file_path");
                entity.Property(e => e.FileType).HasColumnName("file_type");
                entity.Property(e => e.FileSize).HasColumnName("file_size");
                entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
                entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at");
            });

            // =========================
            // REQUIREMENT BOARD
            // =========================
            modelBuilder.Entity<RequirementBoardGroup>(entity =>
            {
                entity.ToTable("requirement_board_groups");
                entity.HasKey(x => x.GroupId);

                entity.Property(x => x.GroupId).HasColumnName("group_id");
                entity.Property(x => x.GroupName).HasColumnName("group_name").HasColumnType("varchar(150)").IsRequired();
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id");
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasMany(x => x.Boards)
                    .WithOne(x => x.Group)
                    .HasForeignKey(x => x.GroupId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.IsActive, x.SortOrder }).HasDatabaseName("idx_requirement_board_groups_active_sort");
            });

            modelBuilder.Entity<RequirementBoard>(entity =>
            {
                entity.ToTable("requirement_boards");
                entity.HasKey(x => x.BoardId);

                entity.Property(x => x.BoardId).HasColumnName("board_id");
                entity.Property(x => x.GroupId).HasColumnName("group_id");
                entity.Property(x => x.BoardName).HasColumnName("board_name").HasColumnType("varchar(150)").IsRequired();
                entity.Property(x => x.CoverImagePath).HasColumnName("cover_image_path").HasColumnType("varchar(500)");
                entity.Property(x => x.CoverColor).HasColumnName("cover_color").HasColumnType("varchar(20)").IsRequired();
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id");
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasMany(x => x.Columns)
                    .WithOne(x => x.Board)
                    .HasForeignKey(x => x.BoardId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.GroupId, x.SortOrder }).HasDatabaseName("idx_requirement_boards_group_sort");
                entity.HasIndex(x => new { x.IsActive, x.SortOrder }).HasDatabaseName("idx_requirement_boards_active_sort");
            });

            modelBuilder.Entity<RequirementBoardColumn>(entity =>
            {
                entity.ToTable("requirement_board_columns");
                entity.HasKey(x => x.ColumnId);

                entity.Property(x => x.ColumnId).HasColumnName("column_id");
                entity.Property(x => x.BoardId).HasColumnName("board_id");
                entity.Property(x => x.ColumnName).HasColumnName("column_name").HasColumnType("varchar(150)").IsRequired();
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id");
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasMany(x => x.Cards)
                    .WithOne(x => x.Column)
                    .HasForeignKey(x => x.ColumnId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.SortOrder).HasDatabaseName("idx_requirement_columns_sort");
                entity.HasIndex(x => new { x.BoardId, x.SortOrder }).HasDatabaseName("idx_requirement_columns_board_sort");
            });

            modelBuilder.Entity<RequirementCard>(entity =>
            {
                entity.ToTable("requirement_cards");
                entity.HasKey(x => x.CardId);

                entity.Property(x => x.CardId).HasColumnName("card_id");
                entity.Property(x => x.ColumnId).HasColumnName("column_id");
                entity.Property(x => x.Title).HasColumnName("title").HasColumnType("varchar(255)").IsRequired();
                entity.Property(x => x.Detail).HasColumnName("detail").HasColumnType("text");
                entity.Property(x => x.CoverImagePath).HasColumnName("cover_image_path").HasColumnType("varchar(500)");
                entity.Property(x => x.CoverImageName).HasColumnName("cover_image_name").HasColumnType("varchar(255)");
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.IsArchived).HasColumnName("is_archived");
                entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id");
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasMany(x => x.Attachments)
                    .WithOne(x => x.Card)
                    .HasForeignKey(x => x.CardId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(x => x.Labels)
                    .WithOne(x => x.Card)
                    .HasForeignKey(x => x.CardId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(x => x.CreatedByEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByEmpId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(x => new { x.ColumnId, x.SortOrder }).HasDatabaseName("idx_requirement_cards_column_sort");
                entity.HasIndex(x => x.CreatedByUserId).HasDatabaseName("idx_requirement_cards_created_by_user");
            });

            modelBuilder.Entity<RequirementBoardLabel>(entity =>
            {
                entity.ToTable("requirement_board_labels");
                entity.HasKey(x => x.LabelId);

                entity.Property(x => x.LabelId).HasColumnName("label_id");
                entity.Property(x => x.LabelName).HasColumnName("label_name").HasColumnType("varchar(100)").IsRequired();
                entity.Property(x => x.ColorHex).HasColumnName("color_hex").HasColumnType("varchar(20)").IsRequired();
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
                entity.Property(x => x.CreatedByEmpId).HasColumnName("created_by_emp_id");
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
                entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

                entity.HasMany(x => x.CardLabels)
                    .WithOne(x => x.Label)
                    .HasForeignKey(x => x.LabelId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.IsActive, x.SortOrder }).HasDatabaseName("idx_requirement_labels_active_sort");
            });

            modelBuilder.Entity<RequirementCardLabel>(entity =>
            {
                entity.ToTable("requirement_card_labels");
                entity.HasKey(x => new { x.CardId, x.LabelId });

                entity.Property(x => x.CardId).HasColumnName("card_id");
                entity.Property(x => x.LabelId).HasColumnName("label_id");
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

                entity.HasIndex(x => x.LabelId).HasDatabaseName("idx_requirement_card_labels_label");
            });

            modelBuilder.Entity<RequirementCardAttachment>(entity =>
            {
                entity.ToTable("requirement_card_attachments");
                entity.HasKey(x => x.AttachmentId);

                entity.Property(x => x.AttachmentId).HasColumnName("attachment_id");
                entity.Property(x => x.CardId).HasColumnName("card_id");
                entity.Property(x => x.FileName).HasColumnName("file_name").HasColumnType("varchar(255)").IsRequired();
                entity.Property(x => x.StoredFileName).HasColumnName("stored_file_name").HasColumnType("varchar(255)").IsRequired();
                entity.Property(x => x.FilePath).HasColumnName("file_path").HasColumnType("varchar(500)").IsRequired();
                entity.Property(x => x.ContentType).HasColumnName("content_type").HasColumnType("varchar(150)");
                entity.Property(x => x.FileSize).HasColumnName("file_size");
                entity.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id");
                entity.Property(x => x.UploadedByEmpId).HasColumnName("uploaded_by_emp_id");
                entity.Property(x => x.UploadedAt).HasColumnName("uploaded_at").HasColumnType("datetime");

                entity.HasOne(x => x.UploadedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(x => x.UploadedByEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.UploadedByEmpId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(x => x.CardId).HasDatabaseName("idx_requirement_attachments_card");
            });

            // ============================
            // VIEW : vw_phase_owner_status
            // ============================
            modelBuilder.Entity<VwPhaseOwnerStatus>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("vw_phase_owner_status");
            });

            // =========================
            // ATTENDANCE
            // =========================
            modelBuilder.Entity<Attendance>(entity =>
            {
                entity.ToTable("attendance");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.EmpId)
                    .HasColumnName("emp_id")
                    .IsRequired();

                entity.Property(x => x.WorkDate)
                    .HasColumnName("work_date")
                    .HasColumnType("date")
                    .IsRequired();

                entity.Property(x => x.CheckinTime)
                    .HasColumnName("checkin_time")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(x => x.CheckinLat)
                    .HasColumnName("checkin_lat")
                    .HasColumnType("decimal(10,7)")
                    .IsRequired(false);

                entity.Property(x => x.CheckinLng)
                    .HasColumnName("checkin_lng")
                    .HasColumnType("decimal(10,7)")
                    .IsRequired(false);

                entity.Property(x => x.CheckoutTime)
                    .HasColumnName("checkout_time")
                    .HasColumnType("datetime")
                    .IsRequired(false);

                entity.Property(x => x.CheckoutLat)
                    .HasColumnName("checkout_lat")
                    .HasColumnType("decimal(10,7)")
                    .IsRequired(false);

                entity.Property(x => x.CheckoutLng)
                    .HasColumnName("checkout_lng")
                    .HasColumnType("decimal(10,7)")
                    .IsRequired(false);

                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime");

                entity.HasIndex(x => new { x.EmpId, x.WorkDate })
                    .IsUnique()
                    .HasDatabaseName("uq_emp_date");

                entity.HasOne<Employee>()
                    .WithMany()
                    .HasForeignKey(x => x.EmpId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}

namespace ProjectTracking.Models
{
    public class MeetingEmailNotification
    {
        public int Id { get; set; }
        public int MeetingId { get; set; }
        public int AttendeeId { get; set; }

        // Kind is explicitly set by meeting notification flows (e.g., "created_email", "line_reminder_3d").
        // Keep the default empty to avoid accidentally forcing an outdated enum value.
        public string Kind { get; set; } = string.Empty;

        public DateTime SentAt { get; set; }
    }
}
