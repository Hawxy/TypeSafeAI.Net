using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>
/// Locates what its instructions describe in an image from the request. The answer is a box around it.
/// Needs a model that supports boxes, such as <c>jev-preview</c>. Requests with box questions send the <c>TypeSafe-Danger-Zone-Images: true</c> header.
/// </summary>
public sealed class BoxQuestion : Question
{
    /// <summary>Creates a box question.</summary>
    /// <param name="instructions">What to find and in which image, such as "the coffee mug in `photo`".</param>
    [JsonConstructor]
    public BoxQuestion(TypeSafeContent? instructions)
        : base(instructions)
    {
    }

    /// <summary>The wire name of this question type.</summary>
    public const string TypeName = "box";

    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => TypeName;

    internal override void CollectAssets(AssetCollector assets)
    {
        assets.Add(Instructions);
        assets.RequiresImages = true;
    }
}
