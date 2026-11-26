using System.Globalization;
using NewUAFGJ;

const string Usage = "Usage: NewUAFGJ [--raw | [--typetree-source <schema-file>] [--resize-atlas [--sync-atlas-materials]]] <source> <replacement> [pathId] [fileId]";

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    bool rawReplacement = false;
    bool resizeAtlas = false;
    bool syncAtlasMaterials = false;
    string? typeTreeSource = null;
    while (args is ["--raw" or "--resize-atlas" or "--sync-atlas-materials" or "--typetree-source", ..])
    {
        switch (args)
        {
            case ["--raw", ..] when !rawReplacement:
                rawReplacement = true;
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
    var replacedClass = AssetReplacement.Replace(args[0], args[1], pathId, rawReplacement, typeTreeSource,
        resizeAtlas, syncAtlasMaterials);
    Console.WriteLine($"{replacedClass} replaced.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"[FATAL] {error.Message}");
    return 1;
}
