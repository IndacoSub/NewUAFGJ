using System.Collections.Concurrent;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal sealed class ReplacementTypeTree
{
    private sealed record Key(string UnityVersion, uint FormatVersion, int TypeId, string TypeHash, string ScriptHash)
    {
        public override string ToString() =>
            $"Unity {UnityVersion}, serialized format {FormatVersion}, TypeID {TypeId}, TypeHash {TypeHash}, ScriptIdHash {ScriptHash}";
    }

    private sealed record Definition(Key Key, TypeTreeType Tree, string Location);
    private sealed record Donor(List<Definition> Definitions, ClassPackageFile? Package = null, byte[]? OriginalDump = null);
    private sealed class Match(Definition first)
    {
        internal Definition First { get; } = first;
        internal Definition? Conflict { get; private set; }

        internal void Add(Definition other)
        {
            if (Conflict is null && !SameDefinition(First.Tree.TypeBlob, other.Tree.TypeBlob))
                Conflict = other;
        }

        internal TypeTreeType GetTree(string className)
        {
            if (Conflict is not null)
                throw new InvalidDataException(
                    $"Conflicting definitions for {className} ({First.Key}) in '{First.Location}' and '{Conflict.Location}'.");
            return First.Tree;
        }
    }

    private readonly string[] roots;
    private readonly IReadOnlyDictionary<string, string> snapshots;
    private readonly HashSet<string> snapshotPaths;
    private readonly ConcurrentDictionary<string, Donor> loaded = new(BatchCommand.PathComparer);
    private readonly ConcurrentDictionary<string, Lazy<Donor>> explicitDonors = new(BatchCommand.PathComparer);
    private readonly Lazy<Dictionary<Key, Match>> discovery;

    internal ReplacementTypeTree(IEnumerable<string> roots, IEnumerable<string>? explicitSources = null,
        IReadOnlyDictionary<string, string>? snapshots = null)
    {
        this.roots = roots.Select(Path.GetFullPath).Distinct(BatchCommand.PathComparer).ToArray();
        this.snapshots = snapshots ?? new Dictionary<string, string>(BatchCommand.PathComparer);
        snapshotPaths = new HashSet<string>(this.snapshots.Values, BatchCommand.PathComparer);
        foreach (string path in explicitSources ?? [])
        {
            string fullPath = Path.GetFullPath(path);
            explicitDonors.TryAdd(fullPath, CreateDonorReader(fullPath));
        }
        discovery = new Lazy<Dictionary<Key, Match>>(ScanGame);
    }

    internal static string GameRoot(string source)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        for (DirectoryInfo? parent = new(directory); parent is not null; parent = parent.Parent)
        {
            if (parent.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(Path.Combine(parent.FullName, "StreamingAssets")))
            {
                return parent.FullName;
            }
        }
        return directory;
    }

    internal void RegisterLoaded(string path, IEnumerable<(AssetsFile File, string Location)> files)
    {
        path = Path.GetFullPath(path);
        bool explicitSource = explicitDonors.ContainsKey(path);
        if (!explicitSource && (discovery.IsValueCreated ||
            (!snapshotPaths.Contains(path) && !roots.Any(root => IsWithin(root, path)))))
            return;

        List<Definition> definitions = [];
        foreach (var (file, location) in files)
            AddDefinitions(definitions, file, location, monoOnly: !explicitSource);
        // The metadata is kept independently of the bundle reader and its decompressed data.
        loaded.TryAdd(path, new Donor(definitions));
    }

    internal byte[]? OriginalFontDump(string sourcePath)
    {
        using var timing = PerformanceProfile.Measure(PerformanceProfile.TypeTreeResolution);
        return ExplicitDonor(sourcePath).OriginalDump;
    }

    internal TypeTreeType Resolve(AssetsFile target, AssetFileInfo asset, string? sourcePath)
    {
        using var timing = PerformanceProfile.Measure(PerformanceProfile.TypeTreeResolution);
        TypeTreeType targetType = target.Metadata.TypeTreeTypes[asset.TypeIdOrIndex];
        if (target.Metadata.TypeTreeEnabled && HasTypeTree(targetType))
            return targetType;

        string className = ((AssetClassID)asset.TypeId).ToString();
        bool isScript = asset.TypeId == (int)AssetClassID.MonoBehaviour;
        if (!isScript && sourcePath is null)
            throw new InvalidDataException($"{className} replacement requires an embedded TypeTree or --typetree-source.");

        Key key = GetKey(target, targetType) ?? throw new InvalidDataException(isScript
            ? "TypeTree lookup requires a nonzero type hash and script hash."
            : "TypeTree lookup requires a nonzero type hash.");

        if (sourcePath is null)
        {
            if (discovery.Value.TryGetValue(key, out var matching))
            {
                TypeTreeType tree = matching.GetTree(className);
                PerformanceProfile.Current?.Describe($"Automatic TypeTree for {className}: {matching.First.Location}");
                return tree;
            }

            throw new InvalidDataException(
                $"{className} replacement requires an embedded TypeTree or --typetree-source. "
                + $"No compatible definition was found in {string.Join(", ", roots)} ({key}).");
        }

        Donor donor = ExplicitDonor(sourcePath);
        if (donor.Package is { } package)
        {
            if (isScript)
                throw new InvalidDataException("A class package supplies native TypeTrees. MonoBehaviour requires a matching embedded script TypeTree.");

            TypeTreeType? tree;
            lock (package)
            {
                var database = package.GetClassDatabase(target.Metadata.UnityVersion);
                tree = ClassDatabaseToTypeTree.Convert(database, targetType.TypeId);
            }
            if (tree is null || !tree.TypeHash.data.AsSpan().SequenceEqual(targetType.TypeHash.data))
                throw new InvalidDataException($"No {className} TypeTree in the class package matches the target's Unity version and type hash.");

            tree.TypeBlobIsDefinition = true;
            return tree;
        }

        Match? match = null;
        foreach (Definition candidate in donor.Definitions)
        {
            if (candidate.Key != key)
                continue;
            if (match is null)
                match = new Match(candidate);
            else
                match.Add(candidate);
        }
        return match?.GetTree(className) ?? throw new InvalidDataException(
            $"No embedded {className} TypeTree in the source matches the target's Unity version, file format, type hash and applicable script hash.");
    }

    private Donor ExplicitDonor(string sourcePath)
    {
        string path = Path.GetFullPath(sourcePath);
        return loaded.TryGetValue(path, out Donor? donor) ? donor
            : explicitDonors.GetOrAdd(path, CreateDonorReader).Value;
    }

    private static Lazy<Donor> CreateDonorReader(string path) =>
        new(() => ReadDonor(path, monoOnly: false));

    private Dictionary<Key, Match> ScanGame()
    {
        var index = new Dictionary<Key, Match>();
        var seen = new HashSet<string>(BatchCommand.PathComparer);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        int parsed = 0;
        foreach (string root in roots)
        {
            foreach (string original in Directory.EnumerateFiles(root, "*", options))
            {
                // The runner snapshots its inputs before importing, so discovery and replacement see the same bytes.
                string path = snapshots.GetValueOrDefault(original, original);
                if (path.EndsWith(".newuafgj.tmp", StringComparison.Ordinal) || !seen.Add(path))
                    continue;
                Donor donor;
                try
                {
                    donor = loaded.TryGetValue(path, out Donor? existing) ? existing
                        : explicitDonors.TryGetValue(path, out var requested) ? requested.Value
                        : ReadDonor(path, monoOnly: true);
                }
                catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
                {
                    throw new InvalidDataException($"Could not inspect TypeTree candidate '{original}': {error.Message}", error);
                }

                if (donor.Definitions.Count > 0)
                    parsed++;
                foreach (Definition definition in donor.Definitions)
                {
                    if (definition.Key.TypeId != (int)AssetClassID.MonoBehaviour)
                        continue;
                    if (index.TryGetValue(definition.Key, out var match))
                        match.Add(definition);
                    else
                        index.Add(definition.Key, new Match(definition));
                }
            }
        }
        PerformanceProfile.Current?.Describe($"TypeTree index built from {parsed} files with embedded definitions. {index.Count} MonoBehaviour keys.");
        return index;
    }

    private static Donor ReadDonor(string path, bool monoOnly)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[64];
        int length = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        if (signature[..length].StartsWith("UnityFS\0"u8))
        {
            using var input = new BundleInput(stream, measureLoading: false);
            List<Definition> definitions = [];
            foreach (var (index, file) in input.ReadSerializedEntries())
            {
                using var reader = file.Reader;
                AddDefinitions(definitions, file, $"{path}:{input.Bundle.GetFileName(index)}", monoOnly);
            }
            return new Donor(definitions);
        }

        if (signature[..length].StartsWith("TPK*"u8))
        {
            if (monoOnly)
                return new Donor([]);
            using var packageReader = new AssetsFileReader(stream);
            var package = new ClassPackageFile();
            package.Read(packageReader);
            return new Donor([], Package: package);
        }

        if (!monoOnly && UabeaDumpText.HasRoot(signature[..length], "MonoBehaviour Base"u8))
        {
            byte[] dump = new byte[checked((int)stream.Length)];
            stream.ReadExactly(dump);
            return new Donor([], OriginalDump: dump);
        }

        using var assetsReader = new AssetsFileReader(stream);
        if (AssetsFile.IsAssetsFile(assetsReader, 0, stream.Length))
        {
            var file = new AssetsFile();
            assetsReader.Position = 0;
            file.Read(assetsReader);
            AssetReplacement.CheckFileSize(file);
            List<Definition> definitions = [];
            AddDefinitions(definitions, file, path, monoOnly);
            return new Donor(definitions);
        }

        if (monoOnly)
            return new Donor([]);
        throw new InvalidDataException("The TypeTree source must be a Unity serialized file, UnityFS bundle or TPK class package.");
    }

    private static void AddDefinitions(List<Definition> definitions, AssetsFile file, string path, bool monoOnly)
    {
        if (!file.Metadata.TypeTreeEnabled)
            return;
        foreach (TypeTreeType candidate in file.Metadata.TypeTreeTypes)
        {
            if ((!monoOnly || candidate.TypeId == (int)AssetClassID.MonoBehaviour) &&
                HasTypeTree(candidate) && GetKey(file, candidate) is { } key)
            {
                definitions.Add(new Definition(key, candidate, path));
            }
        }
    }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static bool HasEmbedded(AssetsFile file, AssetFileInfo asset) =>
        file.Metadata.TypeTreeEnabled && HasTypeTree(file.Metadata.TypeTreeTypes[asset.TypeIdOrIndex]);

    private static bool HasTypeTree(TypeTreeType type) =>
        type.TypeBlobIsDefinition && type.TypeBlob?.Nodes is { Count: > 0 };

    private static Key? GetKey(AssetsFile file, TypeTreeType type)
    {
        bool isScript = type.TypeId == (int)AssetClassID.MonoBehaviour;
        if (!HasValidHash(type.TypeHash) || (isScript && !HasValidHash(type.ScriptIdHash)))
            return null;
        return new Key(file.Metadata.UnityVersion, file.Header.Version, type.TypeId,
            Convert.ToHexString(type.TypeHash.data), isScript ? Convert.ToHexString(type.ScriptIdHash.data) : "");
    }

    private static bool HasValidHash(Hash128 hash) => hash.data is { Length: 16 } && !hash.IsZero();

    private static bool SameDefinition(TypeTreeBlob first, TypeTreeBlob second)
    {
        if (first.Nodes.Count != second.Nodes.Count || !first.StringBufferBytes.AsSpan().SequenceEqual(second.StringBufferBytes))
            return false;
        for (int index = 0; index < first.Nodes.Count; index++)
        {
            var left = first.Nodes[index];
            var right = second.Nodes[index];
            if (left.Version != right.Version || left.Level != right.Level || left.TypeFlags != right.TypeFlags ||
                left.TypeStrOffset != right.TypeStrOffset || left.NameStrOffset != right.NameStrOffset ||
                left.ByteSize != right.ByteSize || left.Index != right.Index || left.MetaFlags != right.MetaFlags ||
                left.RefTypeHash != right.RefTypeHash)
            {
                return false;
            }
        }
        return true;
    }
}
