using SchoolErp.Domain.Common;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Domain.Entities;

public class StudentAttendance : AuditableEntity
{
    public int StudentId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Remarks { get; set; }
    public int? MarkedByEmployeeId { get; set; }
    public DateTime MarkedAtUtc { get; set; } = DateTime.UtcNow;
    public AttendanceSource Source { get; set; } = AttendanceSource.Manual;
}

public class EmployeeAttendance : AuditableEntity
{
    public int EmployeeId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public string? Remarks { get; set; }
    public AttendanceSource Source { get; set; } = AttendanceSource.Manual;
}

public class Vehicle : AuditableEntity
{
    public string RegistrationNo { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int Capacity { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? DriverLicenceNo { get; set; }
    public DateOnly? InsuranceExpiry { get; set; }
    public DateOnly? FitnessExpiry { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TransportRoute : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int? VehicleId { get; set; }
    public string? StartPoint { get; set; }
    public string? EndPoint { get; set; }
    public decimal MonthlyFee { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TransportStop : AuditableEntity
{
    public int TransportRouteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan? PickupTime { get; set; }
    public TimeSpan? DropTime { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int Order { get; set; }
}

public class TransportAllocation : AuditableEntity
{
    public int StudentId { get; set; }
    public int TransportRouteId { get; set; }
    public int? TransportStopId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Book : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public string? Category { get; set; }
    public string? Author { get; set; }
    public string? Publisher { get; set; }
    public string? Edition { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public string? Shelf { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BookTransaction : AuditableEntity
{
    public int BookId { get; set; }
    public int? StudentId { get; set; }
    public int? EmployeeId { get; set; }
    public BookTransactionType Type { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DueAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public decimal FineAmount { get; set; }
    public string? Remarks { get; set; }
}

public class InventoryItem : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = "Nos";
    public decimal Quantity { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Vendor : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? GstNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PurchaseOrder : AuditableEntity
{
    public int VendorId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public string Status { get; set; } = "Draft";
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public int? ApprovedBy { get; set; }
}

public class PurchaseOrderItem : AuditableEntity
{
    public int PurchaseOrderId { get; set; }
    public int InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ReceivedQuantity { get; set; }
}

public class StockTransaction : AuditableEntity
{
    public int InventoryItemId { get; set; }
    public StockTransactionType Type { get; set; }
    public decimal Quantity { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
    public DateOnly TransactionDate { get; set; }
    public int? RecordedBy { get; set; }
}

public class Notification : AuditableEntity
{
    public int? UserAccountId { get; set; }
    public int? StudentId { get; set; }
    public int? GuardianId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public string Category { get; set; } = "General";
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public string? Recipient { get; set; }
}

public class Document : AuditableEntity
{
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string? Checksum { get; set; }
    public int? UploadedBy { get; set; }
    public bool IsScanned { get; set; }
    public bool IsClean { get; set; } = true;
}

public class StudentDocument : AuditableEntity
{
    public int StudentId { get; set; }
    public int DocumentId { get; set; }
    public DocumentType DocumentType { get; set; } = DocumentType.Other;
    public bool IsVerified { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
}

/// <summary>Append-only audit record. Not soft-deleted.</summary>
public class AuditLog : Entity
{
    public int SchoolId { get; set; }
    public int? UserAccountId { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
