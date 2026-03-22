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
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectDocument> ProjectDocuments { get; set; }
        public DbSet<ProjectPhase> ProjectPhases { get; set; }
        public DbSet<PhaseAssign> PhaseAssigns { get; set; }
        public DbSet<LoginUser> LoginUsers { get; set; }
        public DbSet<UserMenu> UserMenus { get; set; }

        // ===== Meetings =====
        public DbSet<Meeting> Meetings { get; set; }
        public DbSet<MeetingAttendee> MeetingAttendees { get; set; }
        public DbSet<MeetingEmailNotification> MeetingEmailNotifications { get; set; }

        // ===== Issues =====
        public DbSet<ProjectIssue> ProjectIssues { get; set; }
        public DbSet<ProjectIssueImage> ProjectIssueImages { get; set; }
        public DbSet<ProjectIssueFixImage> ProjectIssueFixImages { get; set; }

        // ===== Support Orders (Warranty / Maintenance) =====
        public DbSet<ProjectSupportOrder> ProjectSupportOrders { get; set; }
        public DbSet<ProjectSupportImage> ProjectSupportImages { get; set; }
        public DbSet<ProjectSupportFixImage> ProjectSupportFixImages { get; set; }

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

                entity.Property(m => m.ProjectId)
                    .HasColumnType("int")
                    .IsRequired(false);

                // created_at may be managed by DB default
                entity.Property(m => m.CreatedAt)
                    .HasColumnType("timestamp");

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

            // =========================
            // LOGIN USER
            // =========================
            modelBuilder.Entity<LoginUser>()
                .HasKey(u => u.UserId);

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
            // PROJECT
            // =========================
            modelBuilder.Entity<Project>(entity =>
            {
                entity.ToTable("project");
                entity.HasKey(p => p.ProjectId);

                entity.Property(p => p.ProjectId)
                    .HasColumnName("project_id")
                    .HasColumnType("int");

                entity.Property(p => p.ProjectName)
                    .HasColumnName("project_name")
                    .HasColumnType("varchar(150)");

                // 👤 Business Analyst
                entity.Property(p => p.BaEmpId)
                    .HasColumnName("ba_emp_id")
                    .HasColumnType("int")
                    .IsRequired(false);

                // 👤 Business Analyst relationship
                entity.HasOne(p => p.BA)
                    .WithMany()
                    .HasForeignKey(p => p.BaEmpId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(p => p.ProjectName);
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
                    .HasDefaultValue("DRAFT");

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
                    .HasDefaultValue("DRAFT");

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
                entity.HasKey(p => p.PhaseId);

                // ✅ เสริม mapping ช่วงแผนให้ชัด (ใช้กับ workload overlap)
                entity.Property(p => p.PlanStart).IsRequired(false);
                entity.Property(p => p.PlanEnd).IsRequired(false);

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
                    .HasForeignKey(i => i.EmpId)
                    .OnDelete(DeleteBehavior.Restrict);

                // 🔁 REOPEN FIELD MAPPING (สำคัญมากสำหรับ MySQL)
                entity.Property(i => i.IsReopen)
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(i => i.ReopenCount)
                    .HasDefaultValue(0);

                entity.Property(i => i.LastFixedAt)
                    .IsRequired(false);

                // ✅ DevStatus (Programmer status)
                entity.Property(i => i.DevStatus)
                    .HasColumnName("DevStatus")
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue("TODO")
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

            // ============================
            // VIEW : vw_phase_owner_status
            // ============================
            modelBuilder.Entity<VwPhaseOwnerStatus>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("vw_phase_owner_status");
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

        // Kind is explicitly set by background services (e.g., "reminder_10m", "reminder_1d").
        // Keep the default empty to avoid accidentally forcing an outdated enum value.
        public string Kind { get; set; } = string.Empty;

        public DateTime SentAt { get; set; }
    }
}