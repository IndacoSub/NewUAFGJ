using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Collections.Generic;

namespace UAFGJ;

partial class Program
{
	private static readonly int[] Bc7Weights4 =
    {
	    0, 4, 9, 13,
	    17, 21, 26, 30,
	    34, 38, 43, 47,
	    51, 55, 60, 64
    };

	private static bool ImportTexturesCustom(
        ref AssetTypeValueField atvf,
        string png,
        int format,
        string fileKind)
    {
        if (atvf == null)
            throw new ArgumentNullException(nameof(atvf));

        if (string.IsNullOrWhiteSpace(png))
            throw new ArgumentException(
				"[FATAL] PNG path is empty.",
                nameof(png));

        if (!File.Exists(png))
            throw new FileNotFoundException(
				"[FATAL] PNG file was not found.",
                png);

        TextureFormat fmt =
            (TextureFormat)format;

        int originalWidth = 0;
        int originalHeight = 0;

        AssetTypeValueField widthField =
            atvf["m_Width"];

        AssetTypeValueField heightField =
            atvf["m_Height"];

        if (widthField != null &&
            !widthField.IsDummy)
        {
            originalWidth =
                widthField.AsInt;
        }

        if (heightField != null &&
            !heightField.IsDummy)
        {
            originalHeight =
                heightField.AsInt;
        }

        bool shouldResize =
            !png.Contains(
                "FOT",
                StringComparison.OrdinalIgnoreCase) &&
            !png.Contains(
                "HOT",
                StringComparison.OrdinalIgnoreCase) &&
            !png.Contains(
                "Atlas",
                StringComparison.OrdinalIgnoreCase);

        DisplayStr(
            $"[PNG] Importing '{Path.GetFileName(png)}' " +
            $"as {fmt}, original={originalWidth}x{originalHeight}, " +
            $"resize={shouldResize}.");

        byte[] encoded;

        int width;
        int height;

        switch (fmt)
        {
            case TextureFormat.DXT1:

                encoded =
                    ImportDXT1Texture(
                        png,
                        shouldResize,
                        originalWidth,
                        originalHeight,
                        out width,
                        out height);

                break;

            case TextureFormat.DXT5:

                encoded =
                    ImportDXT5Texture(
                        png,
                        shouldResize,
                        originalWidth,
                        originalHeight,
                        out width,
                        out height);

                break;

			case TextureFormat.BC7:

				encoded =
					ImportBC7Texture(
						png,
						shouldResize,
						originalWidth,
						originalHeight,
						out width,
						out height);

				break;

			default:

                encoded =
                    ImportTextureWithAssetsTools(
                        png,
                        fmt,
                        shouldResize,
                        originalWidth,
                        originalHeight,
                        out width,
                        out height);

                break;
        }

        if (encoded == null ||
            encoded.Length == 0)
        {
            throw new InvalidDataException(
                $"Texture encoding produced no data for format {fmt}.");
        }

        /*
         * ------------------------------------------------------------
         * StreamData
         *
         * We are embedding the image data directly into the asset,
         * so the external .resS reference must be cleared.
         * ------------------------------------------------------------
         */

        AssetTypeValueField streamData =
            atvf["m_StreamData"];

        if (streamData != null &&
            !streamData.IsDummy)
        {
            AssetTypeValueField offsetField =
                streamData["offset"];

            AssetTypeValueField sizeField =
                streamData["size"];

            AssetTypeValueField pathField =
                streamData["path"];

            if (offsetField != null &&
                !offsetField.IsDummy)
            {
                switch (
                    offsetField.TemplateField.ValueType)
                {
                    case AssetValueType.Int64:
                        offsetField.AsLong = 0;
                        break;

                    case AssetValueType.UInt64:
                        offsetField.AsULong = 0;
                        break;

                    default:
                        offsetField.AsInt = 0;
                        break;
                }
            }

            if (sizeField != null &&
                !sizeField.IsDummy)
            {
                switch (
                    sizeField.TemplateField.ValueType)
                {
                    case AssetValueType.Int64:
                        sizeField.AsLong = 0;
                        break;

                    case AssetValueType.UInt64:
                        sizeField.AsULong = 0;
                        break;

                    case AssetValueType.UInt32:
                        sizeField.AsUInt = 0;
                        break;

                    default:
                        sizeField.AsInt = 0;
                        break;
                }
            }

            if (pathField != null &&
                !pathField.IsDummy)
            {
                pathField.AsString =
                    string.Empty;
            }
        }

        /*
         * ------------------------------------------------------------
         * Mipmap settings
         * ------------------------------------------------------------
         */

        AssetTypeValueField mipCountField =
            atvf["m_MipCount"];

        if (mipCountField != null &&
            !mipCountField.IsDummy)
        {
            mipCountField.AsInt = 1;
        }

        AssetTypeValueField mipMapField =
            atvf["m_MipMap"];

        if (mipMapField != null &&
            !mipMapField.IsDummy)
        {
            mipMapField.AsBool = false;
        }

        /*
         * ------------------------------------------------------------
         * Texture format
         * ------------------------------------------------------------
         */

        AssetTypeValueField textureFormatField =
            atvf["m_TextureFormat"];

        if (textureFormatField != null &&
            !textureFormatField.IsDummy)
        {
            textureFormatField.AsInt =
                (int)fmt;
        }

        /*
         * ------------------------------------------------------------
         * Complete image size
         * ------------------------------------------------------------
         */

        AssetTypeValueField completeImageSizeField =
            atvf["m_CompleteImageSize"];

        if (completeImageSizeField != null &&
            !completeImageSizeField.IsDummy)
        {
            completeImageSizeField.AsInt =
                encoded.Length;
        }

        /*
         * ------------------------------------------------------------
         * Dimensions
         * ------------------------------------------------------------
         */

        AssetTypeValueField finalWidthField =
            atvf["m_Width"];

        if (finalWidthField != null &&
            !finalWidthField.IsDummy)
        {
            finalWidthField.AsInt =
                width;
        }

        AssetTypeValueField finalHeightField =
            atvf["m_Height"];

        if (finalHeightField != null &&
            !finalHeightField.IsDummy)
        {
            finalHeightField.AsInt =
                height;
        }

        /*
         * ------------------------------------------------------------
         * Image data
         * ------------------------------------------------------------
         */

        AssetTypeValueField imageDataField =
            atvf["image data"];

        if (imageDataField == null ||
            imageDataField.IsDummy)
        {
            throw new InvalidDataException(
                "Texture2D does not contain an 'image data' field.");
        }

        if (imageDataField.TemplateField.ValueType ==
            AssetValueType.ByteArray)
        {
            imageDataField.AsByteArray =
                encoded;
        }
        else
        {
            /*
             * Compatibility fallback for TypeTrees where image data
             * is exposed as an array node.
             */
            imageDataField.AsArray =
                new AssetTypeArrayInfo(
                    encoded.Length);

            var children =
                new List<AssetTypeValueField>(
                    encoded.Length);

            for (int i = 0;
                 i < encoded.Length;
                 i++)
            {
                AssetTypeValueField child =
                    ValueBuilder.DefaultValueFieldFromArrayTemplate(
                        imageDataField);

                child.AsByte =
                    encoded[i];

                children.Add(
                    child);
            }

            imageDataField.Children =
                children;
        }

        DisplayStr(
            $"[PNG] Successfully inserted " +
            $"{encoded.Length:N0} bytes, " +
            $"{width}x{height}, " +
            $"format={fmt}, " +
            $"kind='{fileKind}'.");

        return true;
    }


    // ================================================================
    // ASSETSTOOLS.NET TEXTURE ENCODER
    // ================================================================

    private static byte[] ImportTextureWithAssetsTools(
        string file,
        TextureFormat format,
        bool resize,
        int originalWidth,
        int originalHeight,
        out int width,
        out int height)
    {
        using Image<Rgba32> image =
            Image.Load<Rgba32>(file);

        width =
            image.Width;

        height =
            image.Height;

        if (resize &&
            originalWidth > 0 &&
            originalHeight > 0 &&
            (originalWidth != width ||
             originalHeight != height))
        {
            image.Mutate(x =>
                x.Resize(
                    originalWidth,
                    originalHeight));

            width =
                originalWidth;

            height =
                originalHeight;
        }

        /*
         * Preserve the orientation used by the old importer.
         */
        image.Mutate(x =>
            x.Flip(
                FlipMode.Vertical));

        byte[] rgba =
            new byte[
                checked(
                    width *
                    height *
                    4)];

        image.CopyPixelDataTo(
            rgba);

        TextureFile textureFile =
            new TextureFile
            {
                m_Width =
                    width,

                m_Height =
                    height,

                m_TextureFormat =
                    (int)format,

                m_MipCount =
                    1,

                m_MipMap =
                    false
            };

        DisplayStr(
            $"[PNG] Encoding raw RGBA as {format} " +
            $"({width}x{height}) using " +
            $"AssetsTools.NET.Texture...");

        textureFile.EncodeTextureRaw(
            rgba,
            width,
            height,
            quality: 5,
            useBgra: false);

        byte[] encoded =
            textureFile.pictureData;

        if (encoded == null ||
            encoded.Length == 0)
        {
            throw new InvalidDataException(
                $"AssetsTools.NET failed to encode " +
                $"texture as {format}.");
        }

        width =
            textureFile.m_Width;

        height =
            textureFile.m_Height;

        DisplayStr(
            $"[PNG] AssetsTools.NET produced " +
            $"{encoded.Length:N0} bytes.");

        return encoded;
    }


    // ================================================================
    // DXT1 / BC1
    // ================================================================

    private static byte[] ImportDXT1Texture(
        string file,
        bool resize,
        int originalWidth,
        int originalHeight,
        out int width,
        out int height)
    {
        using Image<Rgba32> image =
            Image.Load<Rgba32>(file);

        width =
            image.Width;

        height =
            image.Height;

        if (resize &&
            originalWidth > 0 &&
            originalHeight > 0 &&
            (originalWidth != width ||
             originalHeight != height))
        {
            image.Mutate(x =>
                x.Resize(
                    originalWidth,
                    originalHeight));

            width =
                originalWidth;

            height =
                originalHeight;
        }

        image.Mutate(x =>
            x.Flip(
                FlipMode.Vertical));

        byte[] rgba =
            new byte[
                checked(
                    width *
                    height *
                    4)];

        image.CopyPixelDataTo(
            rgba);

        DisplayStr(
            $"[DXT1] Encoding " +
            $"{width}x{height} " +
            $"RGBA32 -> BC1/DXT1...");

        byte[] encoded =
            EncodeDxt1(
                rgba,
                width,
                height);

        int blocksX =
            (width + 3) / 4;

        int blocksY =
            (height + 3) / 4;

        int expectedSize =
            checked(
                blocksX *
                blocksY *
                8);

        if (encoded.Length !=
            expectedSize)
        {
            throw new InvalidDataException(
                $"DXT1 encoder generated " +
                $"{encoded.Length} bytes, " +
                $"expected {expectedSize}.");
        }

        DisplayStr(
            $"[DXT1] Encoded successfully: " +
            $"{encoded.Length:N0} bytes " +
            $"({blocksX}x{blocksY} blocks).");

        return encoded;
    }


    // ================================================================
    // DXT5 / BC3
    // ================================================================

    private static byte[] ImportDXT5Texture(
        string file,
        bool resize,
        int originalWidth,
        int originalHeight,
        out int width,
        out int height)
    {
        using Image<Rgba32> image =
            Image.Load<Rgba32>(file);

        width =
            image.Width;

        height =
            image.Height;

        if (resize &&
            originalWidth > 0 &&
            originalHeight > 0 &&
            (originalWidth != width ||
             originalHeight != height))
        {
            image.Mutate(x =>
                x.Resize(
                    originalWidth,
                    originalHeight));

            width =
                originalWidth;

            height =
                originalHeight;
        }

        image.Mutate(x =>
            x.Flip(
                FlipMode.Vertical));

        byte[] rgba =
            new byte[
                checked(
                    width *
                    height *
                    4)];

        image.CopyPixelDataTo(
            rgba);

        DisplayStr(
            $"[DXT5] Encoding " +
            $"{width}x{height} " +
            $"RGBA32 -> BC3/DXT5...");

        byte[] encoded =
            EncodeDxt5(
                rgba,
                width,
                height);

        int blocksX =
            (width + 3) / 4;

        int blocksY =
            (height + 3) / 4;

        int expectedSize =
            checked(
                blocksX *
                blocksY *
                16);

        if (encoded.Length !=
            expectedSize)
        {
            throw new InvalidDataException(
                $"DXT5 encoder generated " +
                $"{encoded.Length} bytes, " +
                $"expected {expectedSize}.");
        }

        DisplayStr(
            $"[DXT5] Encoded successfully: " +
            $"{encoded.Length:N0} bytes " +
            $"({blocksX}x{blocksY} blocks).");

        return encoded;
    }


    // ================================================================
    // DXT1 ENCODER
    // ================================================================

    private static byte[] EncodeDxt1(
        byte[] rgba,
        int width,
        int height)
    {
        if (rgba == null)
            throw new ArgumentNullException(
                nameof(rgba));

        if (rgba.Length <
            checked(
                width *
                height *
                4))
        {
            throw new ArgumentException(
                "RGBA buffer is smaller than " +
                "width*height*4.",
                nameof(rgba));
        }

        int blocksX =
            (width + 3) / 4;

        int blocksY =
            (height + 3) / 4;

        byte[] output =
            new byte[
                checked(
                    blocksX *
                    blocksY *
                    8)];

        int outputOffset =
            0;

        for (int by = 0;
             by < blocksY;
             by++)
        {
            for (int bx = 0;
                 bx < blocksX;
                 bx++)
            {
                EncodeDxt1Block(
                    rgba,
                    width,
                    height,
                    bx * 4,
                    by * 4,
                    output,
                    outputOffset);

                outputOffset +=
                    8;
            }
        }

        return output;
    }


    // ================================================================
    // DXT1 BLOCK
    // ================================================================

    private static void EncodeDxt1Block(
        byte[] rgba,
        int width,
        int height,
        int startX,
        int startY,
        byte[] output,
        int outputOffset)
    {
        Span<byte> r =
            stackalloc byte[16];

        Span<byte> g =
            stackalloc byte[16];

        Span<byte> b =
            stackalloc byte[16];

        Span<byte> a =
            stackalloc byte[16];

        bool hasAlpha =
            false;

        for (int py = 0;
             py < 4;
             py++)
        {
            int sourceY =
                Math.Min(
                    startY + py,
                    height - 1);

            for (int px = 0;
                 px < 4;
                 px++)
            {
                int sourceX =
                    Math.Min(
                        startX + px,
                        width - 1);

                int sourceOffset =
                    checked(
                        (sourceY * width +
                         sourceX) * 4);

                int index =
                    py * 4 + px;

                r[index] =
                    rgba[sourceOffset + 0];

                g[index] =
                    rgba[sourceOffset + 1];

                b[index] =
                    rgba[sourceOffset + 2];

                a[index] =
                    rgba[sourceOffset + 3];

                if (a[index] < 128)
                    hasAlpha = true;
            }
        }

        BuildDxt1ColorBlock(
            r,
            g,
            b,
            a,
            hasAlpha,
            output,
            outputOffset);
    }


    // ================================================================
    // DXT1 COLOR BLOCK
    // ================================================================

    private static void BuildDxt1ColorBlock(
        Span<byte> r,
        Span<byte> g,
        Span<byte> b,
        Span<byte> a,
        bool hasAlpha,
        byte[] output,
        int outputOffset)
    {
        int minR = 255;
        int minG = 255;
        int minB = 255;

        int maxR = 0;
        int maxG = 0;
        int maxB = 0;

        bool anyOpaque =
            false;

        for (int i = 0;
             i < 16;
             i++)
        {
            if (a[i] < 128)
                continue;

            anyOpaque =
                true;

            if (r[i] < minR)
                minR = r[i];

            if (g[i] < minG)
                minG = g[i];

            if (b[i] < minB)
                minB = b[i];

            if (r[i] > maxR)
                maxR = r[i];

            if (g[i] > maxG)
                maxG = g[i];

            if (b[i] > maxB)
                maxB = b[i];
        }

        if (!anyOpaque)
        {
            minR = 0;
            minG = 0;
            minB = 0;

            maxR = 0;
            maxG = 0;
            maxB = 0;

            hasAlpha =
                true;
        }

        if (maxR == minR &&
            maxG == minG &&
            maxB == minB)
        {
            maxR =
                Math.Min(
                    255,
                    maxR + 1);

            maxG =
                Math.Min(
                    255,
                    maxG + 1);

            maxB =
                Math.Min(
                    255,
                    maxB + 1);
        }

        ushort color0 =
            PackRgb565(
                maxR,
                maxG,
                maxB);

        ushort color1 =
            PackRgb565(
                minR,
                minG,
                minB);

        /*
         * Alpha-capable DXT1:
         * color0 <= color1
         *
         * Opaque DXT1:
         * color0 > color1
         */
        if (hasAlpha)
        {
            if (color0 > color1)
            {
                (color0, color1) =
                    (color1, color0);
            }
        }
        else
        {
            if (color0 < color1)
            {
                (color0, color1) =
                    (color1, color0);
            }

            if (color0 == color1)
            {
                if (color0 < 0xFFFF)
                    color0++;
                else if (color1 > 0)
                    color1--;
            }
        }

        Decode565(
            color0,
            out int c0R,
            out int c0G,
            out int c0B);

        Decode565(
            color1,
            out int c1R,
            out int c1G,
            out int c1B);

        Span<int> paletteR =
            stackalloc int[4];

        Span<int> paletteG =
            stackalloc int[4];

        Span<int> paletteB =
            stackalloc int[4];

        paletteR[0] = c0R;
        paletteG[0] = c0G;
        paletteB[0] = c0B;

        paletteR[1] = c1R;
        paletteG[1] = c1G;
        paletteB[1] = c1B;

        if (hasAlpha)
        {
            paletteR[2] =
                (c0R + c1R) / 2;

            paletteG[2] =
                (c0G + c1G) / 2;

            paletteB[2] =
                (c0B + c1B) / 2;

            paletteR[3] = 0;
            paletteG[3] = 0;
            paletteB[3] = 0;
        }
        else
        {
            paletteR[2] =
                (2 * c0R + c1R) / 3;

            paletteG[2] =
                (2 * c0G + c1G) / 3;

            paletteB[2] =
                (2 * c0B + c1B) / 3;

            paletteR[3] =
                (c0R + 2 * c1R) / 3;

            paletteG[3] =
                (c0G + 2 * c1G) / 3;

            paletteB[3] =
                (c0B + 2 * c1B) / 3;
        }

        uint indices =
            0;

        for (int i = 0;
             i < 16;
             i++)
        {
            int bestIndex =
                0;

            if (hasAlpha &&
                a[i] < 128)
            {
                bestIndex =
                    3;
            }
            else
            {
                int bestDistance =
                    int.MaxValue;

                int maxPalette =
                    hasAlpha
                        ? 3
                        : 4;

                for (int p = 0;
                     p < maxPalette;
                     p++)
                {
                    int dr =
                        r[i] -
                        paletteR[p];

                    int dg =
                        g[i] -
                        paletteG[p];

                    int db =
                        b[i] -
                        paletteB[p];

                    int distance =
                        dr * dr +
                        dg * dg * 2 +
                        db * db;

                    if (distance <
                        bestDistance)
                    {
                        bestDistance =
                            distance;

                        bestIndex =
                            p;
                    }
                }
            }

            indices |=
                (uint)(bestIndex & 3) <<
                (2 * i);
        }

        output[outputOffset + 0] =
            (byte)(color0 & 0xFF);

        output[outputOffset + 1] =
            (byte)(color0 >> 8);

        output[outputOffset + 2] =
            (byte)(color1 & 0xFF);

        output[outputOffset + 3] =
            (byte)(color1 >> 8);

        output[outputOffset + 4] =
            (byte)(indices & 0xFF);

        output[outputOffset + 5] =
            (byte)((indices >> 8) & 0xFF);

        output[outputOffset + 6] =
            (byte)((indices >> 16) & 0xFF);

        output[outputOffset + 7] =
            (byte)((indices >> 24) & 0xFF);
    }


    // ================================================================
    // DXT5 / BC3
    // ================================================================

    private static byte[] EncodeDxt5(
        byte[] rgba,
        int width,
        int height)
    {
        if (rgba == null)
            throw new ArgumentNullException(
                nameof(rgba));

        if (rgba.Length <
            checked(
                width *
                height *
                4))
        {
            throw new ArgumentException(
                "RGBA buffer is smaller than " +
                "width*height*4.",
                nameof(rgba));
        }

        int blocksX =
            (width + 3) / 4;

        int blocksY =
            (height + 3) / 4;

        byte[] output =
            new byte[
                checked(
                    blocksX *
                    blocksY *
                    16)];

        int outputOffset =
            0;

        for (int by = 0;
             by < blocksY;
             by++)
        {
            for (int bx = 0;
                 bx < blocksX;
                 bx++)
            {
                EncodeDxt5Block(
                    rgba,
                    width,
                    height,
                    bx * 4,
                    by * 4,
                    output,
                    outputOffset);

                outputOffset +=
                    16;
            }
        }

        return output;
    }


    // ================================================================
    // DXT5 BLOCK
    // ================================================================

    private static void EncodeDxt5Block(
        byte[] rgba,
        int width,
        int height,
        int startX,
        int startY,
        byte[] output,
        int outputOffset)
    {
        Span<byte> r =
            stackalloc byte[16];

        Span<byte> g =
            stackalloc byte[16];

        Span<byte> b =
            stackalloc byte[16];

        Span<byte> a =
            stackalloc byte[16];

        for (int py = 0;
             py < 4;
             py++)
        {
            int sourceY =
                Math.Min(
                    startY + py,
                    height - 1);

            for (int px = 0;
                 px < 4;
                 px++)
            {
                int sourceX =
                    Math.Min(
                        startX + px,
                        width - 1);

                int sourceOffset =
                    checked(
                        (sourceY * width +
                         sourceX) * 4);

                int index =
                    py * 4 + px;

                r[index] =
                    rgba[sourceOffset + 0];

                g[index] =
                    rgba[sourceOffset + 1];

                b[index] =
                    rgba[sourceOffset + 2];

                a[index] =
                    rgba[sourceOffset + 3];
            }
        }

        /*
         * ------------------------------------------------------------
         * Alpha block = 8 bytes
         * ------------------------------------------------------------
         */
        EncodeDxt5AlphaBlock(
            a,
            output,
            outputOffset);

        /*
         * ------------------------------------------------------------
         * Color block = normal DXT1 color portion
         *
         * DXT5 always uses the opaque four-color DXT1 mode for color.
         * Alpha belongs exclusively to the first 8 bytes.
         * ------------------------------------------------------------
         */
        BuildDxt1ColorBlock(
            r,
            g,
            b,
            a,
            false,
            output,
            outputOffset + 8);
    }


    // ================================================================
    // DXT5 ALPHA BLOCK
    // ================================================================

    private static void EncodeDxt5AlphaBlock(
        Span<byte> alpha,
        byte[] output,
        int outputOffset)
    {
        byte alpha0 =
            0;

        byte alpha1 =
            255;

        int minAlpha =
            255;

        int maxAlpha =
            0;

        for (int i = 0;
             i < 16;
             i++)
        {
            int value =
                alpha[i];

            if (value < minAlpha)
                minAlpha = value;

            if (value > maxAlpha)
                maxAlpha = value;
        }

        /*
         * Use the observed extrema as endpoints.
         */
        alpha0 =
            (byte)maxAlpha;

        alpha1 =
            (byte)minAlpha;

        /*
         * DXT5 alpha has two interpolation modes:
         *
         * alpha0 > alpha1:
         *   8 alpha values
         *
         * alpha0 <= alpha1:
         *   6 alpha values + 0 + 255
         *
         * We normally use the 8-value mode because it provides better
         * precision for ordinary textures.
         *
         * Force a valid strictly descending pair.
         */
        if (alpha0 == alpha1)
        {
            if (alpha0 < 255)
                alpha0++;
            else if (alpha1 > 0)
                alpha1--;
        }

        if (alpha0 < alpha1)
        {
            (alpha0, alpha1) =
                (alpha1, alpha0);
        }

        Span<int> palette =
            stackalloc int[8];

        palette[0] =
            alpha0;

        palette[1] =
            alpha1;

        /*
         * Because alpha0 > alpha1:
         *
         * a2 = 6/7 a0 + 1/7 a1
         * a3 = 5/7 a0 + 2/7 a1
         * a4 = 4/7 a0 + 3/7 a1
         * a5 = 3/7 a0 + 4/7 a1
         * a6 = 2/7 a0 + 5/7 a1
         * a7 = 1/7 a0 + 6/7 a1
         */
        palette[2] =
            (6 * alpha0 +
             alpha1) / 7;

        palette[3] =
            (5 * alpha0 +
             2 * alpha1) / 7;

        palette[4] =
            (4 * alpha0 +
             3 * alpha1) / 7;

        palette[5] =
            (3 * alpha0 +
             4 * alpha1) / 7;

        palette[6] =
            (2 * alpha0 +
             5 * alpha1) / 7;

        palette[7] =
            (alpha0 +
             6 * alpha1) / 7;

        /*
         * First two bytes are endpoints.
         */
        output[outputOffset + 0] =
            alpha0;

        output[outputOffset + 1] =
            alpha1;

        /*
         * Six bytes hold 16 × 3-bit indices.
         */
        ulong indices =
            0;

        for (int i = 0;
             i < 16;
             i++)
        {
            int bestIndex =
                0;

            int bestDistance =
                int.MaxValue;

            for (int p = 0;
                 p < 8;
                 p++)
            {
                int difference =
                    alpha[i] -
                    palette[p];

                int distance =
                    difference *
                    difference;

                if (distance <
                    bestDistance)
                {
                    bestDistance =
                        distance;

                    bestIndex =
                        p;
                }
            }

            indices |=
                (ulong)(bestIndex & 7) <<
                (3 * i);
        }

        /*
         * DXT5 alpha indices are serialized little-endian.
         */
        output[outputOffset + 2] =
            (byte)(indices & 0xFF);

        output[outputOffset + 3] =
            (byte)((indices >> 8) & 0xFF);

        output[outputOffset + 4] =
            (byte)((indices >> 16) & 0xFF);

        output[outputOffset + 5] =
            (byte)((indices >> 24) & 0xFF);

        output[outputOffset + 6] =
            (byte)((indices >> 32) & 0xFF);

        output[outputOffset + 7] =
            (byte)((indices >> 40) & 0xFF);
    }

	// ================================================================
	// BC7 / MODE 6
	//
	// Encoder BC7 completamente managed.
	// Nessuna dipendenza esterna aggiuntiva.
	//
	// Mode 6:
	//   - 1 subset
	//   - RGBA combinato
	//   - endpoint RGBAP 7.7.7.7.1
	//   - 16 indici da 4 bit
	//   - indice 0 con 3 bit effettivi (fix-up bit implicito)
	//
	// Layout:
	//   mode      = 7 bits
	//   R0/R1     = 7 + 7
	//   G0/G1     = 7 + 7
	//   B0/B1     = 7 + 7
	//   A0/A1     = 7 + 7
	//   P0/P1     = 1 + 1
	//   indices   = 3 + 15*4
	//
	// Totale = 128 bit = 16 byte.
	// ================================================================

	private static byte[] ImportBC7Texture(
		string file,
		bool resize,
		int originalWidth,
		int originalHeight,
		out int width,
		out int height)
	{
		using Image<Rgba32> image =
			Image.Load<Rgba32>(file);

		width =
			image.Width;

		height =
			image.Height;

		if (resize &&
			originalWidth > 0 &&
			originalHeight > 0 &&
			(originalWidth != width ||
			 originalHeight != height))
		{
			image.Mutate(x =>
				x.Resize(
					originalWidth,
					originalHeight));

			width =
				originalWidth;

			height =
				originalHeight;
		}

		/*
		 * Mantiene lo stesso orientamento usato dagli
		 * encoder DXT1/DXT5 esistenti.
		 */
		image.Mutate(x =>
			x.Flip(
				FlipMode.Vertical));

		byte[] rgba =
			new byte[
				checked(
					width *
					height *
					4)];

		image.CopyPixelDataTo(
			rgba);

		DisplayStr(
			$"[BC7] Encoding " +
			$"{width}x{height} " +
			$"RGBA32 -> BC7 Mode 6 " +
			$"(managed, no external encoder)...");

		byte[] encoded =
			EncodeBC7Mode6(
				rgba,
				width,
				height);

		int blocksX =
			(width + 3) / 4;

		int blocksY =
			(height + 3) / 4;

		int expectedSize =
			checked(
				blocksX *
				blocksY *
				16);

		if (encoded == null ||
			encoded.Length != expectedSize)
		{
			throw new InvalidDataException(
				$"BC7 encoder generated " +
				$"{encoded?.Length ?? 0} bytes, " +
				$"expected {expectedSize}.");
		}

		DisplayStr(
			$"[BC7] Encoded successfully: " +
			$"{encoded.Length:N0} bytes " +
			$"({blocksX}x{blocksY} blocks, " +
			$"Mode 6).");

		return encoded;
	}


	// ================================================================
	// BC7 MODE 6 ENCODER
	// ================================================================

	private static byte[] EncodeBC7Mode6(
		byte[] rgba,
		int width,
		int height)
	{
		if (rgba == null)
			throw new ArgumentNullException(
				nameof(rgba));

		if (width <= 0 ||
			height <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(width),
				"Texture dimensions must be positive.");
		}

		int requiredBytes =
			checked(
				width *
				height *
				4);

		if (rgba.Length < requiredBytes)
		{
			throw new ArgumentException(
				"RGBA buffer is smaller than " +
				"width*height*4.",
				nameof(rgba));
		}

		int blocksX =
			(width + 3) / 4;

		int blocksY =
			(height + 3) / 4;

		byte[] output =
			new byte[
				checked(
					blocksX *
					blocksY *
					16)];

		int outputOffset =
			0;

		for (int by = 0;
			 by < blocksY;
			 by++)
		{
			for (int bx = 0;
				 bx < blocksX;
				 bx++)
			{
				EncodeBC7Mode6Block(
					rgba,
					width,
					height,
					bx * 4,
					by * 4,
					output,
					outputOffset);

				outputOffset +=
					16;
			}
		}

		return output;
	}


	// ================================================================
	// BC7 MODE 6 BLOCK
	// ================================================================

	private static void EncodeBC7Mode6Block(
		byte[] rgba,
		int width,
		int height,
		int startX,
		int startY,
		byte[] output,
		int outputOffset)
	{
		Span<byte> r =
			stackalloc byte[16];

		Span<byte> g =
			stackalloc byte[16];

		Span<byte> b =
			stackalloc byte[16];

		Span<byte> a =
			stackalloc byte[16];

		/*
		 * BC7/DXT-style edge handling:
		 * pixels outside the texture repeat the last valid pixel.
		 */
		for (int py = 0;
			 py < 4;
			 py++)
		{
			int sourceY =
				Math.Min(
					startY + py,
					height - 1);

			for (int px = 0;
				 px < 4;
				 px++)
			{
				int sourceX =
					Math.Min(
						startX + px,
						width - 1);

				int sourceOffset =
					checked(
						(sourceY * width +
						 sourceX) * 4);

				int index =
					py * 4 + px;

				r[index] =
					rgba[sourceOffset + 0];

				g[index] =
					rgba[sourceOffset + 1];

				b[index] =
					rgba[sourceOffset + 2];

				a[index] =
					rgba[sourceOffset + 3];
			}
		}

		/*
		 * ------------------------------------------------------------
		 * Initial endpoints
		 *
		 * Mode 6 has one endpoint pair.
		 * We use the per-channel bounding box:
		 *
		 * endpoint 0 = minima
		 * endpoint 1 = maxima
		 *
		 * This is intentionally simple and deterministic.
		 * ------------------------------------------------------------
		 */

		int minR = 255;
		int minG = 255;
		int minB = 255;
		int minA = 255;

		int maxR = 0;
		int maxG = 0;
		int maxB = 0;
		int maxA = 0;

		for (int i = 0;
			 i < 16;
			 i++)
		{
			if (r[i] < minR)
				minR = r[i];

			if (g[i] < minG)
				minG = g[i];

			if (b[i] < minB)
				minB = b[i];

			if (a[i] < minA)
				minA = a[i];

			if (r[i] > maxR)
				maxR = r[i];

			if (g[i] > maxG)
				maxG = g[i];

			if (b[i] > maxB)
				maxB = b[i];

			if (a[i] > maxA)
				maxA = a[i];
		}

		Bc7Endpoint endpoint0 =
			QuantizeBC7Endpoint(
				minR,
				minG,
				minB,
				minA);

		Bc7Endpoint endpoint1 =
			QuantizeBC7Endpoint(
				maxR,
				maxG,
				maxB,
				maxA);

		/*
		 * ------------------------------------------------------------
		 * Find best 4-bit index for every pixel.
		 * ------------------------------------------------------------
		 */

		Span<byte> indices =
			stackalloc byte[16];

		for (int i = 0;
			 i < 16;
			 i++)
		{
			int bestIndex =
				0;

			int bestError =
				int.MaxValue;

			for (int index = 0;
				 index < 16;
				 index++)
			{
				GetBC7InterpolatedColor(
					endpoint0,
					endpoint1,
					index,
					out int pr,
					out int pg,
					out int pb,
					out int pa);

				int dr =
					r[i] - pr;

				int dg =
					g[i] - pg;

				int db =
					b[i] - pb;

				int da =
					a[i] - pa;

				/*
				 * Alpha ha lo stesso peso di RGB.
				 * Il calcolo rimane ampiamente entro int.
				 */
				int error =
					checked(
						dr * dr +
						dg * dg +
						db * db +
						da * da);

				if (error < bestError)
				{
					bestError =
						error;

					bestIndex =
						index;
				}
			}

			indices[i] =
				(byte)bestIndex;
		}

		/*
		 * ------------------------------------------------------------
		 * BC7 Mode 6 fix-up
		 *
		 * L'indice del texel 0 deve avere MSB = 0.
		 * Con indici a 4 bit significa:
		 *
		 *     index[0] <= 7
		 *
		 * Se non lo è, invertiamo endpoint e indici:
		 *
		 *     index' = 15 - index
		 *
		 * mantenendo identica la palette.
		 * ------------------------------------------------------------
		 */

		if (indices[0] >= 8)
		{
			(endpoint0, endpoint1) =
				(endpoint1, endpoint0);

			for (int i = 0;
				 i < 16;
				 i++)
			{
				indices[i] =
					(byte)(15 - indices[i]);
			}
		}

		/*
		 * ------------------------------------------------------------
		 * Serialize 128-bit BC7 block.
		 *
		 * BC7 uses an LSB-first bitstream.
		 * ------------------------------------------------------------
		 */

		Span<byte> block =
			output.AsSpan(
				outputOffset,
				16);

		block.Clear();

		int bitOffset =
			0;

		/*
		 * Mode 6:
		 *
		 * 0000001 if viewed MSB-first,
		 * therefore numerical bit pattern = 1 << 6.
		 */
		WriteBC7Bits(
			block,
			ref bitOffset,
			1u << 6,
			7);

		/*
		 * Endpoint channel order is:
		 *
		 * R0 R1
		 * G0 G1
		 * B0 B1
		 * A0 A1
		 */

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint0.R7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint1.R7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint0.G7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint1.G7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint0.B7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint1.B7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint0.A7,
			7);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint1.A7,
			7);

		/*
		 * Unique P-bit per endpoint.
		 */
		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint0.P,
			1);

		WriteBC7Bits(
			block,
			ref bitOffset,
			(uint)endpoint1.P,
			1);

		/*
		 * Fix-up index 0:
		 * only the lower 3 bits are physically stored.
		 */
		WriteBC7Bits(
			block,
			ref bitOffset,
			indices[0],
			3);

		/*
		 * Remaining indices:
		 * full 4 bits.
		 */
		for (int i = 1;
			 i < 16;
			 i++)
		{
			WriteBC7Bits(
				block,
				ref bitOffset,
				indices[i],
				4);
		}

		if (bitOffset != 128)
		{
			throw new InvalidDataException(
				$"BC7 Mode 6 serializer produced " +
				$"{bitOffset} bits instead of 128.");
		}
	}


	// ================================================================
	// BC7 ENDPOINT
	// ================================================================

	private struct Bc7Endpoint
	{
		public int R7;
		public int G7;
		public int B7;
		public int A7;
		public int P;

		public int R;
		public int G;
		public int B;
		public int A;
	}


	// ================================================================
	// BC7 ENDPOINT QUANTIZATION
	// ================================================================
	//
	// Mode 6 stores 7 bits + one P-bit for each endpoint.
	//
	// Reconstructed 8-bit component:
	//
	//     reconstructed = (stored7 << 1) | P
	//
	// The P-bit is shared by R/G/B/A of the same endpoint,
	// so we test both possible P values and choose the one with
	// the smallest total endpoint error.
	// ================================================================

	private static Bc7Endpoint QuantizeBC7Endpoint(
		int r,
		int g,
		int b,
		int a)
	{
		long bestError =
			long.MaxValue;

		Bc7Endpoint best =
			default;

		for (int p = 0;
			 p <= 1;
			 p++)
		{
			int r7 =
				QuantizeBC7Component(
					r,
					p);

			int g7 =
				QuantizeBC7Component(
					g,
					p);

			int b7 =
				QuantizeBC7Component(
					b,
					p);

			int a7 =
				QuantizeBC7Component(
					a,
					p);

			int rr =
				(r7 << 1) | p;

			int gg =
				(g7 << 1) | p;

			int bb =
				(b7 << 1) | p;

			int aa =
				(a7 << 1) | p;

			long dr =
				r - rr;

			long dg =
				g - gg;

			long db =
				b - bb;

			long da =
				a - aa;

			long error =
				dr * dr +
				dg * dg +
				db * db +
				da * da;

			if (error < bestError)
			{
				bestError =
					error;

				best.R7 =
					r7;

				best.G7 =
					g7;

				best.B7 =
					b7;

				best.A7 =
					a7;

				best.P =
					p;

				best.R =
					rr;

				best.G =
					gg;

				best.B =
					bb;

				best.A =
					aa;
			}
		}

		return best;
	}


	private static int QuantizeBC7Component(
		int value,
		int p)
	{
		/*
		 * Nearest value in:
		 *
		 *     (7-bit << 1) | p
		 *
		 * We use integer arithmetic to avoid floating point
		 * differences.
		 */
		int q =
			(value - p + 1) / 2;

		if (q < 0)
			q = 0;

		if (q > 127)
			q = 127;

		return q;
	}


	// ================================================================
	// BC7 INTERPOLATION
	// ================================================================

	private static void GetBC7InterpolatedColor(
		Bc7Endpoint endpoint0,
		Bc7Endpoint endpoint1,
		int index,
		out int r,
		out int g,
		out int b,
		out int a)
	{
		/*
		 * BC7 4-bit interpolation weights.
		 *
		 * Index:
		 *  0  1  2  3  4  5  6  7
		 *  8  9 10 11 12 13 14 15
		 */

		int w =
			Bc7Weights4[index];

		r =
			((64 - w) * endpoint0.R +
			 w * endpoint1.R +
			 32) >> 6;

		g =
			((64 - w) * endpoint0.G +
			 w * endpoint1.G +
			 32) >> 6;

		b =
			((64 - w) * endpoint0.B +
			 w * endpoint1.B +
			 32) >> 6;

		a =
			((64 - w) * endpoint0.A +
			 w * endpoint1.A +
			 32) >> 6;
	}


	// ================================================================
	// BC7 BIT WRITER
	// ================================================================

	private static void WriteBC7Bits(
		Span<byte> buffer,
		ref int bitOffset,
		uint value,
		int bitCount)
	{
		if (bitCount < 1 ||
			bitCount > 32)
		{
			throw new ArgumentOutOfRangeException(
				nameof(bitCount));
		}

		if (bitOffset < 0 ||
			bitOffset + bitCount >
			buffer.Length * 8)
		{
			throw new InvalidDataException(
				$"BC7 bit write outside block: " +
				$"offset={bitOffset}, " +
				$"count={bitCount}.");
		}

		for (int i = 0;
			 i < bitCount;
			 i++)
		{
			if (((value >> i) & 1u) != 0)
			{
				int targetBit =
					bitOffset + i;

				int byteIndex =
					targetBit >> 3;

				int bitInByte =
					targetBit & 7;

				buffer[byteIndex] |=
					(byte)(1 << bitInByte);
			}
		}

		bitOffset +=
			bitCount;
	}


	// ================================================================
	// RGB565
	// ================================================================

	private static ushort PackRgb565(
        int r,
        int g,
        int b)
    {
        int r5 =
            (r * 31 + 127) /
            255;

        int g6 =
            (g * 63 + 127) /
            255;

        int b5 =
            (b * 31 + 127) /
            255;

        return (ushort)(
            (r5 << 11) |
            (g6 << 5) |
            b5);
    }


    private static void Decode565(
        ushort color,
        out int r,
        out int g,
        out int b)
    {
        int r5 =
            (color >> 11) & 0x1F;

        int g6 =
            (color >> 5) & 0x3F;

        int b5 =
            color & 0x1F;

        r =
            (r5 * 255 + 15) /
            31;

        g =
            (g6 * 255 + 31) /
            63;

        b =
            (b5 * 255 + 15) /
            31;
    }
}
