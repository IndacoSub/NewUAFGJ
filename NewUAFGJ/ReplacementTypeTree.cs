using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal static class ReplacementTypeTree
{
    public static TypeTreeType Resolve(AssetsFile target, AssetFileInfo asset, string? sourcePath)
    {
        TypeTreeType targetType = target.Metadata.TypeTreeTypes[asset.TypeIdOrIndex];
        if (target.Metadata.TypeTreeEnabled && HasTypeTree(targetType))
            return targetType;

        string className = ((AssetClassID)asset.TypeId).ToString();
        bool isScript = asset.TypeId == (int)AssetClassID.MonoBehaviour;
        if (sourcePath is null)
            throw new InvalidDataException($"{className} replacement requires an embedded TypeTree or --typetree-source.");

        if (!HasValidHash(targetType.TypeHash) || (isScript && !HasValidHash(targetType.ScriptIdHash)))
            throw new InvalidDataException(isScript
                ? "TypeTree lookup requires a nonzero type hash and script hash."
                : "TypeTree lookup requires a nonzero type hash.");

        using var reader = new AssetsFileReader(sourcePath);
        byte[] signature = reader.ReadBytes(8);
        reader.Position = 0;
        if (signature.AsSpan().StartsWith("TPK*"u8))
        {
            if (isScript)
                throw new InvalidDataException("A class package supplies native TypeTrees. MonoBehaviour requires a matching embedded script TypeTree.");

            var package = new ClassPackageFile();
            package.Read(reader);
            var database = package.GetClassDatabase(target.Metadata.UnityVersion);
            TypeTreeType? tree = ClassDatabaseToTypeTree.Convert(database, targetType.TypeId);
            if (tree is null || !tree.TypeHash.data.AsSpan().SequenceEqual(targetType.TypeHash.data))
                throw new InvalidDataException($"No {className} TypeTree in the class package matches the target's Unity version and type hash.");

            tree.TypeBlobIsDefinition = true;
            return tree;
        }

        TypeTreeType? match = null;
        byte[]? matchedTree = null;
        foreach (AssetsFile file in ReadFiles(reader, signature.AsSpan().SequenceEqual("UnityFS\0"u8)))
        {
            if (file.Header.Version != target.Header.Version ||
                file.Metadata.UnityVersion != target.Metadata.UnityVersion ||
                !file.Metadata.TypeTreeEnabled)
            {
                continue;
            }

            foreach (TypeTreeType candidate in file.Metadata.TypeTreeTypes)
            {
                if (candidate.TypeId != targetType.TypeId || !HasTypeTree(candidate) ||
                    !candidate.TypeHash.data.AsSpan().SequenceEqual(targetType.TypeHash.data) ||
                    (isScript && !candidate.ScriptIdHash.data.AsSpan().SequenceEqual(targetType.ScriptIdHash.data)))
                {
                    continue;
                }

                using var treeBytes = new MemoryStream();
                using var writer = new AssetsFileWriter(treeBytes);
                candidate.TypeBlob.Write(writer, file.Header.Version);
                byte[] tree = treeBytes.ToArray();
                if (matchedTree is not null && !tree.AsSpan().SequenceEqual(matchedTree))
                    throw new InvalidDataException($"The TypeTree source contains conflicting definitions for the target {className}.");

                matchedTree = tree;
                match = candidate;
            }
        }

        return match ?? throw new InvalidDataException(
            $"No embedded {className} TypeTree in the source matches the target's Unity version, file format, type hash and applicable script hash.");
    }

    private static bool HasTypeTree(TypeTreeType type) =>
        type.TypeBlobIsDefinition && type.TypeBlob?.Nodes is { Count: > 0 };

    private static bool HasValidHash(Hash128 hash) => hash.data is { Length: 16 } && !hash.IsZero();

    private static IEnumerable<AssetsFile> ReadFiles(AssetsFileReader reader, bool isBundle)
    {
        if (!isBundle)
        {
            if (!AssetsFile.IsAssetsFile(reader, 0, reader.BaseStream.Length))
                throw new InvalidDataException("The TypeTree source must be a Unity serialized file, UnityFS bundle or TPK class package.");

            reader.Position = 0;
            var file = new AssetsFile();
            file.Read(reader);
            AssetReplacement.CheckFileSize(file);
            yield return file;
            yield break;
        }

        var bundle = new AssetBundleFile();
        bundle.Read(reader);
        using var dataReader = bundle.DataReader;
        using var temporary = bundle.DataIsCompressed ? AssetReplacement.OpenTemporaryStream() : null;
        if (temporary is not null)
            bundle = BundleHelper.UnpackBundleToStream(bundle, temporary, freeOriginalStream: false);

        for (int index = 0; index < bundle.BlockAndDirInfo.DirectoryInfos.Count; index++)
        {
            if (bundle.IsAssetsFile(index))
            {
                AssetsFile file = BundleHelper.LoadAssetFromBundle(bundle, index);
                AssetReplacement.CheckFileSize(file);
                yield return file;
            }
        }
    }
}
