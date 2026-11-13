using SchoolErp.Application.Common;
using Xunit;

namespace SchoolErp.Tests;

/// <summary>
/// Swashbuckle keys OpenAPI schemas by the short type name. Two DTOs that share a name in
/// different namespaces collide while the document is being generated, which makes the whole
/// <c>/swagger/v1/swagger.json</c> endpoint answer 500. These tests keep that from coming back.
/// </summary>
public sealed class DtoNamingTests
{
    [Fact]
    public void Application_Types_Have_Unique_Short_Names()
    {
        var duplicates = DescribeDuplicates();

        Assert.True(duplicates.Count == 0,
            "Duplicate type names break Swagger schema generation:" + Environment.NewLine +
            string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void Application_Dtos_Use_Their_Own_Namespace_Prefix_When_Ambiguous()
    {
        // Guards the specific rename that fixed the AnnouncementDto collision: the parent-facing
        // summary DTO and the administration CRUD DTO must stay distinguishable by name.
        var parents = typeof(SchoolErp.Application.Parents.ParentAnnouncementDto);
        var administration = typeof(SchoolErp.Application.Administration.AnnouncementDto);

        Assert.NotEqual(parents.Name, administration.Name);
        Assert.Equal("ParentAnnouncementDto", parents.Name);
    }

    private static List<string> DescribeDuplicates()
    {
        return typeof(PagedResult<>).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass && !type.IsGenericTypeDefinition)
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(type => type.FullName))}")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();
    }
}
