namespace ProjectTracking.Models
{
    public class EmployeeNotesViewModel
    {
        public Employee Employee { get; set; } = null!;
        public IReadOnlyList<EmployeeNote> Notes { get; set; } = Array.Empty<EmployeeNote>();
        public EmployeeNote? SelectedNote { get; set; }
    }
}
