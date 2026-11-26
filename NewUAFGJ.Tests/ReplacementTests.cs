using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Xunit;

namespace NewUAFGJ.Tests;

public sealed partial class ReplacementTests : IDisposable
{
    private const long SelectedPathId = -900000000000000001;
    private const long OtherPathId = long.MaxValue;
    private const string SampleTextDump = """
        0 TextAsset Base
         1 string m_Name = "dump name"
         1 string m_Script = "first "quoted"\nsecond\\n\r\nPerché [\] \\s\\"
        """;
    private const string SampleMonoBehaviourDump = """
        0 MonoBehaviour Base
         1 string m_Name = "behaviour"
         0 PPtr<MonoScript> m_Script
          0 int m_FileID = 0
          0 SInt64 m_PathID = 109
         0 ExampleRecord records
          1 Array Array (2 items)
           0 int size = 2
           [0]
            0 ExampleRecord data
             0 int id = 1
             1 string text = "first "record"\nnext"
             0 float wait = 0,5
             1 UInt8 enabled = 1
           [1]
            0 ExampleRecord data
             0 int id = 2
             1 string text = "second\\n"
             0 float wait = 1.25
             1 UInt8 enabled = 0
        """;
    private const string AliasedMonoBehaviourDump = """
        0 MonoBehaviour Base
         1 string m_Name = "behaviour"
         0 Color m_Color
          0 float r = 1
          0 float g = 0.5
          0 float b = 0.25
          0 float a = 0
         0 Color32 m_PackedColor
          0 unsigned int rgba = 16909060
         0 Vector4 m_Vector
          0 float x = -1
          0 float y = -2
          0 float z = -3
          0 float w = -4
         0 vector m_Empty
          1 Array Array (0 items)
           0 int size = 0
        """;
    private const string SdfFontDump = """
        0 MonoBehaviour Base
         1 string m_Name = "test font"
         0 Glyph m_GlyphTable
          0 Array Array (0 items)
           0 int size = 0
         0 TMP_Character m_CharacterTable
          0 Array Array (0 items)
           0 int size = 0
         0 vector m_AtlasTextures
          0 Array Array (1 items)
           0 int size = 1
           [0]
            0 PPtr<Texture2D> data
             0 int m_FileID = 0
             0 SInt64 m_PathID = 44
        """;
    private const string FlattenedSdfFontDump = """
        0 MonoBehaviour Base
         1 string m_Name = "test font"
         1 Array m_GlyphTable (0 items)
          0 int size = 0
         1 Array m_CharacterTable (0 items)
          0 int size = 0
         1 Array m_AtlasTextures (1 items)
          0 int size = 1
          [0]
           0 PPtr<Texture2D> m_AtlasTextures
            0 int m_FileID = 0
            0 SInt64 m_PathID = 44
        """;
    private const string OriginalSdfFontDump = """
        0 MonoBehaviour Base
         1 string m_Name = "test font"
         0 Glyph m_GlyphTable
          0 Array Array (1 items)
           0 int size = 1
           [0]
            0 Glyph data
             0 unsigned int m_Index = 23
         0 TMP_Character m_CharacterTable
          0 Array Array (1 items)
           0 int size = 1
           [0]
            0 TMP_Character data
             0 unsigned int m_Unicode = 65
         0 vector m_AtlasTextures
          0 Array Array (1 items)
           0 int size = 1
           [0]
            0 PPtr<Texture2D> data
             0 int m_FileID = 0
             0 SInt64 m_PathID = 44
        """;
    private const string FontDump = """
        0 Font Base
         1 string m_Name = "test font"
         0 PPtr<Material> m_DefaultMaterial
          0 int m_FileID = 0
          0 SInt64 m_PathID = 23
         1 vector m_FontData
          0 Array Array (0 items)
           0 int size = 0
         0 vector m_FontNames
          1 Array Array (1 items)
           0 int size = 1
            1 string data = "family"
         1 vector m_FallbackFonts
          1 Array Array (1 items)
           0 int size = 1
            0 PPtr<Font> data
             0 int m_FileID = 0
             0 SInt64 m_PathID = 44
        """;
    private static readonly byte[] OriginalPayload = "old"u8.ToArray();
    private static readonly byte[] ReplacementPayload = [0, 255, 2, 128, 42];
    // A 4x8 RGBA PNG with red over blue and different alpha values in the two rows of BC3 blocks.
    private const string TexturePng =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAICAYAAADeM14FAAAAGElEQVR4nGP4z8DwHxkzkCEAxKGomGQBAHc5NSF/UWltAAAAAElFTkSuQmCC";
    private static readonly Hash128 NameAndScriptTypeHash = new(Convert.FromHexString("486ba4e15dbd6aea8ac1a064305889c8"));
    private static readonly Hash128 MonoBehaviourTypeHash = new(Convert.FromHexString("1a2b3c4d5e6f0102030405060708090a"));
    private static readonly Hash128 MonoBehaviourScriptHash = new(Convert.FromHexString("1a2b3c4d5e6f0a090807060504030201"));
    private const string FixtureUnityVersion = "2022.3.0f1";
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("newuafgj-test-");

    [Theory]
    [InlineData(17u, false)]
    [InlineData(22u, false)]
    [InlineData(22u, true)]
    public async Task ReplacesOnlySelectedAssetAndPreservesName(uint formatVersion, bool bigEndian)
    {
        string source = UnderTemp("with spaces.assets");
        using (var stream = File.Create(source))
        {
            UnityFixture.WriteAssets(stream, formatVersion, true, bigEndian, null,
                (SelectedPathId, "existing name", OriginalPayload),
                (OtherPathId, "other", OriginalPayload));
        }
        string replacement = MakeReplacement();

        var result = await RunCli(source, replacement, "-900000000000000001", "0", "legacy value");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("TextAsset replaced.", result.Output);
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        AssertText(file, SelectedPathId, "existing name", ReplacementPayload);
        AssertText(file, OtherPathId, "other", OriginalPayload);
        Assert.Equal(formatVersion, file.Header.Version);
        Assert.Equal(bigEndian, file.Header.Endianness);
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(AssetBundleCompressionType.None)]
    [InlineData(AssetBundleCompressionType.LZ4)]
    [InlineData(AssetBundleCompressionType.LZMA)]
    public async Task ReplacesBundleAssetAndPreservesOtherEntries(AssetBundleCompressionType compression)
    {
        string source = UnderTemp("bundle-without-extension");
        byte[] assets = UnityFixture.CreateAssets((SelectedPathId, "nested", OriginalPayload));
        byte[] otherAssets = UnityFixture.CreateAssets((OtherPathId, "other", OriginalPayload));
        // Put the target past the first LZ4 block and verify the opaque entry byte for byte.
        byte[] resource = new byte[0x20000 + 31];
        for (int i = 0; i < resource.Length; i++)
            resource[i] = (byte)(i % 251);
        UnityFixture.WriteBundle(source, compression,
            (UnityFixture.ResourceName, resource, false),
            (UnityFixture.EntryName, assets, true),
            ("CAB-other", otherAssets, true));

        var result = await RunCli(source, MakeReplacement(), "-900000000000000001", "-");

        Assert.True(result.ExitCode == 0, result.Error);
        var manager = new AssetsManager();
        try
        {
            BundleFileInstance bundle = manager.LoadBundleFile(source);
            Assert.Equal(compression, bundle.originalCompression);
            Assert.Equal(new[] { UnityFixture.ResourceName, UnityFixture.EntryName, "CAB-other" },
                bundle.file.BlockAndDirInfo.DirectoryInfos.Select(entry => entry.Name));
            Assert.Equal(resource, BundleHelper.LoadAssetDataFromBundle(bundle.file, UnityFixture.ResourceName));
            Assert.Equal(otherAssets, BundleHelper.LoadAssetDataFromBundle(bundle.file, "CAB-other"));
            AssetsFileInstance entry = manager.LoadAssetsFileFromBundle(bundle, UnityFixture.EntryName);
            AssertText(entry.file, SelectedPathId, "nested", ReplacementPayload);
        }
        finally
        {
            manager.UnloadAll();
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(AssetBundleCompressionType.None)]
    [InlineData(AssetBundleCompressionType.LZ4)]
    [InlineData(AssetBundleCompressionType.LZMA)]
    public async Task ReplacesWholeVideoResourceAndUpdatesItsSerializedSize(AssetBundleCompressionType compression)
    {
        string bundlePath = UnderTemp("video-bundle");
        string resource = UnityFixture.EntryName + ".resource";
        string resourcePath = $"archive:/{UnityFixture.EntryName}/{resource}";
        byte[] original = MakeMp4(7);
        byte[] clip = UnityFixture.CreateVideoClipAssets(SelectedPathId, resourcePath, 0, original.Length);
        byte[] otherAssets = UnityFixture.CreateAssets((OtherPathId, "other", OriginalPayload));
        byte[] otherResource = [23, 37, 54];
        UnityFixture.WriteBundle(bundlePath, compression,
            (UnityFixture.EntryName, clip, true),
            (resource, original, false),
            ("CAB-other", otherAssets, true),
            ("other.resource", otherResource, false));
        string replacement = UnderTemp("replacement.resS");
        byte[] video = MakeMp4(50);
        File.WriteAllBytes(replacement, video);

        var result = await RunCli(bundlePath, replacement, "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("VideoClip replaced.", result.Output);
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            Assert.Equal(compression, bundle.originalCompression);
            Assert.Equal(video, BundleHelper.LoadAssetDataFromBundle(bundle.file, resource));
            Assert.Equal(otherAssets, BundleHelper.LoadAssetDataFromBundle(bundle.file, "CAB-other"));
            Assert.Equal(otherResource, BundleHelper.LoadAssetDataFromBundle(bundle.file, "other.resource"));
            var file = manager.LoadAssetsFileFromBundle(bundle, UnityFixture.EntryName).file;
            var info = file.GetAssetInfo(SelectedPathId);
            var reader = file.Reader;
            reader.Position = info.GetAbsoluteByteOffset(file);
            AssertAlignedString(reader, "test clip");
            Assert.Equal(1920u, reader.ReadUInt32());
            Assert.Equal(1080u, reader.ReadUInt32());
            AssertAlignedString(reader, resourcePath);
            Assert.Equal(0UL, reader.ReadUInt64());
            Assert.Equal((ulong)video.Length, reader.ReadUInt64());
            Assert.False(reader.ReadBoolean());
        }
        finally
        {
            manager.UnloadAll();
        }
        AssertNoStagingFile(bundlePath);
    }

    [Theory]
    [InlineData("input.resS", "replacement.mp4")]
    [InlineData("input.mp4", "replacement.resS")]
    public async Task ReplacesStandaloneMp4BySignature(string inputName, string replacementName)
    {
        string input = UnderTemp(inputName);
        File.WriteAllBytes(input, MakeMp4(7));
        string replacement = UnderTemp(replacementName);
        byte[] video = MakeMp4(50);
        File.WriteAllBytes(replacement, video);

        var result = await RunCli(input, replacement);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(video, File.ReadAllBytes(input));
        AssertNoStagingFile(input);
    }

    [Fact]
    public async Task RejectsTruncatedVideoUnchanged()
    {
        string input = UnderTemp("input.resS");
        byte[] original = MakeMp4(7);
        File.WriteAllBytes(input, original);
        string replacement = UnderTemp("replacement.mp4");
        File.WriteAllBytes(replacement, MakeMp4(50)[..^1]);

        var result = await RunCli(input, replacement);

        AssertFailure(result, "outside the file bounds");
        Assert.Equal(original, File.ReadAllBytes(input));
        AssertNoStagingFile(input);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task RejectsVideoThatDoesNotOwnTheEntireBundleResource(int offset, int unusedBytes)
    {
        string bundle = UnderTemp("video-bundle");
        string resource = UnityFixture.EntryName + ".resource";
        byte[] video = MakeMp4(7);
        byte[] clip = UnityFixture.CreateVideoClipAssets(SelectedPathId, resource, offset, video.Length);
        UnityFixture.WriteBundle(bundle, AssetBundleCompressionType.None,
            (UnityFixture.EntryName, clip, true),
            (resource, [.. new byte[offset], .. video, .. new byte[unusedBytes]], false));
        byte[] original = File.ReadAllBytes(bundle);
        string replacement = UnderTemp("replacement.mp4");
        File.WriteAllBytes(replacement, MakeMp4(50));

        var result = await RunCli(bundle, replacement);

        AssertFailure(result, "must occupy the entire bundle resource");
        Assert.Equal(original, File.ReadAllBytes(bundle));
        AssertNoStagingFile(bundle);
    }

    private static byte[] MakeMp4(int mediaSize)
    {
        using var data = new MemoryStream();
        using var writer = new AssetsFileWriter(data) { BigEndian = true };
        writer.Write(16u);
        writer.Write("ftypmp42"u8.ToArray());
        writer.Write(0u);
        writer.Write(12u);
        writer.Write("moov"u8.ToArray());
        writer.Write(1u);
        writer.Write((uint)(8 + mediaSize));
        writer.Write("mdat"u8.ToArray());
        writer.Write(Enumerable.Repeat((byte)0x55, mediaSize).ToArray());
        return data.ToArray();
    }

    [Fact]
    public async Task ReplacesStreamedBc3BlocksAndPreservesOtherBundleBytes()
    {
        string bundlePath = UnderTemp("textures.bundle");
        string textureStream = UnityFixture.EntryName + ".resS";
        byte[] assets = UnityFixture.CreateTextureAssets(4, 8, 32, $"archive:/{UnityFixture.EntryName}/{textureStream}", 16, SelectedPathId);
        byte[] resource = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
        byte[] unrelatedAssets = UnityFixture.CreateAssets((OtherPathId, "other", OriginalPayload));
        UnityFixture.WriteBundle(bundlePath, AssetBundleCompressionType.None,
            (UnityFixture.EntryName, assets, true),
            (textureStream, resource, false),
            ("CAB-other", unrelatedAssets, true));

        var result = await RunCli(bundlePath, MakeTexturePng(), "-", "-");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Texture2D replaced.", result.Output);
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            Assert.Equal(assets, BundleHelper.LoadAssetDataFromBundle(bundle.file, UnityFixture.EntryName));
            Assert.Equal(unrelatedAssets, BundleHelper.LoadAssetDataFromBundle(bundle.file, "CAB-other"));
            byte[] replaced = BundleHelper.LoadAssetDataFromBundle(bundle.file, textureStream);
            Assert.Equal(resource.Length, replaced.Length);
            Assert.Equal(resource[..16], replaced[..16]);
            Assert.Equal(resource[48..], replaced[48..]);
            AssertSolidBc3Block(replaced.AsSpan(16, 16), 0, 0, 255, 85);
            AssertSolidBc3Block(replaced.AsSpan(32, 16), 255, 0, 0, 255);
        }
        finally
        {
            manager.UnloadAll();
        }

        AssertNoStagingFile(bundlePath);
    }

    [Fact]
    public async Task StoresInlineRgba32PixelsInUnityRowOrder()
    {
        string source = UnderTemp("textures.assets");
        File.WriteAllBytes(source, UnityFixture.CreateTextureAssets(4, 8, 128, "", 0, SelectedPathId,
            format: 4, inlineData: new byte[128]));

        var result = await RunCli(source, MakeTexturePng(), SelectedPathId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "test texture");
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(8, reader.ReadInt32());
        Assert.Equal(128, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(128, reader.ReadInt32());
        byte[] rgba = reader.ReadBytes(128);
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(new byte[] { 0, 0, 255, 85 }, rgba.AsSpan(i * 4, 4).ToArray());
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba.AsSpan(64 + i * 4, 4).ToArray());
        }
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task StoresOnlyPngAlphaInUnityRowOrderForAlpha8Atlas()
    {
        string source = UnderTemp("atlas.assets");
        byte[] original = new byte[32];
        File.WriteAllBytes(source, UnityFixture.CreateTextureAssets(4, 8, original.Length, "", 0, SelectedPathId,
            format: 1, inlineData: original));
        byte[] expectedObject = ReadObject(source, SelectedPathId);
        using (var reader = new AssetsFileReader(new MemoryStream(expectedObject)))
        {
            AssertAlignedString(reader, "test texture");
            // The fixture writes eight Texture2D fields followed by the image data length.
            reader.Position += 8 * sizeof(int);
            Assert.Equal(original.Length, reader.ReadInt32());
            int offset = checked((int)reader.Position);
            Array.Fill(expectedObject, (byte)85, offset, 16);
            Array.Fill(expectedObject, (byte)255, offset + 16, 16);
        }

        var result = await RunCli(source, MakeTexturePng());

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expectedObject, ReadObject(source, SelectedPathId));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(1, 96)]
    [InlineData(2, 120)]
    public async Task StoresRgb24PixelsInUnityRowOrderAndPreservesMipCount(int mipCount, int size)
    {
        string source = UnderTemp("rgb24.assets");
        File.WriteAllBytes(source, UnityFixture.CreateTextureAssets(4, 8, size, "", 0, SelectedPathId,
            format: 3, inlineData: new byte[size], mipCount: mipCount));
        string png = UnderTemp("opaque.png");
        File.WriteAllBytes(png, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAICAYAAADeM14FAAAAHElEQVR4nGPkTrz/nwEJMDGgAcICjL8leSk1AwC/rQR52pjtdAAAAABJRU5ErkJggg=="));

        var result = await RunCli(source, png);

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "test texture");
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(8, reader.ReadInt32());
        Assert.Equal(size, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(mipCount, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(size, reader.ReadInt32());

        int halfMipPixels = 16;
        for (int level = 0; level < mipCount; level++)
        {
            for (int i = 0; i < halfMipPixels; i++)
                Assert.Equal(new byte[] { 251, 25, 13 }, reader.ReadBytes(3));
            for (int i = 0; i < halfMipPixels; i++)
                Assert.Equal(new byte[] { 11, 97, 223 }, reader.ReadBytes(3));
            halfMipPixels /= 4;
        }
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task RejectsTransparencyWhenReplacingRgb24()
    {
        string source = UnderTemp("rgb24.assets");
        byte[] original = UnityFixture.CreateTextureAssets(4, 8, 96, "", 0, SelectedPathId,
            format: 3, inlineData: new byte[96]);
        File.WriteAllBytes(source, original);

        var result = await RunCli(source, MakeTexturePng());

        AssertFailure(result, "RGB24 cannot store transparency in the replacement PNG.");
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task ResizesStreamedAlpha8AtlasInlineAndPreservesTheResource()
    {
        string source = UnderTemp("atlas.bundle");
        string resourceName = UnityFixture.EntryName + ".resS";
        byte[] serialized = UnityFixture.CreateTextureAssets(4, 4, 16,
            $"archive:/{UnityFixture.EntryName}/{resourceName}", 8, SelectedPathId, format: 1);
        byte[] resource = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            (UnityFixture.EntryName, serialized, true), (resourceName, resource, false));

        var result = await RunCli("--resize-atlas", source, MakeTexturePng(), "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(source);
            Assert.Equal(AssetBundleCompressionType.LZ4, bundle.originalCompression);
            Assert.Equal(resource, BundleHelper.LoadAssetDataFromBundle(bundle.file, resourceName));
            var file = manager.LoadAssetsFileFromBundle(bundle, UnityFixture.EntryName).file;
            var info = file.GetAssetInfo(SelectedPathId);
            var template = new AssetTypeTemplateField();
            template.FromTypeTree(file.Metadata.TypeTreeTypes[info.TypeIdOrIndex]);
            var texture = template.MakeValue(file.Reader, info.GetAbsoluteByteOffset(file));
            Assert.Equal(4, texture["m_Width"].AsInt);
            Assert.Equal(8, texture["m_Height"].AsInt);
            Assert.Equal(32, texture["m_CompleteImageSize"].AsInt);
            Assert.Equal(1, texture["m_TextureFormat"].AsInt);
            Assert.Equal(1, texture["m_MipCount"].AsInt);
            Assert.Equal(Enumerable.Repeat((byte)85, 16).Concat(Enumerable.Repeat((byte)255, 16)), texture["image data"].AsByteArray);
            Assert.Equal(0UL, texture["m_StreamData.offset"].AsULong);
            Assert.Equal(0U, texture["m_StreamData.size"].AsUInt);
            Assert.Empty(texture["m_StreamData.path"].AsString);
        }
        finally
        {
            manager.UnloadAll();
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task SyncsAtlasDimensionsOnlyOnOptInAndForLocalMaterialReferences(bool sync, bool resize)
    {
        const long matchingMaterial = 37, externalMaterial = 38;
        string source = UnderTemp("font.bundle");
        string resourceName = UnityFixture.EntryName + ".resS";
        int height = resize ? 4 : 8;
        byte[] atlas = UnityFixture.CreateTextureAssets(4, height, 4 * height,
            $"archive:/{UnityFixture.EntryName}/{resourceName}", 0, SelectedPathId, format: 1);
        byte[] serialized = UnityFixture.WithAtlasMaterials(atlas, SelectedPathId, matchingMaterial, externalMaterial);
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            (UnityFixture.EntryName, serialized, true), (resourceName, new byte[4 * height], false));
        string png = MakeTexturePng();

        var result = await RunCli(sync
            ? ["--resize-atlas", "--sync-atlas-materials", source, png]
            : ["--resize-atlas", source, png]);

        Assert.True(result.ExitCode == 0, result.Error);
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(source);
            var file = manager.LoadAssetsFileFromBundle(bundle, UnityFixture.EntryName);
            Assert.Equal(sync ? new[] { 4f, 8f } : [1f, 1f], Sizes(matchingMaterial));
            Assert.Equal(new[] { 1f, 1f }, Sizes(externalMaterial));

            float[] Sizes(long pathId) => manager.GetBaseField(file, pathId)["m_SavedProperties.m_Floats.Array"]
                .Children.Select(property => property["second"].AsFloat).ToArray();
        }
        finally
        {
            manager.UnloadAll();
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false, 4, false, "Expected one resource entry")]
    [InlineData(true, 8, false, "Expected 8x8")]
    [InlineData(true, 4, true, "Multiple Texture2Ds were found. Specify a PathID.")]
    public async Task LeavesBundleUntouchedWhenTextureReplacementCannotProceed(
        bool hasStream, int textureWidth, bool addSecondTexture, string message)
    {
        string bundlePath = UnderTemp("textures.bundle");
        string textureStream = UnityFixture.EntryName + ".resS";
        int imageSize = textureWidth * 8;
        byte[] assets = UnityFixture.CreateTextureAssets(textureWidth, 8, imageSize,
            $"archive:/{UnityFixture.EntryName}/{textureStream}", 0, SelectedPathId);
        var entries = new List<(string, byte[], bool)>
        {
            (UnityFixture.EntryName, assets, true),
            (hasStream ? textureStream : "unrelated.resS", new byte[imageSize], false)
        };
        if (addSecondTexture)
        {
            byte[] secondTexture = UnityFixture.CreateTextureAssets(textureWidth, 8, imageSize, "", 0,
                OtherPathId, inlineData: new byte[imageSize]);
            entries.Add(("CAB-other", secondTexture, true));
        }

        UnityFixture.WriteBundle(bundlePath, AssetBundleCompressionType.None, entries.ToArray());
        byte[] original = File.ReadAllBytes(bundlePath);
        string replacement = MakeTexturePng();

        var result = await RunCli(bundlePath, replacement);

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(bundlePath));
        AssertNoStagingFile(bundlePath);
    }

    [Fact]
    public async Task StoresStandaloneStreamedBc1MipChainInline()
    {
        string source = UnderTemp("textures.assets");
        File.WriteAllBytes(source, UnityFixture.CreateTextureAssets(4, 8, 40, "external.resS", 64,
            SelectedPathId, format: 10, mipCount: 4));

        var result = await RunCli(source, MakeTexturePng(), "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "test texture");
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(8, reader.ReadInt32());
        Assert.Equal(40, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(40, reader.ReadInt32());
        Assert.Contains(reader.ReadBytes(40), b => b != 0);
        Assert.Equal(0UL, reader.ReadUInt64());
        Assert.Equal(0U, reader.ReadUInt32());
        AssertAlignedString(reader, "");
        Assert.Equal(reader.BaseStream.Length, reader.Position);
    }

    [Fact]
    public async Task ReplacesBc7TextureWithTheOriginalMipCountAndAlpha()
    {
        string source = UnderTemp("bc7.assets");
        File.WriteAllBytes(source, UnityFixture.CreateTextureAssets(4, 8, 48, "", 0, SelectedPathId,
            format: 25, inlineData: new byte[48], mipCount: 2));

        var result = await RunCli(source, MakeTexturePng());

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "test texture");
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(8, reader.ReadInt32());
        Assert.Equal(48, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(25, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(48, reader.ReadInt32());

        var decoder = new BcDecoder();
        foreach (int reduction in new[] { 1, 2 })
        {
            int width = 4 / reduction;
            int height = 8 / reduction;
            var rgba = decoder.DecodeRaw(reader.ReadBytes(32 / reduction), width, height, CompressionFormat.Bc7);
            Assert.InRange(rgba[0].b, 245, 255);
            Assert.InRange(rgba[0].a, 80, 90);
            Assert.InRange(rgba[^1].r, 245, 255);
            Assert.InRange(rgba[^1].a, 250, 255);
        }
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task ReplacesOddSizedRgba32MipChain()
    {
        string source = UnderTemp("odd-mips.assets");
        string png = UnderTemp("odd.png");
        const int bytes = (3 * 5 + 1 * 2 + 1) * 4;
        File.WriteAllBytes(source, UnityFixture.CreateTextureAssets(3, 5, bytes, "", 0, SelectedPathId,
            format: 4, inlineData: new byte[bytes], mipCount: 3));
        File.WriteAllBytes(png, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAMAAAAFCAYAAACAcVaiAAAAFUlEQVR4nGPk1T+ZzAAFTAxIgEgOAFicAXJ9aJZQAAAAAElFTkSuQmCC"));

        var result = await RunCli(source, png);

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "test texture");
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(5, reader.ReadInt32());
        Assert.Equal(bytes, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(bytes, reader.ReadInt32());
        Assert.Equal(Enumerable.Range(0, bytes / 4).SelectMany(_ => new byte[] { 13, 47, 201, 99 }), reader.ReadBytes(bytes));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task ReplacesSpriteByteAndRecordArraysWithDifferentLengths()
    {
        string source = UnderTemp("sprite.assets");
        File.WriteAllBytes(source, UnityFixture.CreateSpriteAssets(SelectedPathId));
        string dump = UnderTemp("sprite.txt");
        File.WriteAllText(dump, """
            0 Sprite Base
             1 string m_Name = "logo"
             0 vector m_Indices
              1 Array Array (3 items)
               0 int size = 3
               [0]
                0 UInt8 data = 9
               [1]
                0 UInt8 data = 19
               [2]
                0 UInt8 data = 29
             0 vector m_Records
              1 Array Array (2 items)
               0 int size = 2
               [0]
                0 Record data
                 0 int id = 101
               [1]
                0 Record data
                 0 int id = 202
            """);

        var result = await RunCli(source, dump, "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Sprite replaced.", result.Output);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "logo");
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(new byte[] { 9, 19, 29 }, reader.ReadBytes(3));
        reader.Align();
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(101, reader.ReadInt32());
        Assert.Equal(202, reader.ReadInt32());
        Assert.Equal(reader.BaseStream.Length, reader.Position);
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public async Task ReplacesRectTransformPositionOnlyWithMatchingParent(int parent, bool succeeds)
    {
        string source = UnderTemp("rect.assets");
        byte[] original = UnityFixture.CreateRectTransformAssets(SelectedPathId);
        File.WriteAllBytes(source, original);
        string dump = UnderTemp("rect.txt");
        File.WriteAllText(dump, $"""
            0 RectTransform Base
             0 PPtr<Transform> m_Father
              0 int m_FileID = 0
              0 SInt64 m_PathID = {parent}
             0 Vector2f m_AnchoredPosition
              0 float x = -1,5
              0 float y = 3
            """);

        var result = await RunCli(source, dump, "-900000000000000001");

        if (!succeeds)
        {
            AssertFailure(result, "changes the RectTransform's m_Father");
            Assert.Equal(original, File.ReadAllBytes(source));
            return;
        }

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("RectTransform replaced.", result.Output);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(40L, reader.ReadInt64());
        Assert.Equal(-1.5f, reader.ReadSingle());
        Assert.Equal(3f, reader.ReadSingle());
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public async Task ImportsGameObjectActivationOnlyWithMatchingComponentReference(long component, bool succeeds)
    {
        string source = UnderTemp("gameobject.bundle");
        byte[] originalObject = UnityFixture.CreateGameObjectAssets(SelectedPathId);
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZMA, (UnityFixture.EntryName, originalObject, true));
        byte[] originalBundle = File.ReadAllBytes(source);
        string dump = UnderTemp("button.txt");
        File.WriteAllText(dump, $"""
            0 GameObject Base
             0 vector m_Component
              1 Array Array (1 items)
               0 int size = 1
               [0]
                0 ComponentPair data
                 0 PPtr<Component> component
                  0 int m_FileID = 0
                  0 SInt64 m_PathID = {component}
             1 string m_Name = "button"
             0 bool m_IsActive = false
            """);

        var result = await RunCli(source, dump, "-900000000000000001");

        if (!succeeds)
        {
            AssertFailure(result, "changes the GameObject's m_Component");
            Assert.Equal(originalBundle, File.ReadAllBytes(source));
        }
        else
        {
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("GameObject replaced.", result.Output);
            var manager = new AssetsManager();
            try
            {
                var bundle = manager.LoadBundleFile(source);
                Assert.Equal(AssetBundleCompressionType.LZMA, bundle.originalCompression);
                var file = manager.LoadAssetsFileFromBundle(bundle, UnityFixture.EntryName).file;
                var info = file.GetAssetInfo(SelectedPathId);
                Assert.Equal((int)AssetClassID.GameObject, info.TypeId);
                file.Reader.Position = info.GetAbsoluteByteOffset(file);
                Assert.Equal(1, file.Reader.ReadInt32());
                Assert.Equal(0, file.Reader.ReadInt32());
                Assert.Equal(40, file.Reader.ReadInt64());
                AssertAlignedString(file.Reader, "button");
                Assert.False(file.Reader.ReadBoolean());
            }
            finally
            {
                manager.UnloadAll();
            }
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(42, true)]
    [InlineData(41, false)]
    public async Task ImportsMaterialParametersWithAnUnchangedShaderReference(long shader, bool succeeds)
    {
        string source = UnderTemp("material.assets");
        byte[] original = UnityFixture.CreateMaterialAssets(SelectedPathId);
        File.WriteAllBytes(source, original);
        string dump = UnderTemp("material.txt");
        File.WriteAllText(dump, $"""
            0 Material Base
             1 string m_Name = "test material"
             0 PPtr<Shader> m_Shader
              0 int m_FileID = 1
              0 SInt64 m_PathID = {shader}
             0 float m_Amount = 2.5
            """);

        var result = await RunCli(source, dump, "-900000000000000001");

        if (succeeds)
        {
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("Material replaced.", result.Output);
            using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
            AssertAlignedString(reader, "test material");
            Assert.Equal(1, reader.ReadInt32());
            Assert.Equal(42L, reader.ReadInt64());
            Assert.Equal(2.5f, reader.ReadSingle());
        }
        else
        {
            AssertFailure(result, "changes the Material's m_Shader");
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportsStrippedNativeClassOnlyWhenTheClassPackageTypeHashMatches(bool matching)
    {
        string source = UnderTemp("material.assets");
        string package = UnderTemp("classdata.tpk");
        byte[] original = UnityFixture.CreateStrippedMaterialAssets(SelectedPathId, package, matching);
        File.WriteAllBytes(source, original);
        byte[] packageBytes = File.ReadAllBytes(package);
        string dump = UnderTemp("material.txt");
        File.WriteAllText(dump, """
            0 Material Base
             0 float m_Amount = 2.5
            """);

        var result = await RunCli("--typetree-source", package, source, dump, "-900000000000000001");

        if (matching)
        {
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal(2.5f, BitConverter.ToSingle(ReadObject(source, SelectedPathId)));
        }
        else
        {
            AssertFailure(result, "No Material TypeTree in the class package matches");
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        Assert.Equal(packageBytes, File.ReadAllBytes(package));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task SelectsTheOnlyTextAssetWithoutPathIdAndAcceptsEmptyPayload()
    {
        string source = UnderTemp("single.assets");
        File.WriteAllBytes(source, UnityFixture.CreateAssets((OtherPathId, "single", OriginalPayload)));
        string replacement = UnderTemp("empty");
        File.WriteAllBytes(replacement, []);

        var result = await RunCli(source, replacement);

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        AssertText(file, OtherPathId, "single", []);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public async Task ImportsUabeaDumpScriptAndPreservesAssetName(string newline, bool withBom)
    {
        string source = UnderTemp("dump-target.assets");
        File.WriteAllBytes(source, UnityFixture.CreateAssets((SelectedPathId, "original name", OriginalPayload)));
        string replacement = UnderTemp("replacement.txt");
        File.WriteAllText(replacement, SampleTextDump.ReplaceLineEndings(newline), new UTF8Encoding(withBom));

        var result = await RunCli(source, replacement, "-900000000000000001", "-", "MONOBEHAVIOUR_TEXT");

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        byte[] expected = "first \"quoted\"\nsecond\\n\r\nPerché [] \\s\\"u8.ToArray();
        AssertText(file, SelectedPathId, "original name", expected);
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task RawOptionPreservesDumpShapedBytes()
    {
        string source = UnderTemp("raw-target.assets");
        File.WriteAllBytes(source, UnityFixture.CreateAssets((SelectedPathId, "original name", OriginalPayload)));
        string replacement = UnderTemp("replacement.txt");
        File.WriteAllText(replacement, SampleTextDump, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        byte[] originalBytes = File.ReadAllBytes(replacement);

        var result = await RunCli("--raw", source, replacement);

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        AssertText(file, SelectedPathId, "original name", originalBytes);
    }

    [Theory]
    [InlineData(" 1 string m_Name = \"test\"", "does not contain an m_Script")]
    [InlineData(" 1 string m_Script = \"first\"\n 1 string m_Script = \"second\"", "multiple m_Script")]
    [InlineData(" 1 string m_Script = \"unfinished", "Expected a quoted string")]
    [InlineData(" 1 string m_Script = \"unknown\\t\"", "unsupported escape")]
    public async Task RejectsMalformedTextAssetDumpWithoutChangingSource(string fields, string message)
    {
        string source = UnderTemp("bad-dump-target.assets");
        byte[] original = UnityFixture.CreateAssets((SelectedPathId, "original name", OriginalPayload));
        File.WriteAllBytes(source, original);
        string replacement = UnderTemp("bad-dump.txt");
        File.WriteAllText(replacement, "0 TextAsset Base\n" + fields);

        var result = await RunCli(source, replacement);

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportsMonoBehaviourRecordsWithBothDecimalSeparators(bool bigEndian)
    {
        string source = UnderTemp("behaviours.assets");
        File.WriteAllBytes(source, UnityFixture.CreateMonoBehaviourAssets(bigEndian, SelectedPathId, OtherPathId));
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, SampleMonoBehaviourDump);
        byte[] before = ReadObject(source, OtherPathId);

        var result = await RunCli(source, replacement, "-900000000000000001", "-");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("MonoBehaviour replaced.", result.Output);
        Assert.Equal(before, ReadObject(source, OtherPathId));
        AssertMonoBehaviourReplacement(source, bigEndian);
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AssetBundleCompressionType.LZ4)]
    [InlineData(AssetBundleCompressionType.LZMA)]
    public async Task ImportsStrippedMonoBehaviourUsingMatchingTypeTreeSource(AssetBundleCompressionType? compression)
    {
        string source = UnderTemp("stripped.assets");
        File.WriteAllBytes(source, UnityFixture.CreateMonoBehaviourAssets(false, false,
            MonoBehaviourTypeHash, MonoBehaviourScriptHash, FixtureUnityVersion, SelectedPathId, OtherPathId));
        byte[] before = ReadObject(source, OtherPathId);
        byte[] donorAssets = UnityFixture.CreateMonoBehaviourAssets(false, true,
            MonoBehaviourTypeHash, MonoBehaviourScriptHash, FixtureUnityVersion, 123);
        string donor = UnderTemp(compression is null ? "type trees.assets" : "type trees.bundle");
        if (compression is { } bundleCompression)
            UnityFixture.WriteBundle(donor, bundleCompression, (UnityFixture.EntryName, donorAssets, true));
        else
            File.WriteAllBytes(donor, donorAssets);
        byte[] originalDonor = File.ReadAllBytes(donor);
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, SampleMonoBehaviourDump);

        var result = await RunCli("--typetree-source", donor, source, replacement, "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("MonoBehaviour replaced.", result.Output);
        AssertMonoBehaviourReplacement(source, bigEndian: false);
        Assert.Equal(before, ReadObject(source, OtherPathId));
        Assert.Equal(originalDonor, File.ReadAllBytes(donor));
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        Assert.False(file.Metadata.TypeTreeEnabled);
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData("type hash")]
    [InlineData("script hash")]
    [InlineData("Unity version")]
    [InlineData("blank hashes")]
    public async Task RejectsNonmatchingTypeTreeSourceWithoutChangingEitherInput(string mismatch)
    {
        string source = UnderTemp("stripped.assets");
        Hash128 typeHash = mismatch == "blank hashes" ? Hash128.NewBlankHash() : MonoBehaviourTypeHash;
        Hash128 scriptHash = mismatch == "blank hashes" ? Hash128.NewBlankHash() : MonoBehaviourScriptHash;
        byte[] original = UnityFixture.CreateMonoBehaviourAssets(false, false,
            typeHash, scriptHash, FixtureUnityVersion, SelectedPathId);
        File.WriteAllBytes(source, original);
        string donor = UnderTemp("type trees.assets");
        byte[] originalDonor = UnityFixture.CreateMonoBehaviourAssets(false, true,
            mismatch == "type hash" ? NameAndScriptTypeHash : typeHash,
            mismatch == "script hash" ? NameAndScriptTypeHash : scriptHash,
            mismatch == "Unity version" ? "2022.3.1f1" : FixtureUnityVersion, 123);
        File.WriteAllBytes(donor, originalDonor);
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, SampleMonoBehaviourDump);

        var result = await RunCli("--typetree-source", donor, source, replacement, "-900000000000000001");

        AssertFailure(result, mismatch == "blank hashes" ? "nonzero type hash and script hash" : "No embedded MonoBehaviour TypeTree");
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal(originalDonor, File.ReadAllBytes(donor));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task AcceptsDumpAliasesWhenTheirMembersMatchTheTypeTree()
    {
        string source = UnderTemp("aliases.assets");
        File.WriteAllBytes(source, UnityFixture.CreateMonoBehaviourAliasAssets(SelectedPathId));
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, AliasedMonoBehaviourDump);

        var result = await RunCli(source, replacement, "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "behaviour");
        foreach (float channel in (float[])[1f, 0.5f, 0.25f, 0f])
            Assert.Equal(channel, reader.ReadSingle());
        Assert.Equal(0x01020304u, reader.ReadUInt32());
        foreach (float component in (float[])[-1f, -2f, -3f, -4f])
            Assert.Equal(component, reader.ReadSingle());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(reader.BaseStream.Length, reader.Position);
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData("Color m_Color", "Color32 m_Color", "Expected 0 ColorRGBA m_Color")]
    [InlineData("float r", "int r", "Expected a value for 0 float r")]
    public async Task RejectsAliasesWithIncompatibleMembers(string expected, string replacementType, string message)
    {
        string source = UnderTemp("aliases.assets");
        byte[] original = UnityFixture.CreateMonoBehaviourAliasAssets(SelectedPathId);
        File.WriteAllBytes(source, original);
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, AliasedMonoBehaviourDump.Replace(expected, replacementType));

        var result = await RunCli(source, replacement, "-900000000000000001");

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ClearsSdfGlyphTablesWithoutRedirectingAtlasReferences(bool flattened, bool changeAtlas, bool useOriginalDump)
    {
        string source = UnderTemp("sdf.assets");
        byte[] original = UnityFixture.CreateSdfFontAssets(SelectedPathId, hasTypeTree: !useOriginalDump);
        File.WriteAllBytes(source, original);
        string replacement = UnderTemp("font.txt");
        string dump = flattened ? FlattenedSdfFontDump : SdfFontDump;
        File.WriteAllText(replacement, changeAtlas ? dump.Replace("m_PathID = 44", "m_PathID = 45") : dump);

        string[] args = [source, replacement, "-900000000000000001"];
        if (useOriginalDump)
        {
            string reference = UnderTemp("original-font.txt");
            File.WriteAllText(reference, OriginalSdfFontDump, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            args = ["--typetree-source", reference, ..args];
        }
        var result = await RunCli(args);

        if (changeAtlas)
        {
            AssertFailure(result, "changes the MonoBehaviour's data");
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        else
        {
            Assert.True(result.ExitCode == 0, result.Error);
            using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
            AssertAlignedString(reader, "test font");
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt32());
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(44L, reader.ReadInt64());
            Assert.Equal(reader.BaseStream.Length, reader.Position);
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ClearsSdfFontKerningPairsWithAConsistentArrayCount(int declaredSize)
    {
        string source = UnderTemp("sdf.assets");
        byte[] original = UnityFixture.CreateSdfFontAssets(SelectedPathId, includeFontFeatureTable: true);
        File.WriteAllBytes(source, original);
        string replacement = UnderTemp("font.txt");
        File.WriteAllText(replacement, SdfFontDump + "\n" + $$"""
             0 TMP_FontFeatureTable m_FontFeatureTable
              0 TMP_GlyphPairAdjustmentRecord m_GlyphPairAdjustmentRecords
               0 Array Array (0 items)
                0 int size = {{declaredSize}}
            """);

        var result = await RunCli(source, replacement, SelectedPathId.ToString());

        if (declaredSize == 0)
        {
            Assert.True(result.ExitCode == 0, result.Error);
            using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
            AssertAlignedString(reader, "test font");
            Assert.Equal(0, reader.ReadInt32()); // Glyph count.
            Assert.Equal(0, reader.ReadInt32()); // Character count.
            Assert.Equal(1, reader.ReadInt32()); // Atlas count.
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(44L, reader.ReadInt64());
            Assert.Equal(0, reader.ReadInt32()); // Kerning pair count.
            Assert.Equal(reader.BaseStream.Length, reader.Position);
        }
        else
        {
            AssertFailure(result, "Expected 0 int size = 0");
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false, "does not match the selected object's serialized bytes")]
    [InlineData(true, "has no elements to describe this array")]
    public async Task RejectsIncompleteOrNonmatchingOriginalFontDump(bool emptyGlyphs, string message)
    {
        string source = UnderTemp("stripped-font.assets");
        byte[] original = UnityFixture.CreateSdfFontAssets(SelectedPathId, hasTypeTree: false, hasGlyphs: !emptyGlyphs);
        File.WriteAllBytes(source, original);
        string reference = UnderTemp("original-font.txt");
        File.WriteAllText(reference, emptyGlyphs ? SdfFontDump : OriginalSdfFontDump.Replace("m_Index = 23", "m_Index = 24"));
        string replacement = UnderTemp("replacement-font.txt");
        File.WriteAllText(replacement, OriginalSdfFontDump);

        var result = await RunCli("--typetree-source", reference, source, replacement, "-900000000000000001");

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task ImportsLegacyTextMeshProGlyphsWithAChangedCount()
    {
        string source = UnderTemp("legacyfont.assets");
        File.WriteAllBytes(source, UnityFixture.CreateLegacyTextMeshProAssets(SelectedPathId));
        string dump = UnderTemp("legacyfont.txt");
        File.WriteAllText(dump, """
            0 MonoBehaviour Base
             1 string m_Name = "test font"
             0 FaceInfo m_fontInfo
              0 float AtlasWidth = 512
             0 PPtr<$Texture2D> atlas
              0 int m_FileID = 0
              0 SInt64 m_PathID = 44
             0 TMP_Glyph m_glyphInfoList
              1 Array Array (2 items)
               0 int size = 2
               [0]
                0 TMP_Glyph data
                 0 int id = 65
               [1]
                0 TMP_Glyph data
                 0 int id = 66
             0 KerningTable m_kerningInfo
              0 KerningPair kerningPairs
               0 Array Array (0 items)
                0 int size = 0
            """);

        var result = await RunCli(source, dump, "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
        AssertAlignedString(reader, "test font");
        Assert.Equal(512f, reader.ReadSingle());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(44L, reader.ReadInt64());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(65, reader.ReadInt32());
        Assert.Equal(66, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(reader.BaseStream.Length, reader.Position);
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportsFontsWithUnindexedArraysWithoutRedirectingFallbackReferences(bool changeFallback)
    {
        string source = UnderTemp("font.assets");
        byte[] original = UnityFixture.CreateFontAssets(SelectedPathId);
        File.WriteAllBytes(source, original);
        string replacement = UnderTemp("font.txt");
        File.WriteAllText(replacement, changeFallback ? FontDump.Replace("m_PathID = 44", "m_PathID = 45") : FontDump);

        var result = await RunCli(source, replacement, "-900000000000000001");

        if (changeFallback)
        {
            AssertFailure(result, "changes the Font's data");
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        else
        {
            Assert.True(result.ExitCode == 0, result.Error);
            using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId)));
            AssertAlignedString(reader, "test font");
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(23L, reader.ReadInt64());
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt32());
            AssertAlignedString(reader, "family");
            Assert.Equal(1, reader.ReadInt32());
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(44L, reader.ReadInt64());
            Assert.Equal(reader.BaseStream.Length, reader.Position);
        }
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData("(2 items)", "(3 items)", "Expected 0 Array Array (2 items)")]
    [InlineData("[1]", "[0]", "Expected [1]")]
    [InlineData("m_PathID = 109", "m_PathID = 110", "changes the MonoBehaviour's m_Script")]
    [InlineData("float wait = 0,5", "float wait = 0,5.1", "Invalid value for float wait")]
    public async Task RejectsInvalidMonoBehaviourDumpWithoutChangingSource(string oldValue, string newValue, string message)
    {
        string source = UnderTemp("invalid-behaviour.assets");
        byte[] before = UnityFixture.CreateMonoBehaviourAssets(false, SelectedPathId);
        File.WriteAllBytes(source, before);
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, SampleMonoBehaviourDump.Replace(oldValue, newValue));

        var result = await RunCli(source, replacement, "-900000000000000001");

        AssertFailure(result, message);
        Assert.Equal(before, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task RequiresPathIdForMonoBehaviourDump()
    {
        string source = UnderTemp("behaviour.assets");
        byte[] before = UnityFixture.CreateMonoBehaviourAssets(false, SelectedPathId);
        File.WriteAllBytes(source, before);
        string replacement = UnderTemp("behaviour.txt");
        File.WriteAllText(replacement, SampleMonoBehaviourDump);

        var result = await RunCli(source, replacement);

        AssertFailure(result, "Specify a PathID for a MonoBehaviour dump.");
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(null, "Multiple TextAssets")]
    [InlineData("123", "No TextAsset has PathID")]
    [InlineData("9223372036854775808", "signed 64-bit")]
    public async Task RejectsUnresolvedInputWithoutChangingSource(string? pathId, string message)
    {
        string source = UnderTemp("multiple.assets");
        var original = UnityFixture.CreateAssets(
            (SelectedPathId, "one", OriginalPayload),
            (OtherPathId, "two", OriginalPayload));
        File.WriteAllBytes(source, original);
        string replacement = MakeReplacement();

        var result = await RunCli(pathId is null ? [source, replacement] : [source, replacement, pathId]);

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task RejectsRepeatedPathIdAcrossBundleEntries()
    {
        string source = UnderTemp("ambiguous.bundle");
        byte[] asset = UnityFixture.CreateAssets((SelectedPathId, "ambiguous", OriginalPayload));
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.None, ("CAB-one", asset, true), ("CAB-two", asset, true));
        byte[] original = File.ReadAllBytes(source);

        var result = await RunCli(source, MakeReplacement(), "-900000000000000001");

        AssertFailure(result, "identifies multiple TextAssets");
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task LeavesAssetWithUnknownLayoutAndNoTypeTreeUntouched()
    {
        string source = UnderTemp("stripped.assets");
        using (var stream = File.Create(source))
            UnityFixture.WriteAssets(stream, 22, false, false, null, (SelectedPathId, "stripped", OriginalPayload));
        byte[] original = File.ReadAllBytes(source);

        var result = await RunCli(source, MakeReplacement());

        AssertFailure(result, "requires an embedded TypeTree");
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacesTextAssetWithKnownLayoutAndNoTypeTree(bool bigEndian)
    {
        string source = UnderTemp("known-layout.assets");
        using (var stream = File.Create(source))
        {
            UnityFixture.WriteAssets(stream, 22, false, bigEndian, NameAndScriptTypeHash,
                (SelectedPathId, "existing name", OriginalPayload),
                (OtherPathId, "other", OriginalPayload));
        }

        var result = await RunCli(source, MakeReplacement(), "-900000000000000001");

        Assert.True(result.ExitCode == 0, result.Error);
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        Assert.False(file.Metadata.TypeTreeEnabled);
        Assert.Equal(NameAndScriptTypeHash, file.Metadata.TypeTreeTypes.Single().TypeHash);
        AssertText(file, SelectedPathId, "existing name", ReplacementPayload);
        AssertText(file, OtherPathId, "other", OriginalPayload);
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task RejectsStringExtendingBeyondKnownLayoutObject()
    {
        string source = UnderTemp("truncated-string.assets");
        using (var stream = File.Create(source))
            UnityFixture.WriteAssets(stream, 22, false, false, NameAndScriptTypeHash, (SelectedPathId, "name", OriginalPayload));

        using (var stream = File.Open(source, FileMode.Open, FileAccess.ReadWrite))
        {
            using var reader = new AssetsFileReader(stream);
            var file = new AssetsFile();
            file.Read(reader);
            var asset = file.GetAssetInfo(SelectedPathId);
            reader.Position = asset.GetAbsoluteByteOffset(file);
            int nameLength = reader.ReadInt32();
            reader.Position += nameLength;
            reader.Align();
            using var writer = new AssetsFileWriter(stream);
            // The object contains only four bytes (data and padding) after the string length.
            writer.Write(5);
        }
        byte[] original = File.ReadAllBytes(source);

        var result = await RunCli(source, MakeReplacement());

        AssertFailure(result, "extends beyond the bounds");
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task RejectsTruncatedSourceEvenWhenSelectedObjectIsPresent()
    {
        string source = UnderTemp("truncated.assets");
        byte[] full = UnityFixture.CreateAssets(
            (SelectedPathId, "selected", OriginalPayload),
            (OtherPathId, "truncated tail", "a different payload"u8.ToArray()));
        byte[] truncated = full[..^1];
        File.WriteAllBytes(source, truncated);

        var result = await RunCli(source, MakeReplacement(), "-900000000000000001");

        AssertFailure(result, "Unity serialized file is truncated");
        Assert.Equal(truncated, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task LeavesInvalidInputUntouched()
    {
        string source = UnderTemp("invalid");
        byte[] original = "not a Unity file"u8.ToArray();
        File.WriteAllBytes(source, original);

        var result = await RunCli(source, MakeReplacement());

        AssertFailure(result, "Expected a Unity serialized file or a UnityFS bundle.");
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData("UnityRaw", "Bundle format UnityRaw")]
    [InlineData("UnityWeb", "Bundle format UnityWeb")]
    [InlineData("\x04\x22\x4d\x18", "An LZ4 frame wraps this input")]
    [InlineData("PK\u0003\u0004", "This input is a ZIP archive")]
    public async Task IdentifiesUnsupportedInputContainers(string signature, string message)
    {
        string source = UnderTemp("compressed.assets");
        byte[] original = Encoding.ASCII.GetBytes(signature);
        File.WriteAllBytes(source, original);

        var result = await RunCli(source, MakeReplacement());

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData("replacement.txt", "0 MonoScript Base", "UABEA dumps of MonoScript are unsupported")]
    [InlineData("replacement.txt", "1 Texture2D Base", "UABEA dumps of Texture2D are unsupported")]
    [InlineData("replacement.jpg", "fake JPEG", "Unsupported replacement image")]
    [InlineData("replacement", "DDS ", "Unsupported replacement image")]
    public async Task RejectsRecognizedUnsupportedPayloadUnlessRaw(string fileName, string contents, string message)
    {
        string source = UnderTemp("text.assets");
        byte[] original = UnityFixture.CreateAssets((SelectedPathId, "text", OriginalPayload));
        File.WriteAllBytes(source, original);
        string replacement = UnderTemp(fileName);
        byte[] payload = Encoding.UTF8.GetBytes(contents);
        File.WriteAllBytes(replacement, payload);

        var rejected = await RunCli(source, replacement);

        AssertFailure(rejected, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);

        var raw = await RunCli("--raw", source, replacement);
        Assert.True(raw.ExitCode == 0, raw.Error);
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        AssertText(file, SelectedPathId, "text", payload);
    }

    [Fact]
    public async Task ReportsUnsupportedTextureFormatByNameAndId()
    {
        string source = UnderTemp("texture.assets");
        byte[] original = UnityFixture.CreateTextureAssets(4, 8, 64, "", 0, SelectedPathId,
            format: 13, inlineData: new byte[64]);
        File.WriteAllBytes(source, original);

        var result = await RunCli(source, MakeTexturePng());

        AssertFailure(result, "Texture format RGBA4444 (13) is unsupported");
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(true, "Unsupported UnityFS directory compression (4)")]
    [InlineData(false, "Unsupported UnityFS block compression (4)")]
    public async Task RejectsUnknownUnityFsCompression(bool directory, string message)
    {
        string source = UnderTemp("compressed.bundle");
        byte[] textAsset = UnityFixture.CreateAssets((SelectedPathId, "text", OriginalPayload));
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.None, (UnityFixture.EntryName, textAsset, true));

        using (var stream = new MemoryStream(File.ReadAllBytes(source)))
        using (var reader = new AssetsFileReader(stream))
        {
            var bundle = new AssetBundleFile();
            bundle.Read(reader);
            var writer = new AssetsFileWriter(stream) { BigEndian = true };
            if (directory)
            {
                bundle.Header.FileStreamHeader.Flags |= (AssetBundleFSHeaderFlags)4;
                writer.Position = 0;
                bundle.Header.Write(writer);
            }
            else
            {
                bundle.BlockAndDirInfo.BlockInfos[0].Flags |= 4;
                writer.Position = bundle.Header.GetBundleInfoOffset();
                bundle.BlockAndDirInfo.Write(writer);
            }
            File.WriteAllBytes(source, stream.ToArray());
        }

        byte[] original = File.ReadAllBytes(source);
        var result = await RunCli(source, MakeReplacement());

        AssertFailure(result, message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Fact]
    public async Task DoesNotOverwriteAnExistingStagingFile()
    {
        string source = UnderTemp("locked.assets");
        var original = UnityFixture.CreateAssets((SelectedPathId, "locked", OriginalPayload));
        File.WriteAllBytes(source, original);
        string stagingPath = source + ".newuafgj.tmp";
        File.WriteAllText(stagingPath, "another writer");

        var result = await RunCli(source, MakeReplacement());

        AssertFailure(result, "newuafgj.tmp");
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal("another writer", File.ReadAllText(stagingPath));
    }

    public void Dispose() => temporary.Delete(recursive: true);

    private string UnderTemp(string fileName) => Path.Combine(temporary.FullName, fileName);

    private string MakeReplacement()
    {
        string replacement = UnderTemp("replacement.bytes");
        File.WriteAllBytes(replacement, ReplacementPayload);
        return replacement;
    }

    private string MakeTexturePng()
    {
        string path = UnderTemp("replacement.png");
        File.WriteAllBytes(path, Convert.FromBase64String(TexturePng));
        return path;
    }

    private static void AssertSolidBc3Block(ReadOnlySpan<byte> block, int r, int g, int b, int a)
    {
        int alpha0 = block[0];
        int alpha1 = block[1];
        Span<int> alphas = stackalloc int[8];
        alphas[0] = alpha0;
        alphas[1] = alpha1;
        if (alpha0 > alpha1)
        {
            for (int i = 1; i <= 6; i++)
                alphas[i + 1] = ((7 - i) * alpha0 + i * alpha1) / 7;
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                alphas[i + 1] = ((5 - i) * alpha0 + i * alpha1) / 5;
            alphas[6] = 0;
            alphas[7] = 255;
        }

        ulong alphaSelectors = BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;
        int endpoint0 = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
        int endpoint1 = BinaryPrimitives.ReadUInt16LittleEndian(block[10..]);
        uint colorSelectors = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);

        for (int i = 0; i < 16; i++)
        {
            uint selector = colorSelectors >> (2 * i) & 3;
            int alpha = alphas[(int)(alphaSelectors >> (3 * i) & 7)];
            Assert.InRange(alpha, a - 1, a + 1);
            AssertChannel(selector, endpoint0 >> 11, endpoint1 >> 11, 31, r);
            AssertChannel(selector, endpoint0 >> 5 & 63, endpoint1 >> 5 & 63, 63, g);
            AssertChannel(selector, endpoint0 & 31, endpoint1 & 31, 31, b);
        }
    }

    private static void AssertChannel(uint selector, int value0, int value1, int maximum, int channel)
    {
        value0 = value0 * 255 / maximum;
        value1 = value1 * 255 / maximum;
        int value = selector switch
        {
            0 => value0,
            1 => value1,
            2 => (2 * value0 + value1) / 3,
            _ => (value0 + 2 * value1) / 3
        };
        Assert.InRange(value, channel - 8, channel + 8);
    }

    private static void AssertText(AssetsFile file, long id, string name, byte[] payload)
    {
        var actual = UnityFixture.ReadText(file, id);
        Assert.Equal(name, actual.Name);
        Assert.Equal(payload, actual.Payload);
    }

    private static void AssertFailure((int ExitCode, string Output, string Error) result, string message)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[FATAL]", result.Error);
        Assert.Contains(message, result.Error);
    }

    private static byte[] ReadObject(string source, long id)
    {
        using var reader = new AssetsFileReader(source);
        var file = new AssetsFile();
        file.Read(reader);
        AssetFileInfo asset = file.GetAssetInfo(id);
        reader.Position = asset.GetAbsoluteByteOffset(file);
        return reader.ReadBytes(checked((int)asset.ByteSize));
    }

    private static void AssertAlignedString(AssetsFileReader reader, string value)
    {
        Assert.Equal(value, reader.ReadStringLength(reader.ReadInt32()));
        reader.Align();
    }

    private static void AssertMonoBehaviourReplacement(string source, bool bigEndian)
    {
        using var reader = new AssetsFileReader(new MemoryStream(ReadObject(source, SelectedPathId))) { BigEndian = bigEndian };
        AssertAlignedString(reader, "behaviour");
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(109L, reader.ReadInt64());
        Assert.Equal(2, reader.ReadInt32());
        AssertRecord(reader, 1, "first \"record\"\nnext", 0.5f, 1);
        AssertRecord(reader, 2, "second\\n", 1.25f, 0);
        Assert.Equal(reader.BaseStream.Length, reader.Position);
    }

    private static void AssertRecord(AssetsFileReader reader, int id, string text, float wait, byte enabled)
    {
        Assert.Equal(id, reader.ReadInt32());
        AssertAlignedString(reader, text);
        Assert.Equal(wait, reader.ReadSingle());
        Assert.Equal(enabled, reader.ReadByte());
        reader.Align();
    }

    private static void AssertNoStagingFile(string source) =>
        Assert.False(File.Exists(source + ".newuafgj.tmp"));

    private static async Task<(int ExitCode, string Output, string Error)> RunCli(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "NewUAFGJ.dll"));
        foreach (string arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start NewUAFGJ.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
