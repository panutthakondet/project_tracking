using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using MySqlConnector;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        // หน้า Dashboard
        [RequireMenu("Dashboard.Workload")]
        public IActionResult Workload()
        {
            return View();
        }

        // API สำหรับ Heatmap
        [HttpGet]
        public async Task<IActionResult> GetWorkloadData(int yearFrom, int yearTo)
        {
            var sql = @"
    WITH RECURSIVE weeks AS (
        SELECT 
            1 AS week_no,
            DATE(CONCAT(@yearFrom, '-01-01')) AS week_start,
            DATE_ADD(DATE(CONCAT(@yearFrom, '-01-01')), INTERVAL 6 DAY) AS week_end

        UNION ALL

        SELECT 
            week_no + 1,
            DATE_ADD(week_start, INTERVAL 7 DAY),
            DATE_ADD(week_end, INTERVAL 7 DAY)
        FROM weeks
        WHERE DATE_ADD(week_start, INTERVAL 7 DAY) <= DATE(CONCAT(@yearTo, '-12-31'))
    )

    SELECT 
        e.emp_name AS Emp_Name,
        p.project_name AS Project_Name,
        YEAR(w.week_start) AS Year_No,
        MONTH(w.week_start) AS Month_No,
        FLOOR((DAY(w.week_start)-1)/7) + 1 AS Week_No,

        p.start_date AS Project_Start,
        p.end_date AS Project_End,

        pa.plan_start AS Plan_Start,
        pa.plan_end AS Plan_End,

        pa.actual_start AS Actual_Start,
        pa.actual_end AS Actual_End

    FROM weeks w
    LEFT JOIN phase_assign pa 
        ON pa.plan_start <= w.week_end
       AND pa.plan_end >= w.week_start
    LEFT JOIN employee e ON pa.emp_id = e.emp_id
    LEFT JOIN project_phase ph ON pa.phase_id = ph.phase_id
    LEFT JOIN project p ON ph.project_id = p.project_id
    WHERE e.emp_name IS NOT NULL
    GROUP BY e.emp_name, p.project_name, w.week_start, pa.plan_start, pa.plan_end, pa.actual_start, pa.actual_end, p.start_date, p.end_date
    ORDER BY e.emp_name, p.project_name, YEAR(w.week_start), MONTH(w.week_start), Week_No
";

            var parameters = new[]
            {
                new MySqlParameter("@yearFrom", yearFrom),
                new MySqlParameter("@yearTo", yearTo)
            };

            var data = await _context.Database
                .SqlQueryRaw<WorkloadDto>(sql, parameters)
                .ToListAsync();

            return Json(data);
        }

        public class WorkloadDto
        {
            public string Emp_Name { get; set; } = string.Empty;
            public int Year_No { get; set; }
            public int Month_No { get; set; }
            public int Week_No { get; set; }
            public string Project_Name { get; set; } = string.Empty;

            public DateTime? Project_Start { get; set; }
            public DateTime? Project_End { get; set; }

            public DateTime? Plan_Start { get; set; }
            public DateTime? Plan_End { get; set; }

            public DateTime? Actual_Start { get; set; }
            public DateTime? Actual_End { get; set; }
        }
    }
}