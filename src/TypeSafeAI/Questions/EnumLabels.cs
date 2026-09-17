using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;

namespace TypeSafeAI;

/// <summary>
/// Resolves wire labels, descriptions, and ordinal positions for an enum used as choice options or score levels.
/// Members are ordered by their underlying value; that order defines score level indices.
/// </summary>
public static class EnumLabels<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Entry[] Entries = Build();
    private static readonly Dictionary<string, TEnum> ByLabel = BuildIndex(StringComparer.Ordinal);
    private static readonly Dictionary<string, TEnum> ByLabelIgnoreCase = BuildIndex(StringComparer.OrdinalIgnoreCase);

    /// <summary>The members ordered by underlying value.</summary>
    public static IReadOnlyList<TEnum> Members { get; } = Entries.Select(e => e.Value).ToArray();

    /// <summary>The labels in member order.</summary>
    public static IReadOnlyList<string> Labels { get; } = Entries.Select(e => e.Label).ToArray();

    /// <summary>Gets the wire label for a member.</summary>
    public static string GetLabel(TEnum value) => Find(value).Label;

    /// <summary>Gets the description for a member, or <see langword="null"/> when none is declared.</summary>
    public static string? GetDescription(TEnum value) => Find(value).Description;

    /// <summary>Gets the ordinal position of a member, which is its score level index.</summary>
    public static int IndexOf(TEnum value)
    {
        for (var i = 0; i < Entries.Length; i++)
        {
            if (EqualityComparer<TEnum>.Default.Equals(Entries[i].Value, value))
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, $"{value} is not a declared member of {typeof(TEnum).Name}.");
    }

    /// <summary>Gets the member at an ordinal position.</summary>
    public static TEnum AtIndex(int index)
    {
        if ((uint)index >= (uint)Entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{typeof(TEnum).Name} has {Entries.Length} members.");
        }

        return Entries[index].Value;
    }

    /// <summary>Maps a wire label back to a member. Exact match first, then case-insensitive.</summary>
    public static bool TryParse(string label, out TEnum value) =>
        ByLabel.TryGetValue(label, out value) || ByLabelIgnoreCase.TryGetValue(label, out value);

    /// <summary>Builds choice criteria: label to description.</summary>
    public static Dictionary<string, TypeSafeContent?> ToChoiceCriteria()
    {
        var criteria = new Dictionary<string, TypeSafeContent?>(Entries.Length, StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            criteria.Add(entry.Label, entry.Description is null ? null : (TypeSafeContent?)TypeSafeContent.FromText(entry.Description));
        }

        return criteria;
    }

    /// <summary>Builds score criteria: one level description per member, using the description or the label when none is declared.</summary>
    public static TypeSafeContent?[] ToScoreCriteria() =>
        Entries.Select(e => (TypeSafeContent?)TypeSafeContent.FromText(e.Description ?? e.Label)).ToArray();

    private static Entry Find(TEnum value)
    {
        foreach (var entry in Entries)
        {
            if (EqualityComparer<TEnum>.Default.Equals(entry.Value, value))
            {
                return entry;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, $"{value} is not a declared member of {typeof(TEnum).Name}.");
    }

    // The trimmer keeps every field of an enum type once the type is used, so reading label attributes off the fields is safe.
    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "The trimmer preserves all fields of enum types, including their custom attributes.")]
    private static Entry[] Build()
    {
        var values = Enum.GetValues<TEnum>();
        if (values.Length == 0)
        {
            throw new InvalidOperationException($"{typeof(TEnum).Name} declares no members.");
        }

        var entries = new List<Entry>(values.Length);
        foreach (var value in values)
        {
            var name = value.ToString();
            var field = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static);
            var labelAttribute = field?.GetCustomAttribute<LabelAttribute>();
            var label = labelAttribute?.Label
                ?? field?.GetCustomAttribute<EnumMemberAttribute>()?.Value
                ?? name;
            var description = labelAttribute?.Description
                ?? field?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            entries.Add(new Entry(value, label, description));
        }

        // Enum.GetValues already orders by underlying value; sort defensively so the contract holds.
        entries.Sort((a, b) => Comparer<TEnum>.Default.Compare(a.Value, b.Value));
        return entries.ToArray();
    }

    private static Dictionary<string, TEnum> BuildIndex(StringComparer comparer)
    {
        var index = new Dictionary<string, TEnum>(Entries.Length, comparer);
        foreach (var entry in Entries)
        {
            if (!index.TryAdd(entry.Label, entry.Value) && comparer == StringComparer.Ordinal)
            {
                throw new InvalidOperationException($"{typeof(TEnum).Name} declares the label '{entry.Label}' more than once.");
            }
        }

        return index;
    }

    private readonly record struct Entry(TEnum Value, string Label, string? Description);
}
