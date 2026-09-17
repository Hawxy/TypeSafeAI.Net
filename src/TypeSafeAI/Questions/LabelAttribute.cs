namespace TypeSafeAI;

/// <summary>
/// Sets the wire label and optional description for an enum member used as a choice option or score level.
/// Without it, the label falls back to <see cref="System.Runtime.Serialization.EnumMemberAttribute"/> and then the member name,
/// and the description to <see cref="System.ComponentModel.DescriptionAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class LabelAttribute : Attribute
{
    /// <summary>Creates a label attribute.</summary>
    public LabelAttribute(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
    }

    /// <summary>The label sent to the API.</summary>
    public string Label { get; }

    /// <summary>The description sent as the option or level criteria.</summary>
    public string? Description { get; set; }
}
