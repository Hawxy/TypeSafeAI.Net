using System.Globalization;

namespace TypeSafeAI;

// Gathers the images a request's content carries, keyed by reference, and whether the request needs the images header.
internal sealed class AssetCollector
{
    private Dictionary<string, AssetPayload>? _assets;

    public IReadOnlyDictionary<string, AssetPayload>? Assets => _assets;

    public bool RequiresImages { get; set; }

    public void Add(TypeSafeContent? content)
    {
        if (content is not { Assets.Count: > 0 } value)
        {
            return;
        }

        _assets ??= new Dictionary<string, AssetPayload>(StringComparer.Ordinal);
        foreach (var image in value.Assets)
        {
            if (_assets.TryGetValue(image.Reference, out var existing))
            {
                if (!existing.Image.HasSameData(image))
                {
                    throw new ArgumentException($"Two different images use the reference '{image.Reference}'.");
                }

                continue;
            }

            _assets.Add(image.Reference, new AssetPayload(image, "image-" + _assets.Count.ToString(CultureInfo.InvariantCulture)));
        }

        RequiresImages = true;
    }
}
