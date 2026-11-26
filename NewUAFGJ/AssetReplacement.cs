using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal static class AssetReplacement
{
    private const int StandaloneEntry = -1;
    private sealed record Target(AssetsFile File, AssetFileInfo Asset, int EntryIndex);
    private sealed record PreparedReplacement(AssetClassID ClassId, byte[] Payload, Stream? Video,
        (int Width, int Height)? AtlasSize) : IDisposable
    {
        public void Dispose() => Video?.Dispose();
    }

    internal sealed record ExpectedObject(AssetClassID ClassId, byte[] Data);

    internal static AssetClassID Replace(string source, ReplacementOperation operation, ReplacementTypeTree resolver) =>
        ReplaceBatch(source, source, [operation], new ReplacementSession(resolver), TimeSpan.Zero)[0];

    internal static AssetClassID[] ReplaceBatch(string sourcePath, string outputPath, ReplacementOperation[] operations,
        ReplacementSession session, TimeSpan cooldown, Action<int, ReplacementOperation>? progress = null,
        bool deferCompression = false)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        outputPath = Path.GetFullPath(outputPath);
        var classes = new AssetClassID[operations.Length];
        Publish(sourcePath, outputPath, (source, output) =>
            Rewrite(source, output, operations, session, cooldown, classes, progress, deferCompression));
        return classes;
    }

    internal static void MatchBundleCompression(string sourcePath, string referencePath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        using var reference = File.OpenRead(referencePath);
        using var referenceBundle = new BundleInput(reference, unpackData: false);
        AssetBundleCompressionType compression = referenceBundle.Compression;
        using (var probe = File.OpenRead(sourcePath))
        using (var probeBundle = new BundleInput(probe, unpackData: false))
        {
            if (probeBundle.Compression == compression)
                return;
        }

        Publish(sourcePath, sourcePath, (source, output) =>
        {
            using var input = new BundleInput(source);
            PackBundle(input.Bundle, output, compression);
            PerformanceProfile.Current?.Describe($"UnityFS {input.Compression} → {compression}: {source.Length:N0} input bytes, {output.Length:N0} output bytes.");

            using var validation = PerformanceProfile.Measure(PerformanceProfile.Validation);
            using var staged = new BundleInput(output, measureLoading: false);
            if (staged.Compression != compression ||
                !DirectoryLayout(input.Bundle).SequenceEqual(DirectoryLayout(staged.Bundle)))
            {
                throw new InvalidDataException("The repacked bundle has unexpected compression or directory metadata.");
            }

            Stream originalData = input.Bundle.DataReader.BaseStream;
            Stream stagedData = staged.Bundle.DataReader.BaseStream;
            if (stagedData.Length != originalData.Length)
                throw new InvalidDataException("The repacked bundle has an unexpected data length.");
            stagedData.Position = 0;
            using var original = new SegmentStream(originalData, 0, originalData.Length);
            VerifyBytes(stagedData, original, "repacked bundle data");
        });
    }

    private static IEnumerable<(string Name, uint Flags, long Offset, long Length)> DirectoryLayout(AssetBundleFile bundle) =>
        bundle.BlockAndDirInfo.DirectoryInfos.Select(entry => (entry.Name, entry.Flags, entry.Offset, entry.DecompressedSize));

    private static void Publish(string sourcePath, string outputPath, Action<FileStream, FileStream> rewrite)
    {
        bool inPlace = BatchCommand.PathComparer.Equals(sourcePath, outputPath);
        if (File.GetAttributes(sourcePath).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Choose the source file directly instead of using a symbolic link.");
        if (!inPlace && File.Exists(outputPath))
            throw new IOException($"The batch output already exists: '{outputPath}'.");

        string stagingPath = outputPath + ".newuafgj.tmp";
        // Keep the staging file exclusive until the validated output is ready to publish.
        var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        bool committed = false;
        try
        {
            using (output)
            {
                using (var source = File.OpenRead(sourcePath))
                    rewrite(source, output);

                output.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stagingPath, File.GetUnixFileMode(sourcePath));

            File.Move(stagingPath, outputPath, overwrite: inPlace);
            committed = true;
        }
        finally
        {
            if (!committed)
                File.Delete(stagingPath);
        }
    }

    private static PreparedReplacement LoadReplacement(string sourcePath, ReplacementOperation operation)
    {
        using var timing = PerformanceProfile.Measure(PerformanceProfile.PayloadLoading);
        operation.Validate();
        string replacementPath = Path.GetFullPath(operation.Replacement);
        if (BatchCommand.PathComparer.Equals(sourcePath, replacementPath))
            throw new ArgumentException("Source and replacement must be different files.");

        var input = File.OpenRead(replacementPath);
        bool handOffStream = false;
        try
        {
            Span<byte> prefix = stackalloc byte[8];
            int prefixLength = input.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
            input.Position = 0;
            string extension = Path.GetExtension(replacementPath);
            bool isVideo = !operation.Raw && (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                Mp4Container.HasSignature(prefix[..prefixLength]));
            byte[] payload = isVideo ? [] : new byte[checked((int)input.Length)];
            input.ReadExactly(payload);

            AssetClassID classId = AssetClassID.TextAsset;
            if (isVideo)
            {
                Mp4Container.Validate(input);
                classId = AssetClassID.VideoClip;
            }
            else if (!operation.Raw)
            {
                if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                    payload.AsSpan().StartsWith(TextureImage.PngSignature))
                {
                    classId = AssetClassID.Texture2D;
                }
                else if (DetectStructuredDump(payload) is { } structuredClass)
                {
                    if (operation.PathId is null)
                        throw new InvalidDataException($"Specify a PathID for a {structuredClass} dump.");
                    classId = structuredClass;
                }
                else
                {
                    RejectUnsupportedReplacement(payload, extension);
                    payload = UabeaTextAssetDump.DecodeIfPresent(payload);
                }
            }

            if (operation.TypeTreeSource is not null && classId == AssetClassID.TextAsset)
                throw new ArgumentException("--typetree-source requires a structured dump, PNG, or MP4.");
            if (operation.ResizeAtlas && classId != AssetClassID.Texture2D)
                throw new ArgumentException("--resize-atlas requires a PNG.");

            var replacement = new PreparedReplacement(classId, payload, isVideo ? input : null,
                operation.ResizeAtlas ? TextureImage.ReadPngDimensions(payload) : null);
            handOffStream = isVideo;
            return replacement;
        }
        finally
        {
            if (!handOffStream)
                input.Dispose();
        }
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

    private static void Rewrite(FileStream source, FileStream output, ReplacementOperation[] operations,
        ReplacementSession session, TimeSpan cooldown, AssetClassID[] classes,
        Action<int, ReplacementOperation>? progress, bool deferCompression)
    {
        Span<byte> signature = stackalloc byte[8];
        int signatureLength = source.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        source.Position = 0;
        var magic = signature[..signatureLength];
        if (magic.SequenceEqual("UnityFS\0"u8))
        {
            RewriteBundle(source, output, operations, session, cooldown, classes, progress, deferCompression);
            return;
        }

        if (deferCompression)
            throw new ArgumentException("Deferring compression requires a UnityFS bundle.");

        if (magic.SequenceEqual("UnityRaw"u8) || magic.SequenceEqual("UnityWeb"u8))
            throw new InvalidDataException($"Bundle format {System.Text.Encoding.ASCII.GetString(magic)} is unsupported. Use a UnityFS bundle.");
        ReadOnlySpan<byte> lz4Frame = [0x04, 0x22, 0x4d, 0x18];
        if (magic.StartsWith(lz4Frame))
            throw new InvalidDataException("An LZ4 frame wraps this input. Decompress it first. LZ4 compression inside UnityFS bundles is supported.");
        if (magic.StartsWith("PK\u0003\u0004"u8))
            throw new InvalidDataException("This input is a ZIP archive. Extract the Unity serialized file or bundle first.");

        if (Mp4Container.HasSignature(magic))
        {
            Mp4Container.Validate(source);
            for (int index = 0; index < operations.Length; index++)
            {
                WaitBetweenOperations(index, cooldown);
                var operation = operations[index];
                progress?.Invoke(index, operation);
                try
                {
                    using var applying = PerformanceProfile.Measure(PerformanceProfile.Applying);
                    using var replacement = LoadReplacement(source.Name, operation);
                    if (replacement.Video is not { } video)
                        throw new InvalidDataException("A standalone MP4 requires an MP4 replacement.");
                    if (operation.PathId is not null || operation.TypeTreeSource is not null || operation.Entry is not null)
                        throw new ArgumentException("A standalone MP4 has no PathID, TypeTree, or serialized entry.");

                    // Earlier MP4 replacements were fully validated. Only the final video survives.
                    if (index == operations.Length - 1)
                    {
                        using (PerformanceProfile.Measure(PerformanceProfile.AssetSerialization))
                            video.CopyTo(output);
                        using var validation = PerformanceProfile.Measure(PerformanceProfile.Validation);
                        if (output.Length != video.Length)
                            throw new InvalidDataException("The staged MP4 has an unexpected length.");
                        output.Position = 0;
                        VerifyBytes(output, video, "MP4");
                    }
                    classes[index] = replacement.ClassId;
                }
                catch (Exception error) when (progress is not null)
                {
                    throw OperationError(operation, error);
                }
            }
            return;
        }

        using var reader = new AssetsFileReader(source);
        AssetsFile file;
        using (PerformanceProfile.Measure(PerformanceProfile.BundleLoading))
        {
            if (!AssetsFile.IsAssetsFile(reader, 0, source.Length))
                throw new InvalidDataException("Expected a Unity serialized file or a UnityFS bundle.");
            reader.Position = 0;
            file = new AssetsFile();
            file.Read(reader);
            CheckFileSize(file);
        }

        var files = new Dictionary<int, AssetsFile> { [StandaloneEntry] = file };
        session.TypeTrees.RegisterLoaded(source.Name, [(file, source.Name)]);
        var expected = Apply(source.Name, files, operations, session, cooldown, classes, progress);
        using (PerformanceProfile.Measure(PerformanceProfile.AssetSerialization))
        {
            var writer = new AssetsFileWriter(output);
            file.Write(writer);
            writer.Flush();
        }

        using var verification = PerformanceProfile.Measure(PerformanceProfile.Validation);
        output.Position = 0;
        var staged = new AssetsFile();
        staged.Read(new AssetsFileReader(output));
        CheckFileSize(staged);
        VerifyObjects(staged, expected[StandaloneEntry]);
    }

    private static void RewriteBundle(FileStream source, FileStream output, ReplacementOperation[] operations,
        ReplacementSession session, TimeSpan cooldown, AssetClassID[] classes,
        Action<int, ReplacementOperation>? progress, bool deferCompression)
    {
        using var input = new BundleInput(source);
        AssetBundleFile bundle = input.Bundle;
        Dictionary<int, AssetsFile> files;
        using (PerformanceProfile.Measure(PerformanceProfile.BundleLoading))
            files = input.ReadSerializedEntries();
        session.TypeTrees.RegisterLoaded(source.Name, files.Select(pair =>
            (pair.Value, $"{source.Name}:{bundle.GetFileName(pair.Key)}")));

        using var resources = new BundleResources(bundle, files);
        var expected = Apply(source.Name, files, operations, session, cooldown, classes, progress, resources);
        var compression = deferCompression ? AssetBundleCompressionType.None : input.Compression;
        var serialized = new List<FileStream>();
        try
        {
            using (PerformanceProfile.Measure(PerformanceProfile.AssetSerialization))
            {
                foreach (int index in expected.Keys)
                {
                    AssetsFile file = files[index];
                    if (!file.AssetInfos.Any(asset => asset.Replacer is not null))
                        continue;
                    var stream = OpenTemporaryStream();
                    serialized.Add(stream);
                    var writer = new AssetsFileWriter(stream);
                    file.Write(writer);
                    writer.Flush();
                    bundle.BlockAndDirInfo.DirectoryInfos[index].Replacer = new ContentReplacerFromStream(stream);
                }
            }
            resources.Stage();
            WriteBundle(bundle, output, compression);
        }
        finally
        {
            foreach (FileStream stream in serialized)
                stream.Dispose();
        }

        PerformanceProfile.Current?.Describe($"UnityFS {input.Compression} → {compression}: {source.Length:N0} input bytes, {output.Length:N0} output bytes.");
        using var validation = PerformanceProfile.Measure(PerformanceProfile.Validation);
        output.Position = 0;
        using var staged = new BundleInput(output, measureLoading: false);
        if (staged.Compression != compression)
            throw new InvalidDataException($"The staged bundle uses {staged.Compression} instead of {compression} compression.");
        foreach (var (index, objects) in expected)
        {
            if (staged.Bundle.GetFileName(index) != bundle.GetFileName(index))
                throw new InvalidDataException("The staged bundle does not contain the expected entry.");
            AssetsFile stagedFile = BundleHelper.LoadAssetFromBundle(staged.Bundle, index);
            using var reader = stagedFile.Reader;
            CheckFileSize(stagedFile);
            VerifyObjects(stagedFile, objects);
        }
        resources.Verify(staged.Bundle);
    }

    private static void WriteBundle(AssetBundleFile bundle, Stream output, AssetBundleCompressionType compression)
    {
        using var uncompressed = compression != AssetBundleCompressionType.None ? OpenTemporaryStream() : null;
        using (PerformanceProfile.Measure(PerformanceProfile.BundleWriting))
        {
            var writer = new AssetsFileWriter(uncompressed ?? output);
            bundle.Write(writer);
            writer.Flush();
        }

        if (uncompressed is not null)
        {
            using var reader = new AssetsFileReader(uncompressed);
            var toPack = new AssetBundleFile();
            toPack.Read(reader);
            using var data = toPack.DataReader;
            PackBundle(toPack, output, compression);
        }
    }

    private static void PackBundle(AssetBundleFile bundle, Stream output, AssetBundleCompressionType compression)
    {
        using var timing = PerformanceProfile.Measure(PerformanceProfile.BundleCompression);
        var writer = new AssetsFileWriter(output);
        bundle.Pack(writer, compression);
        writer.Flush();
    }

    private static Dictionary<int, Dictionary<long, ExpectedObject>> Apply(string source,
        Dictionary<int, AssetsFile> files, ReplacementOperation[] operations, ReplacementSession session,
        TimeSpan cooldown, AssetClassID[] classes, Action<int, ReplacementOperation>? progress,
        BundleResources? resources = null)
    {
        var expected = new Dictionary<int, Dictionary<long, ExpectedObject>>();
        for (int index = 0; index < operations.Length; index++)
        {
            WaitBetweenOperations(index, cooldown);
            var operation = operations[index];
            progress?.Invoke(index, operation);
            try
            {
                using var applying = PerformanceProfile.Measure(PerformanceProfile.Applying);
                using var replacement = LoadReplacement(source, operation);
                if (operation.Entry is not null && resources is null)
                    throw new ArgumentException("A serialized entry can only be selected in a UnityFS bundle.");
                if (replacement.Video is not null && resources is null)
                    throw new InvalidDataException("Video replacement requires a UnityFS bundle or a standalone MP4.");

                Target target;
                using (PerformanceProfile.Measure(PerformanceProfile.PathIdSearch))
                {
                    target = SelectTarget(FindTargets(files, replacement.ClassId, operation.PathId,
                        operation.Entry, resources?.Bundle), replacement.ClassId, operation.PathId);
                }
                if (!expected.TryGetValue(target.EntryIndex, out var objects))
                    expected.Add(target.EntryIndex, objects = new Dictionary<long, ExpectedObject>());
                UpdateAsset(target, operation, replacement, session, resources, files, objects);
                classes[index] = replacement.ClassId;
            }
            catch (Exception error) when (progress is not null)
            {
                throw OperationError(operation, error);
            }
        }
        return expected;
    }

    private static void WaitBetweenOperations(int index, TimeSpan cooldown)
    {
        if (index == 0 || cooldown == TimeSpan.Zero)
            return;
        using var timing = PerformanceProfile.Measure(PerformanceProfile.Cooldown);
        Thread.Sleep(cooldown);
    }

    private static InvalidDataException OperationError(ReplacementOperation operation, Exception error)
    {
        string fileId = operation.FileId is not (null or "-") ? $", FileID {operation.FileId}" : "";
        return new InvalidDataException(
            $"{operation.Context ?? "Replacement"} [PathID {operation.PathId?.ToString() ?? "auto"}{fileId}] "
            + $"← {operation.Replacement}{Environment.NewLine}{error.Message}", error);
    }

    internal static void CheckFileSize(AssetsFile file)
    {
        long actualSize = file.Reader.BaseStream.Length;
        if (file.Header.FileSize > actualSize)
            throw new InvalidDataException($"The Unity serialized file is truncated. Its header declares {file.Header.FileSize} bytes, but only {actualSize} are present.");
    }

    private static IEnumerable<Target> FindTargets(Dictionary<int, AssetsFile> files, AssetClassID classId,
        long? pathId, string? entry = null, AssetBundleFile? bundle = null)
    {
        foreach (var (index, file) in files)
        {
            if (entry is not null && bundle!.GetFileName(index) != entry)
                continue;
            foreach (AssetFileInfo asset in file.AssetInfos)
            {
                if (asset.TypeId == (int)classId && (pathId is null || asset.PathId == pathId))
                    yield return new Target(file, asset, index);
            }
        }
    }

    private static Target SelectTarget(IEnumerable<Target> targets, AssetClassID classId, long? pathId)
    {
        var matches = targets.Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException(pathId is null
                ? $"No {classId} was found."
                : $"No {classId} has PathID {pathId}."),
            _ => throw new InvalidDataException(pathId is null
                ? $"Multiple {classId}s were found. Specify a PathID."
                : $"PathID {pathId} identifies multiple {classId}s in the bundle.")
        };
    }

    private static void UpdateAsset(Target target, ReplacementOperation operation, PreparedReplacement replacement,
        ReplacementSession session, BundleResources? resources, Dictionary<int, AssetsFile> files,
        Dictionary<long, ExpectedObject> expected)
    {
        AssetsFile file = target.File;
        AssetFileInfo asset = target.Asset;
        AssetClassID classId = replacement.ClassId;
        if (file.Header.Version is < 17 or > 22)
            throw new InvalidDataException($"Serialized file format {file.Header.Version} is unsupported for {classId}. Expected versions 17–22.");

        byte[] data;
        if (replacement.Video is { } video)
        {
            if (FindTargets(files, classId, null).Skip(1).Any())
                throw new InvalidDataException("Video replacement requires a bundle containing exactly one VideoClip.");
            var clip = new VideoClipReplacement(file, asset, session.TypeTrees, operation.TypeTreeSource);
            int resource = resources!.Find(target.EntryIndex, clip.ResourcePath);
            using var original = resources.OpenCurrent(resource);
            if (clip.Offset != 0 || clip.Size != original.Length)
                throw new InvalidDataException("The VideoClip must occupy the entire bundle resource entry.");
            Mp4Container.Validate(original);
            resources.Apply(resource, target, video, offset: null);
            data = clip.ResizeResource(video.Length, file.Header.Endianness);
        }
        else if (classId == AssetClassID.Texture2D)
        {
            var texture = new TextureReplacement(file, asset, session.TypeTrees, operation.TypeTreeSource, replacement.AtlasSize);
            if (operation.SyncAtlasMaterials && replacement.AtlasSize is { } size)
            {
                foreach (var (id, material) in AtlasMaterialSync.Apply(file, asset.PathId, size, session.TypeTrees, operation.TypeTreeSource))
                    expected[id] = new ExpectedObject(AssetClassID.Material, material);
            }

            if (resources is not null && !texture.DimensionsChanged && texture.Stream is { } location)
            {
                int resource = resources.Find(target.EntryIndex, location.Path);
                using var pixels = new MemoryStream(texture.Encode(operation.Replacement, replacement.Payload, session.Textures), writable: false);
                resources.Apply(resource, target, pixels, location.Offset);
                expected[asset.PathId] = new ExpectedObject(classId, SerializedObject.ReadBytes(file, asset));
                return;
            }

            data = texture.ReplaceInline(operation.Replacement, replacement.Payload, file.Header.Endianness, session.Textures);
        }
        else
        {
            data = classId == AssetClassID.TextAsset
                ? TextAssetReplacement.Build(file, asset, replacement.Payload)
                : StructuredAssetDump.Import(file, asset, replacement.Payload, session.TypeTrees, operation.TypeTreeSource);
        }

        asset.SetNewData(data);
        expected[asset.PathId] = new ExpectedObject(classId, data);
    }

    internal static void VerifyObjects(AssetsFile staged, Dictionary<long, ExpectedObject> expected)
    {
        var remaining = new Dictionary<long, ExpectedObject>(expected);
        foreach (AssetFileInfo asset in staged.AssetInfos)
        {
            if (!expected.TryGetValue(asset.PathId, out var item))
                continue;
            if (!remaining.Remove(asset.PathId) || asset.TypeId != (int)item.ClassId ||
                !SerializedObject.ReadBytes(staged, asset).AsSpan().SequenceEqual(item.Data))
            {
                throw new InvalidDataException($"The staged {item.ClassId} (PathID {asset.PathId}) does not match the requested replacement.");
            }
        }
        if (remaining.Count > 0)
            throw new InvalidDataException($"The staged entry is missing PathID {remaining.Keys.First()}.");
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

    private sealed class BundleResources(AssetBundleFile bundle, Dictionary<int, AssetsFile> files) : IDisposable
    {
        private sealed record Range(int Entry, long PathId, long? Offset, long Size);
        private sealed record Resource(string Name, FileStream Stream, List<Range> Changes);

        private readonly Dictionary<int, Resource> changes = new();
        internal AssetBundleFile Bundle => bundle;

        internal int Find(int assetIndex, string path)
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
            var matches = Enumerable.Range(0, entries.Count).Where(i => entries[i].Name == name).Take(2).ToArray();
            if (matches.Length != 1 || entries[matches[0]].IsSerialized || files.ContainsKey(matches[0]))
                throw new InvalidDataException($"Expected one resource entry named '{name}' in the UnityFS bundle.");
            return matches[0];
        }

        internal Stream OpenCurrent(int index) => changes.TryGetValue(index, out var patch)
            ? new SegmentStream(patch.Stream, 0, patch.Stream.Length) : Open(bundle, index);

        internal void Apply(int index, Target target, Stream payload, long? offset)
        {
            string name = bundle.GetFileName(index);
            var range = new Range(target.EntryIndex, target.Asset.PathId, offset, payload.Length);
            if (!changes.TryGetValue(index, out var resource))
            {
                var stream = OpenTemporaryStream();
                changes.Add(index, resource = new Resource(name, stream, []));
                if (offset is not null)
                {
                    using var original = Open(bundle, index);
                    original.CopyTo(stream);
                    if (stream.Length != original.Length)
                        throw new InvalidDataException("The streamed texture resource is truncated.");
                }
            }

            if (offset is { } start && (start < 0 || start > resource.Stream.Length - payload.Length))
                throw new InvalidDataException("The texture stream range is outside its resource entry.");

            foreach (Range previous in resource.Changes)
            {
                if (previous.Entry == range.Entry && previous.PathId == range.PathId && previous.Offset == range.Offset)
                    continue;
                if (previous.Offset is null || offset is null ||
                    (offset < previous.Offset + previous.Size && previous.Offset < offset + payload.Length))
                {
                    throw new InvalidDataException($"Overlapping replacement ranges in bundle resource '{name}'.");
                }
            }
            resource.Changes.RemoveAll(previous => previous.Entry == range.Entry &&
                previous.PathId == range.PathId && previous.Offset == range.Offset);
            resource.Changes.Add(range);

            if (offset is null)
                resource.Stream.SetLength(0);
            resource.Stream.Position = offset ?? 0;
            payload.Position = 0;
            payload.CopyTo(resource.Stream);
        }

        internal void Stage()
        {
            foreach (var (index, resource) in changes)
                bundle.BlockAndDirInfo.DirectoryInfos[index].Replacer = new ContentReplacerFromStream(resource.Stream);
        }

        internal void Verify(AssetBundleFile staged)
        {
            foreach (var (index, resource) in changes)
            {
                if (staged.GetFileName(index) != resource.Name)
                    throw new InvalidDataException("The expected resource is missing.");
                using var file = Open(staged, index);
                if (file.Length != resource.Stream.Length)
                    throw new InvalidDataException($"The staged resource '{resource.Name}' has an unexpected length.");
                VerifyBytes(file, resource.Stream, $"resource '{resource.Name}'");
            }
        }

        private static SegmentStream Open(AssetBundleFile bundle, int index)
        {
            bundle.GetFileRange(index, out long offset, out long size);
            return new SegmentStream(bundle.DataReader.BaseStream, offset, size);
        }

        public void Dispose()
        {
            foreach (Resource resource in changes.Values)
                resource.Stream.Dispose();
        }
    }

    internal static FileStream OpenTemporaryStream()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 4096, FileOptions.DeleteOnClose);
    }
}
