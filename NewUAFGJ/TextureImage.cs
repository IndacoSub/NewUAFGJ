using System.Buffers.Binary;
using System.Numerics;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using StbImageSharp;

namespace NewUAFGJ;

internal static class TextureImage
{
    internal const int Alpha8 = 1;
    private const int Rgba32 = 4;
    private const int Dxt1 = 10;
    private const int Dxt5 = 12;
    private const int Bc7 = 25;
    private const int BytesPerPixel = 4;
    private const int CompressedBlockWidth = 4;
    public static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static int GetEncodedSize(int width, int height, int format, int mipCount = 1)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("The texture must have positive dimensions.");

        if (mipCount < 1 || mipCount > 1 + BitOperations.Log2((uint)Math.Max(width, height)))
            throw new InvalidDataException("The texture's mip count is invalid for its dimensions.");

        if (format == Alpha8 && mipCount != 1)
            throw new InvalidDataException("Alpha8 textures require exactly one mip level.");

        (int blockWidth, int bytesPerBlock) = format switch
        {
            Alpha8 => (1, 1),
            Rgba32 => (1, BytesPerPixel),
            Dxt1 => (CompressedBlockWidth, 8),
            Dxt5 or Bc7 => (CompressedBlockWidth, 16),
            _ => throw new InvalidDataException($"Texture format {DescribeFormat(format)} is unsupported. Expected Alpha8, RGBA32, DXT1 (BC1), DXT5 (BC3) or BC7.")
        };
        int size = 0;
        for (int level = 0; level < mipCount; level++)
        {
            size = checked(size + BlockCount(width, blockWidth) * BlockCount(height, blockWidth) * bytesPerBlock);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        return size;
    }

    public static byte[] Encode(byte[] png, int width, int height, int format, int mipCount = 1)
    {
        int expectedSize = GetEncodedSize(width, height, format, mipCount);
        byte[] pixels = DecodePng(png, width, height);
        byte[] encoded = format switch
        {
            Alpha8 => ExtractAlpha(pixels),
            Rgba32 when mipCount == 1 => pixels,
            Rgba32 => EncodeMipChain(pixels, width, height, CompressionFormat.Rgba, mipCount, expectedSize),
            Dxt1 => EncodeMipChain(pixels, width, height, CompressionFormat.Bc1WithAlpha, mipCount, expectedSize),
            Dxt5 => EncodeMipChain(pixels, width, height, CompressionFormat.Bc3, mipCount, expectedSize),
            Bc7 => EncodeMipChain(pixels, width, height, CompressionFormat.Bc7, mipCount, expectedSize),
            _ => throw new InvalidDataException($"Texture format {format} is unsupported.")
        };
        if (encoded.Length != expectedSize)
            throw new InvalidDataException("The encoded texture has an unexpected size.");

        return encoded;
    }

    private static int BlockCount(int dimension, int blockWidth) => (dimension - 1) / blockWidth + 1;

    private static string DescribeFormat(int format) => format switch
    {
        2 => "ARGB4444 (2)",
        3 => "RGB24 (3)",
        7 => "RGB565 (7)",
        13 => "RGBA4444 (13)",
        28 => "DXT1Crunched (28)",
        29 => "DXT5Crunched (29)",
        _ => format.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static byte[] ExtractAlpha(ReadOnlySpan<byte> pixels)
    {
        byte[] alpha = new byte[pixels.Length / BytesPerPixel];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = pixels[i * BytesPerPixel + 3];

        return alpha;
    }

    internal static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> header)
    {
        if (header.Length < 33 || !header.StartsWith(PngSignature) ||
            BinaryPrimitives.ReadUInt32BigEndian(header[8..]) != 13 || !header[12..16].SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("Expected a PNG image with an IHDR header.");
        }

        uint pngWidth = BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
        uint pngHeight = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
        if (pngWidth is 0 or > int.MaxValue || pngHeight is 0 or > int.MaxValue)
            throw new InvalidDataException("The PNG dimensions are out of range.");

        return ((int)pngWidth, (int)pngHeight);
    }

    private static byte[] DecodePng(byte[] png, int width, int height)
    {
        var (pngWidth, pngHeight) = ReadPngDimensions(png);
        if (pngWidth != width || pngHeight != height)
            throw new InvalidDataException($"The PNG is {pngWidth}x{pngHeight}. Expected {width}x{height}.");

        var pixels = new byte[checked(width * height * BytesPerPixel)];
        ImageResult image = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        if (image.Width != width || image.Height != height || image.Data.Length != pixels.Length)
            throw new InvalidDataException("The decoded PNG does not match its header.");

        // PNG scanlines start at the top, whereas the first Unity texture row is the bottom one.
        int stride = width * BytesPerPixel;
        for (int row = 0; row < height; row++)
            image.Data.AsSpan(row * stride, stride).CopyTo(pixels.AsSpan((height - 1 - row) * stride));

        return pixels;
    }

    private static byte[] EncodeMipChain(ReadOnlySpan<byte> pixels, int width, int height, CompressionFormat format, int mipCount, int expectedSize)
    {
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = mipCount > 1;
        encoder.OutputOptions.MaxMipMapLevel = mipCount;
        encoder.OutputOptions.Quality = format == CompressionFormat.Bc7 ? CompressionQuality.Fast : CompressionQuality.Balanced;
        byte[][] mips = encoder.EncodeToRawBytes(pixels, width, height, PixelFormat.Rgba32);
        if (mips.Length != mipCount || mips.Sum(mip => mip.Length) != expectedSize)
            throw new InvalidDataException("The encoded mip levels have an unexpected size.");

        if (mipCount == 1)
            return mips[0];

        byte[] packed = new byte[expectedSize];
        int offset = 0;
        foreach (byte[] mip in mips)
        {
            mip.CopyTo(packed, offset);
            offset += mip.Length;
        }
        return packed;
    }
}
