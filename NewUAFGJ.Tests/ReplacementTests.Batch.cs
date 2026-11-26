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

    [Fact]
    public async Task BatchWaitsOneSecondBetweenOperationsByDefault()
    {
        string source = UnderTemp("source.assets");
        File.WriteAllBytes(source, UnityFixture.CreateAssets((1, "first", OriginalPayload),
            (2, "second", OriginalPayload)));
        string replacement = MakeReplacement();
        string plan = UnderTemp("cooldown.json");
        File.WriteAllText(plan, JsonSerializer.Serialize(new
        {
            sources = new[] { new { source, operations = new[] { new { replacement, pathId = 1 },
                new { replacement, pathId = 2 } } } }
        }));

        var result = await RunCli("batch", plan);

        Assert.True(result.ExitCode == 0, result.Error);
        const string prefix = "[BATCH] ";
        string line = result.Output.Split('\n').Single(output => output.StartsWith(prefix, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(line[prefix.Length..]);
        double delay = json.RootElement.GetProperty("timings").GetProperty("Same-source cooldown").GetDouble();
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
