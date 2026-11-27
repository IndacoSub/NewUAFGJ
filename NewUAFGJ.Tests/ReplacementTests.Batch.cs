using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Xunit;

namespace NewUAFGJ.Tests;

public sealed partial class ReplacementTests
{
    [Fact]
    public async Task DiscoversOneMonoBehaviourIndexAcrossMultipleParallelBatches()
    {
        string game = Directory.CreateDirectory(UnderTemp("game")).FullName;
        string bundles = Directory.CreateDirectory(Path.Combine(game, "StreamingAssets", "AssetBundles")).FullName;
        string donor = Path.Combine(bundles, "extensionless-donor");
        UnityFixture.WriteBundle(donor, AssetBundleCompressionType.LZMA,
            ("CAB-font-schema", MakeScriptFixture(123, hasTypeTree: true), true));
        string dump = MakeScriptDump();
        var jobs = new List<object>();
        byte[] original = MakeScriptFixture(SelectedPathId);
        foreach (int index in new[] { 1, 2 })
        {
            string source = Path.Combine(game, $"sharedassets{index}.assets");
            File.WriteAllBytes(source, original);
            jobs.Add(new { source, output = UnderTemp($"output{index}.assets"),
                operations = new[] { new { replacement = dump, pathId = SelectedPathId } } });
        }

        var result = await RunBatch(jobs, [game], maxWorkers: 2);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(2, result.Output.Split("Automatic TypeTree for MonoBehaviour").Length - 1);
        Assert.Equal(1, result.Output.Split("TypeTree index built").Length - 1);
        Assert.Contains("CAB-font-schema", result.Output);
        foreach (int index in new[] { 1, 2 })
        {
            AssertMonoBehaviourReplacement(UnderTemp($"output{index}.assets"), bigEndian: false);
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(game, $"sharedassets{index}.assets")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipsUnsupportedSerializedFormatsDuringTypeTreeDiscovery(bool insideBundle)
    {
        string source = UnderTemp("sharedassets0.assets");
        File.WriteAllBytes(source, MakeScriptFixture(SelectedPathId));

        byte[] supported = MakeScriptFixture(123, hasTypeTree: true);
        byte[] incompatible = UnityFixture.CreateMonoBehaviourAssets(23, false, true,
            MonoBehaviourTypeHash, MonoBehaviourScriptHash, FixtureUnityVersion, 456);
        int extendedTypeTreeMagic = incompatible.AsSpan().IndexOf("mhtt"u8);
        Assert.True(extendedTypeTreeMagic >= 0);
        incompatible[extendedTypeTreeMagic] ^= 0xff;

        if (insideBundle)
        {
            UnityFixture.WriteBundle(UnderTemp("schemas.bundle"), AssetBundleCompressionType.LZ4,
                ("CAB-unrelated", incompatible, true), ("CAB-supported", supported, true));
        }
        else
        {
            File.WriteAllBytes(UnderTemp("unrelated.assets"), incompatible);
            File.WriteAllBytes(UnderTemp("supported.assets"), supported);
        }

        var result = await RunCli(source, MakeScriptDump(), SelectedPathId.ToString());

        Assert.True(result.ExitCode == 0, result.Error);
        AssertMonoBehaviourReplacement(source, bigEndian: false);
    }

    [Theory]
    [InlineData("type hash")]
    [InlineData("script hash")]
    [InlineData("Unity version")]
    [InlineData("format")]
    [InlineData("TPK")]
    public async Task RejectsAnIncompatibleAutomaticDonor(string mismatch)
    {
        string source = UnderTemp("sharedassets0.assets");
        byte[] original = MakeScriptFixture(SelectedPathId);
        File.WriteAllBytes(source, original);
        string donor = UnderTemp("schema");
        if (mismatch == "TPK")
            UnityFixture.CreateStrippedMaterialAssets(7, donor, matching: true);
        else
        {
            File.WriteAllBytes(donor, UnityFixture.CreateMonoBehaviourAssets(mismatch == "format" ? 21u : 22u,
                false, true, mismatch == "type hash" ? NameAndScriptTypeHash : MonoBehaviourTypeHash,
                mismatch == "script hash" ? NameAndScriptTypeHash : MonoBehaviourScriptHash,
                mismatch == "Unity version" ? "2022.3.1f1" : FixtureUnityVersion, 123));
        }
        string dump = MakeScriptDump();

        var result = await RunCli(source, dump, SelectedPathId.ToString());

        AssertFailure(result, "No compatible definition");
        Assert.Contains("TypeHash", result.Error);
        Assert.Contains("ScriptIdHash", result.Error);
        Assert.Equal(original, File.ReadAllBytes(source));
        if (mismatch == "TPK")
        {
            var explicitResult = await RunCli("--typetree-source", donor, source, dump, SelectedPathId.ToString());
            AssertFailure(explicitResult, "A class package supplies native TypeTrees");
        }
    }

    [Fact]
    public async Task IdentifiesConflictingDefinitionsEvenWithMatchingHashes()
    {
        string source = UnderTemp("sharedassets0.assets");
        byte[] original = MakeScriptFixture(SelectedPathId);
        File.WriteAllBytes(source, original);
        byte[] first = MakeScriptFixture(123, hasTypeTree: true);
        string firstPath = UnderTemp("first.assets");
        File.WriteAllBytes(firstPath, first);
        string secondPath = UnderTemp("second.bundle");
        using var stream = new MemoryStream(first);
        using var reader = new AssetsFileReader(stream);
        var file = new AssetsFile();
        file.Read(reader);
        file.Metadata.TypeTreeTypes[0].Nodes[0].MetaFlags ^= 0x4000;
        using var changed = new MemoryStream();
        file.Write(new AssetsFileWriter(changed));
        UnityFixture.WriteBundle(secondPath, AssetBundleCompressionType.LZ4,
            ("CAB-conflicting", changed.ToArray(), true));

        var result = await RunCli(source, MakeScriptDump(), SelectedPathId.ToString());

        AssertFailure(result, "Conflicting definitions for MonoBehaviour");
        Assert.Contains("first.assets", result.Error);
        Assert.Contains("second.bundle:CAB-conflicting", result.Error);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(AssetBundleCompressionType.LZ4)]
    [InlineData(AssetBundleCompressionType.LZMA)]
    public async Task BatchesMultipleEntriesAndMixedTypesWithPerOperationSchemas(AssetBundleCompressionType compression)
    {
        string source = UnderTemp("bundle");
        string target = UnderTemp("patched");
        string dump = MakeScriptDump();
        string firstSchema = UnderTemp("first.assets");
        string secondSchema = UnderTemp("second.assets");
        byte[] firstScript = MakeScriptFixture(OtherPathId);
        byte[] secondScript = UnityFixture.CreateMonoBehaviourAssets(false, false,
            NameAndScriptTypeHash, MonoBehaviourScriptHash, FixtureUnityVersion, OtherPathId);
        File.WriteAllBytes(firstSchema, MakeScriptFixture(100, hasTypeTree: true));
        File.WriteAllBytes(secondSchema, UnityFixture.CreateMonoBehaviourAssets(false, true,
            NameAndScriptTypeHash, MonoBehaviourScriptHash, FixtureUnityVersion, 100));
        UnityFixture.WriteBundle(source, compression,
            ("CAB-texts", UnityFixture.CreateAssets((SelectedPathId, "first", OriginalPayload),
                (OtherPathId, "second", OriginalPayload)), true),
            ("CAB-duplicate", UnityFixture.CreateAssets((SelectedPathId, "other", OriginalPayload)), true),
            ("CAB-mono-one", firstScript, true),
            ("CAB-mono-two", secondScript, true));
        byte[] original = File.ReadAllBytes(source);
        string raw = MakeReplacement();
        var operations = new object[]
        {
            new { replacement = raw, pathId = SelectedPathId, entry = "CAB-texts", fileId = "0" },
            new { replacement = dump, pathId = OtherPathId, entry = "CAB-mono-one", typeTreeSource = firstSchema },
            new { replacement = dump, pathId = OtherPathId, entry = "CAB-texts", raw = true },
            new { replacement = dump, pathId = OtherPathId, entry = "CAB-mono-two", typeTreeSource = secondSchema }
        };

        var result = await RunBatch([new { source, output = target, operations }]);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(original, File.ReadAllBytes(source));
        using var output = File.OpenRead(target);
        using var bundle = new BundleInput(output);
        Assert.Equal(compression, bundle.Compression);
        var written = BundleHelper.LoadAssetFromBundle(bundle.Bundle, "CAB-texts");
        AssertText(written, SelectedPathId, "first", ReplacementPayload);
        AssertText(written, OtherPathId, "second", File.ReadAllBytes(dump));
        AssertText(BundleHelper.LoadAssetFromBundle(bundle.Bundle, "CAB-duplicate"),
            SelectedPathId, "other", OriginalPayload);
        foreach (string name in new[] { "CAB-mono-one", "CAB-mono-two" })
        {
            var behaviour = BundleHelper.LoadAssetFromBundle(bundle.Bundle, name);
            Assert.False(behaviour.Metadata.TypeTreeEnabled);
            Assert.Contains("first \"record\"", System.Text.Encoding.UTF8.GetString(
                SerializedObject.ReadBytes(behaviour, behaviour.GetAssetInfo(OtherPathId))));
        }
    }

    [Fact]
    public async Task KeepsTwoStreamedTexturesInOneResourceAndReusesIdenticalEncoding()
    {
        string source = UnderTemp("textures.bundle");
        string target = UnderTemp("patched.bundle");
        string replacement = MakeTexturePng();
        byte[] resource = Enumerable.Range(1, 100).Select(value => (byte)value).ToArray();
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            ("CAB-a", UnityFixture.CreateTextureAssets(4, 8, 32, UnityFixture.ResourceName, 8, 1), true),
            ("CAB-b", UnityFixture.CreateTextureAssets(4, 8, 32, UnityFixture.ResourceName, 56, 2), true),
            (UnityFixture.ResourceName, resource, false));

        var result = await RunBatch([new { source, output = target,
            operations = new[] { new { replacement, pathId = 1 }, new { replacement, pathId = 2 } } }]);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Reused the encoded texture", result.Output);
        using var output = File.OpenRead(target);
        using var bundle = new BundleInput(output);
        byte[] replaced = BundleHelper.LoadAssetDataFromBundle(bundle.Bundle, UnityFixture.ResourceName);
        Assert.Equal(resource[..8], replaced[..8]);
        Assert.Equal(resource[40..56], replaced[40..56]);
        Assert.Equal(resource[88..], replaced[88..]);
        Assert.Equal(replaced[8..40], replaced[56..88]);
        AssertSolidBc3Block(replaced.AsSpan(8, 16), 0, 0, 255, 85);
        AssertSolidBc3Block(replaced.AsSpan(24, 16), 255, 0, 0, 255);
    }

    [Fact]
    public async Task ExtendsALegacyTmpFontAndSynchronizesItsAtlasMaterialInOneBatch()
    {
        const long atlasId = 44, fontId = 45, materialId = 46, externalMaterialId = 47;
        string source = UnderTemp("legacy-font.bundle");
        string output = UnderTemp("patched-font.bundle");
        string resourceName = UnityFixture.EntryName + ".resS";
        byte[] atlas = UnityFixture.CreateTextureAssets(2, 8, 16,
            $"archive:/{UnityFixture.EntryName}/{resourceName}", 8, atlasId, format: 1);
        byte[] font = UnityFixture.CreateLegacyTextMeshProAssets(fontId, atlas, atlasId, atlasWidth: 2);
        byte[] assets = UnityFixture.WithAtlasMaterials(font, atlasId, materialId, externalMaterialId);
        byte[] resource = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            (UnityFixture.EntryName, assets, true), (resourceName, resource, false));
        byte[] original = File.ReadAllBytes(source);
        string dump = UnderTemp("legacy-font.txt");
        File.WriteAllText(dump, """
            0 MonoBehaviour Base
             1 string m_Name = "test font"
             0 FaceInfo m_fontInfo
              0 float AtlasWidth = 4
             0 PPtr<$Texture2D> atlas
              0 int m_FileID = 0
              0 SInt64 m_PathID = 44
             0 TMP_Glyph m_glyphInfoList
              1 Array Array (2 items)
               0 int size = 2
               [0]
                0 TMP_Glyph data
                 0 int id = 65
                 0 float xAdvance = 3.25
               [1]
                0 TMP_Glyph data
                 0 int id = 232
                 0 float xAdvance = 2.5
             0 KerningTable m_kerningInfo
              0 KerningPair kerningPairs
               0 Array Array (1 items)
                0 int size = 1
                [0]
                 0 KerningPair data
                  0 float xOffset = 2
            """);

        var result = await RunBatch([new { source, output, operations = new object[]
        {
            new { pathId = atlasId, replacement = MakeTexturePng(), resizeAtlas = true, syncAtlasMaterials = true },
            new { pathId = fontId, replacement = dump }
        } }]);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(original, File.ReadAllBytes(source));
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(output);
            Assert.Equal(AssetBundleCompressionType.LZ4, bundle.originalCompression);
            Assert.Equal(resource, BundleHelper.LoadAssetDataFromBundle(bundle.file, resourceName));
            var file = manager.LoadAssetsFileFromBundle(bundle, UnityFixture.EntryName);
            var patchedAtlas = manager.GetBaseField(file, atlasId);
            Assert.Equal(4, patchedAtlas["m_Width"].AsInt);
            Assert.Equal(8, patchedAtlas["m_Height"].AsInt);
            Assert.Equal(1, patchedAtlas["m_TextureFormat"].AsInt);
            Assert.Equal(0U, patchedAtlas["m_StreamData.size"].AsUInt);
            Assert.Equal(Enumerable.Repeat((byte)85, 16).Concat(Enumerable.Repeat((byte)255, 16)),
                patchedAtlas["image data"].AsByteArray);

            var patchedFont = manager.GetBaseField(file, fontId);
            Assert.Equal(atlasId, patchedFont["atlas.m_PathID"].AsLong);
            Assert.Equal(4f, patchedFont["m_fontInfo.AtlasWidth"].AsFloat);
            Assert.Collection(patchedFont["m_glyphInfoList.Array"].Children,
                glyph => AssertGlyph(glyph, 65, 3.25f),
                glyph => AssertGlyph(glyph, 232, 2.5f));
            Assert.Equal(2f, Assert.Single(patchedFont["m_kerningInfo.kerningPairs.Array"].Children)["xOffset"].AsFloat);
            Assert.Equal(new[] { 4f, 8f }, MaterialDimensions(materialId));
            Assert.Equal(new[] { 1f, 1f }, MaterialDimensions(externalMaterialId));

            float[] MaterialDimensions(long pathId) => manager.GetBaseField(file, pathId)["m_SavedProperties.m_Floats.Array"]
                .Children.Select(property => property["second"].AsFloat).ToArray();
        }
        finally
        {
            manager.UnloadAll();
        }

        static void AssertGlyph(AssetTypeValueField glyph, int id, float advance)
        {
            Assert.Equal(id, glyph["id"].AsInt);
            Assert.Equal(advance, glyph["xAdvance"].AsFloat);
        }
    }

    [Fact]
    public async Task ASecondVideoReplacementUsesTheFirstStagedSize()
    {
        string source = UnderTemp("video.bundle");
        byte[] original = MakeMp4(3);
        byte[] final = MakeMp4(117);
        string firstVideo = UnderTemp("first.mp4");
        string finalVideo = UnderTemp("final.mp4");
        File.WriteAllBytes(firstVideo, MakeMp4(37));
        File.WriteAllBytes(finalVideo, final);
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            ("CAB-video", UnityFixture.CreateVideoClipAssets(1, UnityFixture.ResourceName, 0, original.Length), true),
            (UnityFixture.ResourceName, original, false));

        var result = await RunBatch([new { source, operations = new[] { new { replacement = firstVideo },
            new { replacement = finalVideo } } }]);

        Assert.True(result.ExitCode == 0, result.Error);
        using var output = File.OpenRead(source);
        using var bundle = new BundleInput(output);
        Assert.Equal(final, BundleHelper.LoadAssetDataFromBundle(bundle.Bundle, UnityFixture.ResourceName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LateFailureCannotPublishEarlierOperations(bool inPlace)
    {
        string source = UnderTemp("source.bundle");
        string? output = inPlace ? null : UnderTemp("new.bundle");
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            ("CAB-text", UnityFixture.CreateAssets((SelectedPathId, "first", OriginalPayload)), true));
        byte[] original = File.ReadAllBytes(source);
        string replacement = MakeReplacement();

        var result = await RunBatch([new { source, output, operations = new[]
            { new { replacement, pathId = SelectedPathId }, new { replacement, pathId = OtherPathId } } }]);

        AssertFailure(result, $"No TextAsset has PathID {OtherPathId}");
        Assert.Equal(original, File.ReadAllBytes(source));
        if (output is not null)
            Assert.False(File.Exists(output));
        AssertNoStagingFile(output ?? source);
    }

    [Fact]
    public void FinalObjectVerificationRejectsIncorrectBytesAtAnExpectedPathId()
    {
        using var payload = new MemoryStream(UnityFixture.CreateAssets((1, "first", OriginalPayload),
            (2, "second", OriginalPayload)));
        using var reader = new AssetsFileReader(payload);
        var file = new AssetsFile();
        file.Read(reader);
        var expected = new Dictionary<long, AssetReplacement.ExpectedObject>
        {
            [1] = new(AssetClassID.TextAsset, SerializedObject.ReadBytes(file, file.GetAssetInfo(1))),
            [2] = new(AssetClassID.TextAsset, ReplacementPayload)
        };
        var error = Assert.Throws<InvalidDataException>(() => AssetReplacement.VerifyObjects(file, expected));
        Assert.Contains("PathID 2", error.Message);
    }

    [Fact]
    public async Task DuplicateSourcesAreRejectedBeforeParallelWorkersStart()
    {
        string source = UnderTemp("source.assets");
        byte[] original = UnityFixture.CreateAssets((1, "first", OriginalPayload));
        File.WriteAllBytes(source, original);
        string first = UnderTemp("first.assets");
        string second = UnderTemp("second.assets");
        var operations = new[] { new { replacement = MakeReplacement(), pathId = 1 } };

        var result = await RunBatch([new { source, output = first, operations },
            new { source, output = second, operations }], maxWorkers: 2);

        AssertFailure(result, "duplicate source or output");
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task BatchWaitsOnlyWhenASameSourceDelayIsRequested(int? cooldownSeconds)
    {
        string source = UnderTemp("source.assets");
        File.WriteAllBytes(source, UnityFixture.CreateAssets((1, "first", OriginalPayload),
            (2, "second", OriginalPayload)));
        string replacement = MakeReplacement();
        string plan = UnderTemp("cooldown.json");
        var settings = new Dictionary<string, object>
        {
            ["sources"] = new[] { new { source, operations = new[] { new { replacement, pathId = 1 },
                new { replacement, pathId = 2 } } } }
        };
        if (cooldownSeconds is { } seconds)
            settings.Add("sameSourceDelaySeconds", seconds);
        File.WriteAllText(plan, JsonSerializer.Serialize(settings));

        var result = await RunCli("batch", plan);

        Assert.True(result.ExitCode == 0, result.Error);
        const string prefix = "[BATCH] ";
        string line = result.Output.Split('\n').Single(output => output.StartsWith(prefix, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(line[prefix.Length..]);
        double delay = json.RootElement.GetProperty("timings").GetProperty("Same-source cooldown").GetDouble();
        if (cooldownSeconds is null)
            Assert.Equal(0.0, delay);
        else
            Assert.True(delay >= 950, $"Expected a one-second cooldown, recorded {delay} ms.");
    }

    [Theory]
    [InlineData(AssetBundleCompressionType.LZMA)]
    [InlineData(AssetBundleCompressionType.LZ4)]
    public async Task RepackVerifiesDeferredCompressionWithoutMakingAnotherReplacement(AssetBundleCompressionType compression)
    {
        string source = UnderTemp("original.bundle");
        string target = UnderTemp("new.bundle");
        UnityFixture.WriteBundle(source, compression,
            ("CAB-text", UnityFixture.CreateAssets((1, "first", OriginalPayload)), true),
            (UnityFixture.ResourceName, new byte[200_000], false));
        var batch = await RunBatch([new { source, output = target, deferCompression = true,
            operations = new[] { new { replacement = MakeReplacement() } } }]);
        Assert.True(batch.ExitCode == 0, batch.Error);
        var repack = await RunCli("repack", target, source);
        Assert.True(repack.ExitCode == 0, repack.Error);
        using var output = File.OpenRead(target);
        using var bundle = new BundleInput(output);
        Assert.Equal(compression, bundle.Compression);
        AssertText(BundleHelper.LoadAssetFromBundle(bundle.Bundle, "CAB-text"), 1, "first", ReplacementPayload);
        Assert.Equal(new byte[200_000], BundleHelper.LoadAssetDataFromBundle(bundle.Bundle, UnityFixture.ResourceName));
    }

    private Task<(int ExitCode, string Output, string Error)> RunBatch(
        IEnumerable<object> sources, string[]? roots = null, int maxWorkers = 1)
    {
        string plan = UnderTemp("plan.json");
        File.WriteAllText(plan, JsonSerializer.Serialize(new
        {
            sources, typeTreeRoots = roots ?? [], maxWorkers, sameSourceDelaySeconds = 0
        }));
        return RunCli("--profile", "batch", plan);
    }

    private string MakeScriptDump()
    {
        string dump = UnderTemp("replace-script.txt");
        File.WriteAllText(dump, SampleMonoBehaviourDump);
        return dump;
    }

    private static byte[] MakeScriptFixture(long pathId, bool hasTypeTree = false) =>
        UnityFixture.CreateMonoBehaviourAssets(false, hasTypeTree, MonoBehaviourTypeHash,
            MonoBehaviourScriptHash, FixtureUnityVersion, pathId);
}
