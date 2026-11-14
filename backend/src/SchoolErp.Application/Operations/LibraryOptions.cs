namespace SchoolErp.Application.Operations;

/// <summary>Bound from the "Library" configuration section.</summary>
public sealed class LibraryOptions
{
    public const string SectionName = "Library";

    public decimal FinePerDay { get; set; } = 5m;
}
