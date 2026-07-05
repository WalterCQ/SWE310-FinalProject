using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Helpers;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NonWhiteSpaceAttribute : ValidationAttribute
{
    public NonWhiteSpaceAttribute()
    {
        ErrorMessage = "The {0} field cannot be empty or whitespace.";
    }

    public override bool IsValid(object? value)
    {
        return value is not string text || !string.IsNullOrWhiteSpace(text);
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NonEmptyGuidAttribute : ValidationAttribute
{
    public NonEmptyGuidAttribute()
    {
        ErrorMessage = "The {0} field must be a non-empty GUID.";
    }

    public override bool IsValid(object? value)
    {
        return value is not Guid guid || guid != Guid.Empty;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DefinedEnumAttribute : ValidationAttribute
{
    public DefinedEnumAttribute()
    {
        ErrorMessage = "The {0} field has an unsupported enum value.";
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var valueType = value.GetType();
        return valueType.IsEnum && Enum.IsDefined(valueType, value);
    }
}
