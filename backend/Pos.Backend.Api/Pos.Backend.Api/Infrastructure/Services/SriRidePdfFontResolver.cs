using System.Collections.Concurrent;
using System.Reflection;
using PdfSharp.Fonts;

namespace Pos.Backend.Api.Infrastructure.Services;

internal sealed class SriRidePdfFontResolver : IFontResolver
{
    public const string SansFamily = "HfposRideSans";
    public const string MonoFamily = "HfposRideMono";

    private const string LatoRegularFace = "hfpos-ride-lato-regular";
    private const string LatoBoldFace = "hfpos-ride-lato-bold";
    private const string IbmPlexMonoRegularFace = "hfpos-ride-ibm-plex-mono-regular";

    private static readonly SriRidePdfFontResolver Instance = new();
    private static readonly object RegistrationLock = new();
    private static readonly ConcurrentDictionary<string, byte[]> FontCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _registered;

    private static readonly IReadOnlyDictionary<string, string> ResourceByFaceName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LatoRegularFace] = "HfposRideFonts.Lato-Regular.ttf",
            [LatoBoldFace] = "HfposRideFonts.Lato-Bold.ttf",
            [IbmPlexMonoRegularFace] = "HfposRideFonts.IBMPlexMono-Regular.ttf",
        };

    public static void Register()
    {
        lock (RegistrationLock)
        {
            if (_registered)
            {
                return;
            }

            var currentResolver = GlobalFontSettings.FontResolver;
            if (currentResolver is not null && !ReferenceEquals(currentResolver, Instance))
            {
                throw new InvalidOperationException("PDFSHARP_FONT_RESOLVER_ALREADY_CONFIGURED");
            }

            GlobalFontSettings.FontResolver = Instance;
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var normalizedFamily = NormalizeFamilyName(familyName);

        if (normalizedFamily == NormalizeFamilyName(MonoFamily))
        {
            return new FontResolverInfo(IbmPlexMonoRegularFace);
        }

        if (normalizedFamily == NormalizeFamilyName(SansFamily))
        {
            return new FontResolverInfo(bold ? LatoBoldFace : LatoRegularFace);
        }

        return null;
    }

    public byte[]? GetFont(string faceName)
    {
        if (!ResourceByFaceName.TryGetValue(faceName, out var resourceName))
        {
            return null;
        }

        return FontCache.GetOrAdd(resourceName, LoadFontResource);
    }

    private static byte[] LoadFontResource(string resourceName)
    {
        var assembly = typeof(SriRidePdfFontResolver).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded PDF font resource '{resourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return memory.ToArray();
    }

    private static string NormalizeFamilyName(string value)
        => value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
