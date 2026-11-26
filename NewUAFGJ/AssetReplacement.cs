using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal static class AssetReplacement
{
    private sealed record Target(AssetsFile File, AssetFileInfo Asset, int? EntryIndex = null);

    private sealed record Replacement(AssetClassID ClassId, long? PathId, byte[] Payload,
        string? TypeTreeSource, (int Width, int Height)? AtlasSize, Stream? Video, bool SyncAtlasMaterials);
    private sealed record UpdatedAsset(byte[] ObjectData, ResourceUpdate? Resource = null,
        bool WriteSerializedEntry = true, IReadOnlyDictionary<long, byte[]>? Materials = null);
    private sealed record ResourceUpdate(int Index, string Name, Stream Bytes, long? Offset = null);

    public static AssetClassID Replace(string sourcePath, string replacementPath, long? requestedPathId,
        bool rawReplacement, string? typeTreeSource, bool resizeAtlas, bool syncAtlasMaterials)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        replacementPath = Path.GetFullPath(replacementPath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (sourcePath.Equals(replacementPath, pathComparison))
            throw new ArgumentException("Source and replacement must be different files.");
        if (File.GetAttributes(sourcePath).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Choose the source file directly instead of using a symbolic link.");

        using var input = File.OpenRead(replacementPath);
        Span<byte> prefix = stackalloc byte[8];
        int prefixLength = input.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        input.Position = 0;
        string extension = Path.GetExtension(replacementPath);
        bool isVideo = !rawReplacement && (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            Mp4Container.HasSignature(prefix[..prefixLength]));
        byte[] payload = isVideo ? [] : new byte[checked((int)input.Length)];
        input.ReadExactly(payload);

        AssetClassID classId = AssetClassID.TextAsset;
        if (isVideo)
        {
            Mp4Container.Validate(input);
            classId = AssetClassID.VideoClip;
        }
        else if (!rawReplacement)
        {
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                payload.AsSpan().StartsWith(TextureImage.PngSignature))
            {
                classId = AssetClassID.Texture2D;
            }
            else if (DetectStructuredDump(payload) is { } structuredClass)
            {
                if (requestedPathId is null)
                    throw new InvalidDataException($"Specify a PathID for a {structuredClass} dump.");

                classId = structuredClass;
            }
            else
            {
                RejectUnsupportedReplacement(payload, extension);
                payload = UabeaTextAssetDump.DecodeIfPresent(payload);
            }
        }

        if (typeTreeSource is not null && classId == AssetClassID.TextAsset)
            throw new ArgumentException("--typetree-source requires a structured dump, PNG, or MP4.");
        if (resizeAtlas && classId != AssetClassID.Texture2D)
            throw new ArgumentException("--resize-atlas requires a PNG.");

        var replacement = new Replacement(classId, requestedPathId, payload, typeTreeSource,
            resizeAtlas ? TextureImage.ReadPngDimensions(payload) : null, isVideo ? input : null, syncAtlasMaterials);

        string stagingPath = sourcePath + ".newuafgj.tmp";
        // Reserve the sibling path before opening the source, so two CLI processes cannot stage the same file.
        var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        bool committed = false;
        try
        {
            using (output)
            {
                using (var source = File.OpenRead(sourcePath))
                    Rewrite(source, output, replacement);

                output.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stagingPath, File.GetUnixFileMode(sourcePath));

            File.Move(stagingPath, sourcePath, overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed)
                File.Delete(stagingPath);
        }

        return classId;
    }

    private static AssetClassID? DetectStructuredDump(byte[] payload)
    {
        if (UabeaDumpText.HasRoot(payload, "MonoBehaviour Base"u8))
            return AssetClassID.MonoBehaviour;

        if (UabeaDumpText.HasRoot(payload, "Sprite Base"u8))
            return AssetClassID.Sprite;

        if (UabeaDumpText.HasRoot(payload, "RectTransform Base"u8))
            return AssetClassID.RectTransform;

        if (UabeaDumpText.HasRoot(payload, "Font Base"u8))
            return AssetClassID.Font;

        if (UabeaDumpText.HasRoot(payload, "GameObject Base"u8))
            return AssetClassID.GameObject;

        if (UabeaDumpText.HasRoot(payload, "Material Base"u8))
            return AssetClassID.Material;

        return null;
    }

    private static void RejectUnsupportedReplacement(ReadOnlySpan<byte> payload, string extension)
    {
        var root = UabeaDumpText.GetRoot(payload);
        if (root.Length > 5 && root.EndsWith(" Base"u8) && !root[..^5].Contains((byte)' ') &&
            !root.SequenceEqual("TextAsset Base"u8))
        {
            string type = UabeaDumpText.Utf8.GetString(root[..^5]);
            throw new InvalidDataException($"UABEA dumps of {type} are unsupported. Use a PNG for Texture2D, an MP4 for VideoClip or --raw for TextAsset bytes.");
        }

        ReadOnlySpan<byte> jpegSignature = [0xff, 0xd8, 0xff];
        if (extension.ToLowerInvariant() is ".dds" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".tga" or ".tif" or ".tiff" ||
            payload.StartsWith("DDS "u8) || payload.StartsWith(jpegSignature))
        {
            throw new InvalidDataException("Unsupported replacement image. Use a PNG for Texture2D or --raw for TextAsset bytes.");
        }
    }

    private static void Rewrite(FileStream source, FileStream output, Replacement replacement)
    {
        Span<byte> signature = stackalloc byte[8];
        int signatureLength = source.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        source.Position = 0;
        var magic = signature[..signatureLength];
        if (magic.SequenceEqual("UnityFS\0"u8))
        {
            RewriteBundle(source, output, replacement);
            return;
        }

        if (magic.SequenceEqual("UnityRaw"u8) || magic.SequenceEqual("UnityWeb"u8))
            throw new InvalidDataException($"Bundle format {System.Text.Encoding.ASCII.GetString(magic)} is unsupported. Use a UnityFS bundle.");
        ReadOnlySpan<byte> lz4Frame = [0x04, 0x22, 0x4d, 0x18];
        if (magic.StartsWith(lz4Frame))
            throw new InvalidDataException("An LZ4 frame wraps this input. Decompress it first. LZ4 compression inside UnityFS bundles is supported.");
        if (magic.StartsWith("PK\u0003\u0004"u8))
            throw new InvalidDataException("This input is a ZIP archive. Extract the Unity serialized file or bundle first.");

        if (replacement.Video is { } video)
        {
            if (!Mp4Container.HasSignature(signature[..signatureLength]))
                throw new InvalidDataException("Video replacement requires a UnityFS bundle or a standalone MP4.");
            if (replacement.PathId is not null || replacement.TypeTreeSource is not null)
                throw new ArgumentException("A standalone MP4 has no PathID or TypeTree.");

            Mp4Container.Validate(source);
            video.CopyTo(output);
            if (output.Length != video.Length)
                throw new InvalidDataException("The staged MP4 has an unexpected length.");

            output.Position = 0;
            VerifyBytes(output, video, "MP4");
            return;
        }

        using var reader = new AssetsFileReader(source);
        if (!AssetsFile.IsAssetsFile(reader, 0, source.Length))
            throw new InvalidDataException("Expected a Unity serialized file or a UnityFS bundle.");

        var file = new AssetsFile();
        reader.Position = 0;
        file.Read(reader);
        CheckFileSize(file);
        Target target = SelectTarget(FindTargets(file, replacement), replacement);
        UpdatedAsset expected = UpdateAsset(target, replacement);

        var writer = new AssetsFileWriter(output);
        file.Write(writer);
        writer.Flush();

        output.Position = 0;
        var staged = new AssetsFile();
        staged.Read(new AssetsFileReader(output));
        Verify(staged, target.Asset.PathId, replacement, expected);
    }

    private static void RewriteBundle(FileStream source, FileStream output, Replacement replacement)
    {
        using var originalReader = new AssetsFileReader(source);
        var header = new AssetBundleHeader();
        header.Read(originalReader);
        if (header.GetCompressionType() > 3)
            throw new InvalidDataException($"Unsupported UnityFS directory compression ({header.GetCompressionType()}).");

        var bundle = new AssetBundleFile();
        bundle.Read(originalReader);
        using var dataReader = bundle.DataReader;
        foreach (var block in bundle.BlockAndDirInfo.BlockInfos)
        {
            if (block.GetCompressionType() > 3)
                throw new InvalidDataException($"Unsupported UnityFS block compression ({block.GetCompressionType()}).");
        }
        AssetBundleCompressionType compression = bundle.GetCompressionType();

        // LZ4 entries are seekable through the library. LZMA needs a fully decompressed stream.
        using var unpacked = bundle.DataIsCompressed ? OpenTemporaryStream() : null;
        if (unpacked is not null)
            bundle = BundleHelper.UnpackBundleToStream(bundle, unpacked, freeOriginalStream: false);

        Target target = SelectTarget(FindBundleTargets(bundle, replacement), replacement);
        int entryIndex = target.EntryIndex!.Value;
        UpdatedAsset expected = UpdateAsset(target, replacement, bundle);

        using var serializedEntry = expected.WriteSerializedEntry ? OpenTemporaryStream() : null;
        if (serializedEntry is not null)
        {
            var serializedWriter = new AssetsFileWriter(serializedEntry);
            target.File.Write(serializedWriter);
            serializedWriter.Flush();
            bundle.BlockAndDirInfo.DirectoryInfos[entryIndex].Replacer = new ContentReplacerFromStream(serializedEntry);
        }

        using var patchedResource = expected.Resource?.Offset is not null ? OpenTemporaryStream() : null;
        if (expected.Resource is { } resource)
        {
            Stream resourceData = resource.Bytes;
            if (patchedResource is not null)
            {
                using var originalEntry = OpenResource(bundle, resource);
                originalEntry.CopyTo(patchedResource);
                if (patchedResource.Length != originalEntry.Length)
                    throw new InvalidDataException("The streamed texture resource is truncated.");

                patchedResource.Position = resource.Offset!.Value;
                resourceData.CopyTo(patchedResource);
                resourceData = patchedResource;
            }
            bundle.BlockAndDirInfo.DirectoryInfos[resource.Index].Replacer = new ContentReplacerFromStream(resourceData);
        }

        using var uncompressedOutput = compression != AssetBundleCompressionType.None ? OpenTemporaryStream() : null;
        var bundleWriter = new AssetsFileWriter(uncompressedOutput ?? output);
        bundle.Write(bundleWriter);
        bundleWriter.Flush();

        if (uncompressedOutput is not null)
        {
            using var packReader = new AssetsFileReader(uncompressedOutput);
            var toPack = new AssetBundleFile();
            toPack.Read(packReader);
            using var unpackedData = toPack.DataReader;
            var packWriter = new AssetsFileWriter(output);
            toPack.Pack(packWriter, compression);
            packWriter.Flush();
        }

        var staged = new AssetBundleFile();
        staged.Read(new AssetsFileReader(output));
        using var stagedData = staged.DataReader;
        using var verificationStream = staged.DataIsCompressed ? OpenTemporaryStream() : null;
        if (verificationStream is not null)
            staged = BundleHelper.UnpackBundleToStream(staged, verificationStream, freeOriginalStream: false);

        if (staged.GetFileName(entryIndex) != bundle.GetFileName(entryIndex))
            throw new InvalidDataException("The staged bundle does not contain the expected entry.");

        AssetsFile stagedFile = BundleHelper.LoadAssetFromBundle(staged, entryIndex);
        Verify(stagedFile, target.Asset.PathId, replacement, expected);

        if (expected.Resource is { } updatedResource)
        {
            using var resourceStream = OpenResource(staged, updatedResource);
            if (updatedResource.Offset is null && resourceStream.Length != updatedResource.Bytes.Length)
                throw new InvalidDataException("The video resource has an unexpected length.");

            resourceStream.Position = updatedResource.Offset ?? 0;
            VerifyBytes(resourceStream, updatedResource.Bytes, "resource");
        }
    }

    private static IEnumerable<Target> FindBundleTargets(AssetBundleFile bundle, Replacement replacement)
    {
        for (int index = 0; index < bundle.BlockAndDirInfo.DirectoryInfos.Count; index++)
        {
            if (bundle.IsAssetsFile(index))
            {
                AssetsFile file = BundleHelper.LoadAssetFromBundle(bundle, index);
                CheckFileSize(file);
                foreach (Target target in FindTargets(file, replacement, index))
                    yield return target;
            }
        }
    }

    internal static void CheckFileSize(AssetsFile file)
    {
        long actualSize = file.Reader.BaseStream.Length;
        if (file.Header.FileSize > actualSize)
            throw new InvalidDataException($"The Unity serialized file is truncated. Its header declares {file.Header.FileSize} bytes, but only {actualSize} are present.");
    }

    private static IEnumerable<Target> FindTargets(AssetsFile file, Replacement replacement, int? entryIndex = null)
    {
        return file.GetAssetsOfType(replacement.ClassId)
            .Where(asset => replacement.PathId is null || asset.PathId == replacement.PathId)
            .Select(asset => new Target(file, asset, entryIndex));
    }

    private static Target SelectTarget(IEnumerable<Target> targets, Replacement replacement)
    {
        var matches = targets.Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException(replacement.PathId is null
                ? $"No {replacement.ClassId} was found."
                : $"No {replacement.ClassId} has PathID {replacement.PathId}."),
            _ => throw new InvalidDataException(replacement.PathId is null
                ? $"Multiple {replacement.ClassId}s were found. Specify a PathID."
                : $"PathID {replacement.PathId} identifies multiple {replacement.ClassId}s in the bundle.")
        };
    }

    private static UpdatedAsset UpdateAsset(Target target, Replacement replacement, AssetBundleFile? bundle = null)
    {
        if (target.File.Header.Version is < 17 or > 22)
            throw new InvalidDataException($"Serialized file format {target.File.Header.Version} is unsupported for {replacement.ClassId}. Expected versions 17–22.");

        if (replacement.Video is { } video)
            return ReplaceVideoClip(bundle ?? throw new InvalidDataException("Expected a UnityFS video bundle."),
                target, replacement, video);

        byte[] expected;
        Dictionary<long, byte[]>? materials = null;
        if (replacement.ClassId == AssetClassID.Texture2D)
        {
            var texture = new TextureReplacement(target.File, target.Asset, replacement.TypeTreeSource, replacement.AtlasSize);
            if (replacement.SyncAtlasMaterials && replacement.AtlasSize is { } size)
                materials = AtlasMaterialSync.Apply(target.File, target.Asset.PathId, size, replacement.TypeTreeSource);

            if (bundle is not null && !texture.DimensionsChanged && texture.Stream is { } location)
            {
                int index = FindResource(bundle, target.EntryIndex!.Value, location.Path);
                var pixels = new MemoryStream(texture.Encode(replacement.Payload), writable: false);
                var patch = new ResourceUpdate(index, bundle.GetFileName(index), pixels, location.Offset);
                return new UpdatedAsset(SerializedObject.ReadBytes(target.File, target.Asset), patch,
                    WriteSerializedEntry: materials is { Count: > 0 }, Materials: materials);
            }

            expected = texture.ReplaceInline(replacement.Payload, target.File.Header.Endianness);
        }
        else
        {
            expected = replacement.ClassId == AssetClassID.TextAsset
                ? TextAssetReplacement.Build(target.File, target.Asset, replacement.Payload)
                : StructuredAssetDump.Import(target.File, target.Asset, replacement.Payload, replacement.TypeTreeSource);
        }

        target.Asset.SetNewData(expected);
        return new UpdatedAsset(expected, Materials: materials);
    }

    private static UpdatedAsset ReplaceVideoClip(AssetBundleFile bundle, Target target, Replacement replacement, Stream video)
    {
        if (FindBundleTargets(bundle, replacement with { PathId = null }).Skip(1).Any())
            throw new InvalidDataException("Video replacement requires a bundle containing exactly one VideoClip.");

        var clip = new VideoClipReplacement(target.File, target.Asset, replacement.TypeTreeSource);
        int index = FindResource(bundle, target.EntryIndex!.Value, clip.ResourcePath);
        var resource = new ResourceUpdate(index, bundle.GetFileName(index), video);
        using var original = OpenResource(bundle, resource);
        if (clip.Offset != 0 || clip.Size != original.Length)
            throw new InvalidDataException("The VideoClip must occupy the entire bundle resource entry.");

        Mp4Container.Validate(original);
        byte[] updated = clip.ResizeResource(video.Length, target.File.Header.Endianness);
        target.Asset.SetNewData(updated);
        return new UpdatedAsset(updated, resource);
    }

    private static int FindResource(AssetBundleFile bundle, int assetIndex, string path)
    {
        const string archivePrefix = "archive:/";
        string name = path;
        if (path.StartsWith(archivePrefix, StringComparison.Ordinal))
        {
            string entryPrefix = archivePrefix + bundle.GetFileName(assetIndex) + "/";
            if (!path.StartsWith(entryPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("The resource is outside the selected archive entry.");

            name = path[entryPrefix.Length..];
        }

        if (name.Length == 0 || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("Expected a resource in the same UnityFS bundle.");

        var entries = bundle.BlockAndDirInfo.DirectoryInfos;
        var matches = Enumerable.Range(0, entries.Count)
            .Where(i => string.Equals(entries[i].Name, name, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length != 1 || entries[matches[0]].IsSerialized || bundle.IsAssetsFile(matches[0]))
            throw new InvalidDataException($"Expected one resource entry named '{name}' in the UnityFS bundle.");

        return matches[0];
    }

    private static SegmentStream OpenResource(AssetBundleFile bundle, ResourceUpdate patch)
    {
        if (bundle.GetFileName(patch.Index) != patch.Name)
            throw new InvalidDataException("The expected resource is missing.");

        bundle.GetFileRange(patch.Index, out long offset, out long length);
        if (patch.Offset is { } start && (start < 0 || start > length - patch.Bytes.Length))
            throw new InvalidDataException("The texture stream range is outside its resource entry.");

        return new SegmentStream(bundle.DataReader.BaseStream, offset, length);
    }

    private static void VerifyBytes(Stream staged, Stream source, string description)
    {
        source.Position = 0;
        Span<byte> expected = stackalloc byte[4096];
        Span<byte> actual = stackalloc byte[4096];
        int length;
        while ((length = source.ReadAtLeast(expected, expected.Length, throwOnEndOfStream: false)) > 0)
        {
            staged.ReadExactly(actual[..length]);
            if (!actual[..length].SequenceEqual(expected[..length]))
                throw new InvalidDataException($"The staged {description} does not match the requested replacement.");
        }
    }

    private static void Verify(AssetsFile staged, long pathId, Replacement replacement, UpdatedAsset expected)
    {
        var stagedSelection = replacement with { PathId = pathId };
        Target target = SelectTarget(FindTargets(staged, stagedSelection), stagedSelection);
        if (!SerializedObject.ReadBytes(staged, target.Asset).AsSpan().SequenceEqual(expected.ObjectData))
            throw new InvalidDataException($"The staged {replacement.ClassId} does not match the requested replacement.");

        if (expected.Materials is null)
            return;

        foreach (var (materialId, data) in expected.Materials)
        {
            var material = staged.GetAssetInfo(materialId);
            if (material is null || material.TypeId != (int)AssetClassID.Material ||
                !SerializedObject.ReadBytes(staged, material).AsSpan().SequenceEqual(data))
            {
                throw new InvalidDataException($"The staged Material {materialId} does not match the atlas dimensions.");
            }
        }
    }

    internal static FileStream OpenTemporaryStream()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 4096, FileOptions.DeleteOnClose);
    }
}
