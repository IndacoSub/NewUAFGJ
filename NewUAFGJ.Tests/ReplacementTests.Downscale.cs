using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Xunit;

namespace NewUAFGJ.Tests;

public sealed partial class ReplacementTests
{
    [Fact]
    public async Task DownscalesToTheStreamedTextureSizeWithoutChangingItsLayout()
    {
        string source = UnderTemp("texture.bundle");
        const int offset = 12;
        const int size = 40;
        byte[] resource = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
        byte[] assets = UnityFixture.CreateTextureAssets(2, 5, size, UnityFixture.ResourceName, offset,
            SelectedPathId, format: 4);
        UnityFixture.WriteBundle(source, AssetBundleCompressionType.LZ4,
            (UnityFixture.EntryName, assets, true), (UnityFixture.ResourceName, resource, false));
        byte[] original = File.ReadAllBytes(source);
        string png = MakeTexturePng();

        AssertFailure(await RunCli(source, png), "The PNG is 4x8. Expected 2x5.");
        Assert.Equal(original, File.ReadAllBytes(source));
        var result = await RunCli("--downscale", source, png);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("from 4x8 to 2x5", result.Output);
        using var file = File.OpenRead(source);
        using var bundle = new BundleInput(file);
        Assert.Equal(AssetBundleCompressionType.LZ4, bundle.Compression);
        Assert.Equal(assets, BundleHelper.LoadAssetDataFromBundle(bundle.Bundle, UnityFixture.EntryName));
        byte[] replaced = BundleHelper.LoadAssetDataFromBundle(bundle.Bundle, UnityFixture.ResourceName);
        Assert.Equal(resource[..offset], replaced[..offset]);
        Assert.Equal(resource[(offset + size)..], replaced[(offset + size)..]);
        Assert.Equal(new byte[] {
            0, 0, 255, 85, 0, 0, 255, 85,
            0, 0, 255, 85, 0, 0, 255, 85,
            191, 0, 64, 170, 191, 0, 64, 170,
            255, 0, 0, 255, 255, 0, 0, 255,
            255, 0, 0, 255, 255, 0, 0, 255
        }, replaced[offset..(offset + size)]);
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(8, 8, 4, "cannot enlarge")]
    [InlineData(2, 2, 4, "requires compatible aspect ratios")]
    [InlineData(2, 4, 1, "unavailable for Alpha8")]
    public async Task DownscaleRejectsIncompatibleTargets(int width, int height, int format, string message)
    {
        string source = UnderTemp("texture.assets");
        int size = width * height * (format == 1 ? 1 : 4);
        byte[] original = UnityFixture.CreateTextureAssets(width, height, size, "", 0, SelectedPathId,
            format: format, inlineData: new byte[size]);
        File.WriteAllBytes(source, original);

        AssertFailure(await RunCli("--downscale", source, MakeTexturePng()), message);
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchChecksDownscalePermissionBeforeReusingEncodings(bool secondDownscale)
    {
        string source = UnderTemp("texture.assets");
        string output = UnderTemp("output.assets");
        byte[] original = UnityFixture.CreateTextureAssets(2, 4, 32, "", 0, SelectedPathId,
            format: 4, inlineData: new byte[32]);
        File.WriteAllBytes(source, original);
        string replacement = MakeTexturePng();
        var result = await RunBatch([new { source, output, operations = new[]
        {
            new { replacement, downscale = true },
            new { replacement, downscale = secondDownscale }
        } }]);

        Assert.Equal(secondDownscale, File.Exists(output));
        if (secondDownscale)
        {
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("Reused the encoded texture", result.Output);
        }
        else
        {
            AssertFailure(result, "The PNG is 4x8. Expected 2x4.");
        }
        Assert.Equal(original, File.ReadAllBytes(source));
        AssertNoStagingFile(output);
    }

    [Theory]
    [InlineData(false, 128)]
    [InlineData(true, 188)]
    public void DownscaleUsesTheTextureTransferFunction(bool srgb, byte middle)
    {
        var resampling = srgb ? PngResampling.Srgb : PngResampling.Linear;
        byte[] result = TextureDownsampling.Resize([0, 0, 0, 255, 255, 255, 255, 255], (2, 1),
            new TextureEncoding(1, 1, 4, 1, resampling));
        Assert.Equal(new byte[] { middle, middle, middle, 255 }, result);
    }
}
