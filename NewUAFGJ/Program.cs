using System.Diagnostics;
using System.Globalization;
using NewUAFGJ;

long started = Stopwatch.GetTimestamp();
const string Usage = """
    Usage: NewUAFGJ [--profile] [--typetree-root <game-data>] [--raw | [--typetree-source <schema-file>] [--downscale | --resize-atlas [--sync-atlas-materials]]] <source> <replacement> [pathId] [fileId]
           NewUAFGJ [--profile] batch <plan.json>
           NewUAFGJ [--profile] repack <bundle> <compression-reference>
    """;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    if (args is ["batch", var plan])
        return BatchCommand.Run(plan, printProfile: false, started);
    if (args is ["--profile", "batch", var profiledPlan])
        return BatchCommand.Run(profiledPlan, printProfile: true, started);
    if (args is ["repack", var bundle, var reference])
        return BatchCommand.Repack(bundle, reference, printProfile: false, started);
    if (args is ["--profile", "repack", var profiledBundle, var profiledReference])
        return BatchCommand.Repack(profiledBundle, profiledReference, printProfile: true, started);

    bool rawReplacement = false;
    bool downscale = false;
    bool resizeAtlas = false;
    bool syncAtlasMaterials = false;
    string? typeTreeSource = null;
    var roots = new List<string>();
    while (args is ["--profile" or "--raw" or "--downscale" or "--resize-atlas" or "--sync-atlas-materials" or "--typetree-source" or "--typetree-root", ..])
    {
        switch (args)
        {
            case ["--profile", ..] when PerformanceProfile.Current is null:
                PerformanceProfile.Current = new PerformanceProfile(started);
                args = args[1..];
                break;
            case ["--raw", ..] when !rawReplacement:
                rawReplacement = true;
                args = args[1..];
                break;
            case ["--downscale", ..] when !downscale:
                downscale = true;
                args = args[1..];
                break;
            case ["--resize-atlas", ..] when !resizeAtlas:
                resizeAtlas = true;
                args = args[1..];
                break;
            case ["--sync-atlas-materials", ..] when !syncAtlasMaterials:
                syncAtlasMaterials = true;
                args = args[1..];
                break;
            case ["--typetree-source", var templatePath, ..] when typeTreeSource is null:
                typeTreeSource = templatePath;
                args = args[2..];
                break;
            case ["--typetree-root", var root, ..]:
                roots.Add(root);
                args = args[2..];
                break;
            default:
                throw new ArgumentException(Usage);
        }
    }

    if (args.Length is < 2 or > 5 || (rawReplacement && (resizeAtlas || typeTreeSource is not null)))
        throw new ArgumentException(Usage);
    if (syncAtlasMaterials && !resizeAtlas)
        throw new ArgumentException("--sync-atlas-materials requires --resize-atlas.");

    long? pathId = null;
    if (args.Length >= 3 && args[2] != "-")
    {
        if (!long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedPathId))
            throw new ArgumentException("PathID must be a signed 64-bit integer or '-'.");
        pathId = parsedPathId;
    }

    if (args.Length >= 4 && args[3] is not ("-" or "0"))
        throw new ArgumentException("FileID must be '-' or '0'. External file references are unsupported.");

    // A fifth positional argument is accepted for legacy callers. The serialized class controls the operation.
    if (roots.Count == 0)
        roots.Add(ReplacementTypeTree.GameRoot(args[0]));
    var resolver = new ReplacementTypeTree(roots, typeTreeSource is null ? null : [typeTreeSource]);
    var operation = new ReplacementOperation(args[1], pathId, args.Length >= 4 ? args[3] : null,
        typeTreeSource, rawReplacement, resizeAtlas, syncAtlasMaterials, Downscale: downscale);
    PerformanceProfile.Current?.Record(PerformanceProfile.Initialization, started);
    var replacedClass = AssetReplacement.Replace(args[0], operation, resolver);
    Console.WriteLine($"{replacedClass} replaced.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"[FATAL] {error.Message}");
    return 1;
}
finally
{
    PerformanceProfile.Current?.Print();
}
