namespace SchoolErp.Domain.Entities;

/// <summary>
/// An uploaded file kept in the database (logos, photos, documents), so uploads survive
/// redeploys on hosts without a persistent disk. Served at /files/db/{Id}.
/// </summary>
public class StoredBlob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? SchoolId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
