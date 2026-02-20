using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("Employees.Index")]
    public class EmployeesController : BaseController
    {
        private readonly AppDbContext _context;

        public EmployeesController(AppDbContext context)
        {
            _context = context;
        }

        // ===========================
        // GET: /Employees
        // แสดงเฉพาะพนักงาน ACTIVE
        // ===========================
        public async Task<IActionResult> Index()
        {
            var employees = await _context.Employees
                .Where(e => e.Status == "ACTIVE")
                .OrderBy(e => e.Position)
                .ThenBy(e => e.EmpId)
                .ToListAsync();

            return View(employees);
        }

        // ===========================
        // GET: /Employees/Create
        // ===========================
        public IActionResult Create()
        {
            return View();
        }

        // ===========================
        // POST: /Employees/Create
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee)
        {
            if (ModelState.IsValid)
            {
                _context.Add(employee);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(employee);
        }

        // ===========================
        // GET: /Employees/Edit/5
        // ===========================
        public async Task<IActionResult> Edit(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
            {
                return NotFound();
            }

            return View(employee);
        }

        // ===========================
        // POST: /Employees/Edit/5
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.EmpId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                _context.Update(employee);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(employee);
        }

        // ===========================
        // GET: /Employees/Delete/5
        // ===========================
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmpId == id);

            if (employee == null)
            {
                return NotFound();
            }

            return View(employee);
        }

        // ===========================
        // POST: /Employees/Delete/5
        // Soft Delete
        // ===========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee != null)
            {
                employee.Status = "INACTIVE";
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}