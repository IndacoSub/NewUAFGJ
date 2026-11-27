namespace NewUAFGJ;

internal static class TextureDownsampling
{
    private static readonly double[] Linear = Enumerable.Range(0, 256).Select(value => (double)value).ToArray();
    private static readonly double[] Srgb = Enumerable.Range(0, 256).Select(Linearize).ToArray();

    internal static byte[] Resize(ReadOnlySpan<byte> source, (int Width, int Height) sourceSize, TextureEncoding encoding)
    {
        int width = encoding.Width;
        int height = encoding.Height;
        int sourceWidth = sourceSize.Width;
        int sourceHeight = sourceSize.Height;
        bool srgb = encoding.Resampling == PngResampling.Srgb;
        double[] channels = srgb ? Srgb : Linear;
        byte[] result = new byte[checked(width * height * 4)];
        long area = (long)sourceWidth * sourceHeight;

        // Measure pixel overlaps in integer units to preserve fractional coverage at noninteger ratios.
        for (int y = 0; y < height; y++)
        {
            long top = (long)y * sourceHeight;
            long bottom = top + sourceHeight;
            int firstRow = (int)(top / height);
            int lastRow = (int)((bottom - 1) / height);
            for (int x = 0; x < width; x++)
            {
                long left = (long)x * sourceWidth;
                long right = left + sourceWidth;
                int firstColumn = (int)(left / width);
                int lastColumn = (int)((right - 1) / width);
                long alpha = 0;
                double red = 0, green = 0, blue = 0;
                for (int row = firstRow; row <= lastRow; row++)
                {
                    long rowTop = (long)row * height;
                    long overlapY = Math.Min(bottom, rowTop + height) - Math.Max(top, rowTop);
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        long columnLeft = (long)column * width;
                        long overlapX = Math.Min(right, columnLeft + width) - Math.Max(left, columnLeft);
                        int pixel = (row * sourceWidth + column) * 4;
                        long coverage = overlapX * overlapY * source[pixel + 3];
                        alpha += coverage;
                        red += coverage * channels[source[pixel]];
                        green += coverage * channels[source[pixel + 1]];
                        blue += coverage * channels[source[pixel + 2]];
                    }
                }

                int destination = (y * width + x) * 4;
                result[destination + 3] = (byte)((alpha + area / 2) / area);
                if (result[destination + 3] == 0)
                    continue;
                result[destination] = EncodeChannel(red / alpha, srgb);
                result[destination + 1] = EncodeChannel(green / alpha, srgb);
                result[destination + 2] = EncodeChannel(blue / alpha, srgb);
            }
        }
        return result;
    }

    private static double Linearize(int value)
    {
        double level = value / 255.0;
        return 255 * (level <= 0.04045 ? level / 12.92 : Math.Pow((level + 0.055) / 1.055, 2.4));
    }

    private static byte EncodeChannel(double value, bool srgb)
    {
        if (srgb)
        {
            double level = value / 255.0;
            value = 255 * (level <= 0.0031308 ? level * 12.92 : 1.055 * Math.Pow(level, 1 / 2.4) - 0.055);
        }
        return (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }
}
