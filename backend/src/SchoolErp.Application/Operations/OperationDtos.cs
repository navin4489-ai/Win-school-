using System.ComponentModel.DataAnnotations;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Operations;

public sealed class EmployeeDto
{
    public int Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Department { get; init; }
    public string? Designation { get; init; }
    public DateOnly JoiningDate { get; init; }
    public EmploymentStatus Status { get; init; }
}

public sealed class SaveEmployeeRequest
{
    [Required, MaxLength(120)] public string FirstName { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string LastName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(20)] public string? Phone { get; set; }
    public int? DepartmentId { get; set; }
    public int? DesignationId { get; set; }
    public DateOnly JoiningDate { get; set; }
    [MaxLength(250)] public string? Qualification { get; set; }
    public decimal? ExperienceYears { get; set; }
    public decimal? BasicSalary { get; set; }
}

public sealed class LeaveDto
{
    public int Id { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string LeaveType { get; init; } = string.Empty;
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public string Reason { get; init; } = string.Empty;
    public LeaveStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class ApplyLeaveRequest
{
    public int? EmployeeId { get; set; }
    [Required, MaxLength(50)] public string LeaveType { get; set; } = "Casual";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class PayslipDto
{
    public int Id { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public decimal Basic { get; init; }
    public decimal Gross { get; init; }
    public decimal Deductions { get; init; }
    public decimal Net { get; init; }
    public int DaysPresent { get; init; }
    public int DaysAbsent { get; init; }
}

public sealed class PayrollRunDto
{
    public int Id { get; init; }
    public int Month { get; init; }
    public int Year { get; init; }
    public PayrollStatus Status { get; init; }
    public decimal TotalGross { get; init; }
    public decimal TotalDeductions { get; init; }
    public decimal TotalNet { get; init; }
    public int EmployeeCount { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
}

public sealed class RunPayrollRequest
{
    [Range(1, 12)] public int Month { get; set; }
    [Range(2000, 2100)] public int Year { get; set; }
}

public sealed class VehicleDto
{
    public int Id { get; init; }
    public string RegistrationNo { get; init; } = string.Empty;
    public string? Model { get; init; }
    public int Capacity { get; init; }
    public string? DriverName { get; init; }
    public string? DriverPhone { get; init; }
    public DateOnly? InsuranceExpiry { get; init; }
    public DateOnly? FitnessExpiry { get; init; }
}

public sealed class SaveVehicleRequest
{
    [Required, MaxLength(30)] public string RegistrationNo { get; set; } = string.Empty;
    [MaxLength(100)] public string? Model { get; set; }
    [Range(1, 200)] public int Capacity { get; set; }
    [MaxLength(120)] public string? DriverName { get; set; }
    [MaxLength(20)] public string? DriverPhone { get; set; }
    [MaxLength(50)] public string? DriverLicenceNo { get; set; }
    public DateOnly? InsuranceExpiry { get; set; }
    public DateOnly? FitnessExpiry { get; set; }
}

public sealed class TransportRouteDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public int? VehicleId { get; init; }
    public string? StartPoint { get; init; }
    public string? EndPoint { get; init; }
    public decimal MonthlyFee { get; init; }
    public int StopCount { get; init; }
}

public sealed class SaveTransportRouteRequest
{
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public int? VehicleId { get; set; }
    [MaxLength(150)] public string? StartPoint { get; set; }
    [MaxLength(150)] public string? EndPoint { get; set; }
    [Range(0, 1_000_000)] public decimal MonthlyFee { get; set; }
}

public sealed class TransportAllocationDto
{
    public int Id { get; init; }
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public int TransportRouteId { get; init; }
    public string RouteName { get; init; } = string.Empty;
    public int? TransportStopId { get; init; }
    public DateOnly StartDate { get; init; }
    public bool IsActive { get; init; }
}

public sealed class AllocateTransportRequest
{
    [Range(1, int.MaxValue)] public int StudentId { get; set; }
    [Range(1, int.MaxValue)] public int TransportRouteId { get; set; }
    public int? TransportStopId { get; set; }
    public DateOnly? StartDate { get; set; }
}

public sealed class BookDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Isbn { get; init; }
    public string? Category { get; init; }
    public string? Author { get; init; }
    public int TotalCopies { get; init; }
    public int AvailableCopies { get; init; }
    public string? Shelf { get; init; }
}

public sealed class SaveBookRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [MaxLength(30)] public string? Isbn { get; set; }
    [MaxLength(80)] public string? Category { get; set; }
    [MaxLength(150)] public string? Author { get; set; }
    [MaxLength(150)] public string? Publisher { get; set; }
    [Range(1, 10_000)] public int TotalCopies { get; set; }
    [MaxLength(50)] public string? Shelf { get; set; }
}

public sealed class IssueBookRequest
{
    [Range(1, int.MaxValue)] public int BookId { get; set; }
    public int? StudentId { get; set; }
    public int? EmployeeId { get; set; }
    public int LoanDays { get; set; } = 14;
}

public sealed class ReturnBookRequest
{
    [Range(1, int.MaxValue)] public int BookTransactionId { get; set; }
    public bool IsLost { get; set; }
    public decimal? FineAmount { get; set; }
}

public sealed class BookTransactionDto
{
    public int Id { get; init; }
    public int BookId { get; init; }
    public string BookTitle { get; init; } = string.Empty;
    public string Borrower { get; init; } = string.Empty;
    public BookTransactionType Type { get; init; }
    public DateTime IssuedAtUtc { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public DateTime? ReturnedAtUtc { get; init; }
    public decimal FineAmount { get; init; }
}

public sealed class InventoryItemDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal MinimumStock { get; init; }
    public decimal UnitPrice { get; init; }
    public bool IsBelowMinimum { get; init; }
}

public sealed class SaveInventoryItemRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(80)] public string? Category { get; set; }
    [MaxLength(20)] public string Unit { get; set; } = "Nos";
    [Range(0, 1_000_000)] public decimal Quantity { get; set; }
    [Range(0, 1_000_000)] public decimal MinimumStock { get; set; }
    [Range(0, 10_000_000)] public decimal UnitPrice { get; set; }
    [MaxLength(100)] public string? Location { get; set; }
}

public sealed class StockEntryRequest
{
    [Range(1, int.MaxValue)] public int InventoryItemId { get; set; }
    public StockTransactionType Type { get; set; } = StockTransactionType.Inward;
    [Range(0.01, 1_000_000)] public decimal Quantity { get; set; }
    [MaxLength(100)] public string? ReferenceNo { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public sealed class LookupDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class StudyMaterialDto
{
    public int Id { get; init; }
    public int ClassId { get; init; }
    public int? SectionId { get; init; }
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? FileUrl { get; init; }
    public string? VideoUrl { get; init; }
    public string? Chapter { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SaveStudyMaterialRequest
{
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    public int? SectionId { get; set; }
    [Range(1, int.MaxValue)] public int SubjectId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    [Url, MaxLength(500)] public string? FileUrl { get; set; }
    [Url, MaxLength(500)] public string? VideoUrl { get; set; }
    [MaxLength(80)] public string? Chapter { get; set; }
}
