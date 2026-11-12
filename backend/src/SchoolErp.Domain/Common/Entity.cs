namespace SchoolErp.Domain.Common;

public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>Fields stamped on every school-owned record.</summary>
public interface IAuditable
{
    int SchoolId { get; set; }
    int? BranchId { get; set; }
    DateTime CreatedAtUtc { get; set; }
    string? CreatedBy { get; set; }
    DateTime? ModifiedAtUtc { get; set; }
    string? ModifiedBy { get; set; }
    bool IsDeleted { get; set; }
    byte[]? RowVersion { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditable
{
    public int SchoolId { get; set; }
    public int? BranchId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[]? RowVersion { get; set; }
}

public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Approve = "Approve";
    public const string Publish = "Publish";
    public const string Refund = "Refund";
    public const string Export = "Export";
}
