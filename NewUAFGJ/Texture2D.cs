using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;

namespace UAFGJ;

partial class Program
{
    private static bool ImportTexturesCustom(
        ref AssetTypeValueField atvf,
        string png,
        int format,
        string fileKind)
    {
        if (atvf == null)
            throw new ArgumentNullException(nameof(atvf));

        if (string.IsNullOrWhiteSpace(png))
            throw new ArgumentException("PNG path is empty.", nameof(png));

        if (!File.Exists(png))
            throw new FileNotFoundException(
                "PNG file was not found.",
                png);

        TextureFormat fmt = (TextureFormat)format;

        int originalWidth = 0;
        int originalHeight = 0;

        AssetTypeValueField widthField = atvf["m_Width"];
        AssetTypeValueField heightField = atvf["m_Height"];

        if (widthField != null && !widthField.IsDummy)
            originalWidth = widthField.AsInt;

        if (heightField != null && !heightField.IsDummy)
            originalHeight = heightField.AsInt;

        bool shouldResize =
            !png.Contains("FOT", StringComparison.OrdinalIgnoreCase) &&
            !png.Contains("HOT", StringComparison.OrdinalIgnoreCase) &&
            !png.Contains("Atlas", StringComparison.OrdinalIgnoreCase);

        DisplayStr(
            $"[PNG] Importing '{Path.GetFileName(png)}' " +
            $"as {fmt}, original={originalWidth}x{originalHeight}, " +
            $"resize={shouldResize}.");

        byte[] encoded;

        int width;
        int height;

        /*
         * DXT1 is handled by our own BC1 encoder.
         *
         * This deliberately avoids TextureFile.EncodeTextureRaw()
         * because the managed encoder in the installed package does
         * not support DXT1 when its native encoder is unavailable.
         */
        if (fmt == TextureFormat.DXT1)
        {
            encoded = ImportDXT1Texture(
                png,
                shouldResize,
                originalWidth,
                originalHeight,
                out width,
                out height);
        }
        else
        {
            encoded = ImportTextureWithAssetsTools(
                png,
                fmt,
                shouldResize,
                originalWidth,
                originalHeight,
                out width,
                out height);
        }

        if (encoded == null || encoded.Length == 0)
        {
            throw new InvalidDataException(
                $"Texture encoding produced no data for format {fmt}.");
        }

        /*
         * ------------------------------------------------------------
         * Update Texture2D fields.
         * ------------------------------------------------------------
         */

        AssetTypeValueField streamData = atvf["m_StreamData"];

        if (streamData != null && !streamData.IsDummy)
        {
            AssetTypeValueField offsetField = streamData["offset"];
            AssetTypeValueField sizeField = streamData["size"];
            AssetTypeValueField pathField = streamData["path"];

            if (offsetField != null && !offsetField.IsDummy)
            {
                offsetField.AsULong = 0;
            }

            if (sizeField != null && !sizeField.IsDummy)
            {
                sizeField.AsUInt = 0;
            }

            if (pathField != null && !pathField.IsDummy)
            {
                pathField.AsString = string.Empty;
            }
        }

        /*
         * One mipmap only.
         */
        AssetTypeValueField mipCountField = atvf["m_MipCount"];

        if (mipCountField != null && !mipCountField.IsDummy)
        {
            mipCountField.AsInt = 1;
        }

        AssetTypeValueField mipMapField = atvf["m_MipMap"];

        if (mipMapField != null && !mipMapField.IsDummy)
        {
            mipMapField.AsBool = false;
        }

        /*
         * Texture format.
         */
        AssetTypeValueField textureFormatField =
            atvf["m_TextureFormat"];

        if (textureFormatField != null &&
            !textureFormatField.IsDummy)
        {
            textureFormatField.AsInt = (int)fmt;
        }

        /*
         * Complete encoded image size.
         */
        AssetTypeValueField completeImageSizeField =
            atvf["m_CompleteImageSize"];

        if (completeImageSizeField != null &&
            !completeImageSizeField.IsDummy)
        {
            completeImageSizeField.AsInt = encoded.Length;
        }

        /*
         * Width / height.
         *
         * For DXT1 the logical dimensions remain the original image
         * dimensions. The encoder internally pads the last blocks.
         */
        AssetTypeValueField finalWidthField = atvf["m_Width"];

        if (finalWidthField != null &&
            !finalWidthField.IsDummy)
        {
            finalWidthField.AsInt = width;
        }

        AssetTypeValueField finalHeightField = atvf["m_Height"];

        if (finalHeightField != null &&
            !finalHeightField.IsDummy)
        {
            finalHeightField.AsInt = height;
        }

        /*
         * image data.
         */
        AssetTypeValueField imageDataField =
            atvf["image data"];

        if (imageDataField == null ||
            imageDataField.IsDummy)
        {
            throw new InvalidDataException(
                "Texture2D does not contain an 'image data' field.");
        }

        /*
         * Most Unity Texture2D TypeTrees expose image data as byte[].
         */
        if (imageDataField.TemplateField.ValueType ==
            AssetValueType.ByteArray)
        {
            imageDataField.AsByteArray = encoded;
        }
        else
        {
            imageDataField.AsArray =
                new AssetTypeArrayInfo(encoded.Length);

            var children =
                new System.Collections.Generic.List<AssetTypeValueField>(
                    encoded.Length);

            for (int i = 0; i < encoded.Length; i++)
            {
                AssetTypeValueField child =
                    ValueBuilder.DefaultValueFieldFromArrayTemplate(
                        imageDataField);

                child.AsByte = encoded[i];
                children.Add(child);
            }

            imageDataField.Children = children;
        }

        DisplayStr(
            $"[PNG] Successfully inserted " +
            $"{encoded.Length:N0} bytes, " +
            $"{width}x{height}, format={fmt}, kind='{fileKind}'.");

        return true;
    }

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

        width = image.Width;
        height = image.Height;

        if (resize &&
            originalWidth > 0 &&
            originalHeight > 0 &&
            (originalWidth != width ||
             originalHeight != height))
        {
            image.Mutate(x =>
                x.Resize(originalWidth, originalHeight));

            width = originalWidth;
            height = originalHeight;
        }

        /*
         * Unity textures are stored vertically flipped relative to
         * the ImageSharp representation used here.
         */
        image.Mutate(x =>
            x.Flip(FlipMode.Vertical));

        byte[] rgba =
            new byte[checked(width * height * 4)];

        image.CopyPixelDataTo(rgba);

        TextureFile textureFile = new TextureFile
        {
            m_Width = width,
            m_Height = height,
            m_TextureFormat = (int)format,
            m_MipCount = 1,
            m_MipMap = false
        };

        DisplayStr(
            $"[PNG] Encoding raw RGBA as {format} " +
            $"({width}x{height}) using AssetsTools.NET.Texture...");

        /*
         * This is the actual signature exposed by your installed
         * AssetsTools.NET.Texture assembly.
         *
         * EncodeTextureRaw(
         *     byte[] textureData,
         *     int width,
         *     int height,
         *     int quality = 3,
         *     bool useBgra = true)
         */
        textureFile.EncodeTextureRaw(
            rgba,
            width,
            height,
            quality: 5,
            useBgra: false);

        byte[] encoded = textureFile.pictureData;

        if (encoded == null || encoded.Length == 0)
        {
            throw new InvalidDataException(
                $"AssetsTools.NET failed to encode texture as {format}.");
        }

        width = textureFile.m_Width;
        height = textureFile.m_Height;

        DisplayStr(
            $"[PNG] AssetsTools.NET produced " +
            $"{encoded.Length:N0} bytes.");

        return encoded;
    }

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

        width = image.Width;
        height = image.Height;

        if (resize &&
            originalWidth > 0 &&
            originalHeight > 0 &&
            (originalWidth != width ||
             originalHeight != height))
        {
            image.Mutate(x =>
                x.Resize(originalWidth, originalHeight));

            width = originalWidth;
            height = originalHeight;
        }

        /*
         * Same orientation handling as the rest of the importer.
         */
        image.Mutate(x =>
            x.Flip(FlipMode.Vertical));

        byte[] rgba =
            new byte[checked(width * height * 4)];

        image.CopyPixelDataTo(rgba);

        DisplayStr(
            $"[DXT1] Encoding {width}x{height} RGBA32 -> BC1/DXT1...");

        byte[] encoded =
            EncodeDxt1(rgba, width, height);

        int blocksX =
            (width + 3) / 4;

        int blocksY =
            (height + 3) / 4;

        int expectedSize =
            checked(blocksX * blocksY * 8);

        if (encoded.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"DXT1 encoder generated {encoded.Length} bytes, " +
                $"expected {expectedSize}.");
        }

        DisplayStr(
            $"[DXT1] Encoded successfully: " +
            $"{encoded.Length:N0} bytes " +
            $"({blocksX}x{blocksY} blocks).");

        return encoded;
    }

    /*
     * ================================================================
     * DXT1 / BC1 ENCODER
     * ================================================================
     *
     * DXT1 stores each 4x4 block in exactly 8 bytes:
     *
     *   uint16 color0
     *   uint16 color1
     *   uint32 color indices
     *
     * This implementation supports both the opaque four-color mode
     * and the one-bit-alpha three-color mode.
     */
    private static byte[] EncodeDxt1(
        byte[] rgba,
        int width,
        int height)
    {
        if (rgba == null)
            throw new ArgumentNullException(nameof(rgba));

        if (rgba.Length <
            checked(width * height * 4))
        {
            throw new ArgumentException(
                "RGBA buffer is smaller than width*height*4.",
                nameof(rgba));
        }

        int blocksX =
            (width + 3) / 4;

        int blocksY =
            (height + 3) / 4;

        byte[] output =
            new byte[checked(blocksX * blocksY * 8)];

        int outputOffset = 0;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                EncodeDxt1Block(
                    rgba,
                    width,
                    height,
                    bx * 4,
                    by * 4,
                    output,
                    outputOffset);

                outputOffset += 8;
            }
        }

        return output;
    }

    private static void EncodeDxt1Block(
        byte[] rgba,
        int width,
        int height,
        int startX,
        int startY,
        byte[] output,
        int outputOffset)
    {
        /*
         * Read 16 pixels. Pixels outside the image are replicated
         * from the edge, which avoids introducing arbitrary black
         * borders into the last compressed block.
         */
        Span<byte> r =
            stackalloc byte[16];

        Span<byte> g =
            stackalloc byte[16];

        Span<byte> b =
            stackalloc byte[16];

        Span<byte> a =
            stackalloc byte[16];

        bool hasAlpha = false;

        for (int py = 0; py < 4; py++)
        {
            int sourceY =
                Math.Min(startY + py, height - 1);

            for (int px = 0; px < 4; px++)
            {
                int sourceX =
                    Math.Min(startX + px, width - 1);

                int sourceOffset =
                    checked((sourceY * width + sourceX) * 4);

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

        /*
         * Find RGB bounding box.
         */
        int minR = 255;
        int minG = 255;
        int minB = 255;

        int maxR = 0;
        int maxG = 0;
        int maxB = 0;

        for (int i = 0; i < 16; i++)
        {
            if (a[i] < 128)
                continue;

            if (r[i] < minR) minR = r[i];
            if (g[i] < minG) minG = g[i];
            if (b[i] < minB) minB = b[i];

            if (r[i] > maxR) maxR = r[i];
            if (g[i] > maxG) maxG = g[i];
            if (b[i] > maxB) maxB = b[i];
        }

        /*
         * Fully transparent block.
         */
        bool anyOpaque = false;

        for (int i = 0; i < 16; i++)
        {
            if (a[i] >= 128)
            {
                anyOpaque = true;
                break;
            }
        }

        if (!anyOpaque)
        {
            minR = 0;
            minG = 0;
            minB = 0;

            maxR = 0;
            maxG = 0;
            maxB = 0;

            hasAlpha = true;
        }

        /*
         * Expand tiny color ranges slightly to reduce quantization
         * collapse.
         */
        if (maxR == minR &&
            maxG == minG &&
            maxB == minB)
        {
            maxR = Math.Min(255, maxR + 1);
            maxG = Math.Min(255, maxG + 1);
            maxB = Math.Min(255, maxB + 1);
        }

        ushort color0 =
            PackRgb565(maxR, maxG, maxB);

        ushort color1 =
            PackRgb565(minR, minG, minB);

        /*
         * DXT1 alpha mode requires color0 <= color1.
         *
         * In opaque mode we want the normal four-color ordering:
         * color0 > color1.
         *
         * In alpha mode color0 <= color1 enables:
         *
         *   color2 = 1/2 color0 + 1/2 color1
         *   color3 = transparent
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

        uint indices = 0;

        for (int i = 0; i < 16; i++)
        {
            int bestIndex = 0;

            if (hasAlpha &&
                a[i] < 128)
            {
                bestIndex = 3;
            }
            else
            {
                int bestDistance =
                    int.MaxValue;

                int maxPalette =
                    hasAlpha ? 3 : 4;

                for (int p = 0; p < maxPalette; p++)
                {
                    int dr =
                        r[i] - paletteR[p];

                    int dg =
                        g[i] - paletteG[p];

                    int db =
                        b[i] - paletteB[p];

                    /*
                     * Give green slightly more weight because RGB565
                     * carries one extra green bit.
                     */
                    int distance =
                        dr * dr +
                        dg * dg * 2 +
                        db * db;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = p;
                    }
                }
            }

            indices |=
                (uint)(bestIndex & 3)
                << (2 * i);
        }

        /*
         * Write little-endian DXT1 block.
         */
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

    private static ushort PackRgb565(
        int r,
        int g,
        int b)
    {
        int r5 =
            (r * 31 + 127) / 255;

        int g6 =
            (g * 63 + 127) / 255;

        int b5 =
            (b * 31 + 127) / 255;

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
            (r5 * 255 + 15) / 31;

        g =
            (g6 * 255 + 31) / 63;

        b =
            (b5 * 255 + 15) / 31;
    }
}
