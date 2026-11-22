using System.ComponentModel.DataAnnotations;

namespace SchoolErp.Application.Common;

/// <summary>
/// Optional e-mail address. Null, empty and whitespace-only values are valid — use
/// [Required] as well when a value is mandatory. Non-empty values must still be
/// well-formed e-mail addresses.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionalEmailAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Email = new();

    public OptionalEmailAttribute()
        : base("The {0} field is not a valid e-mail address.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var text = value as string ?? value.ToString();
        return string.IsNullOrWhiteSpace(text) || Email.IsValid(text);
    }
}
