using System.Buffers.Binary;

namespace NewUAFGJ;

internal static class Mp4Container
{
    public static bool HasSignature(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 8 && prefix[4..8].SequenceEqual("ftyp"u8);

    public static void Validate(Stream source)
    {
        source.Position = 0;
        long length = source.Length;
        Span<byte> header = stackalloc byte[8];
        Span<byte> extendedSize = stackalloc byte[8];
        bool hasMovie = false;
        bool hasMedia = false;
        while (source.Position < length)
        {
            long start = source.Position;
            long remaining = length - start;
            if (remaining < header.Length)
                throw new InvalidDataException("The MP4 has an incomplete box header.");

            source.ReadExactly(header);
            ReadOnlySpan<byte> type = header[4..];
            int headerSize = header.Length;
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (size == 1)
            {
                headerSize += 8;
                if (remaining < headerSize)
                    throw new InvalidDataException("The MP4 has an incomplete extended box header.");

                source.ReadExactly(extendedSize);
                size = BinaryPrimitives.ReadUInt64BigEndian(extendedSize);
            }
            else if (size == 0)
            {
                size = (ulong)remaining;
            }

            if (size < (ulong)headerSize || size > (ulong)remaining)
                throw new InvalidDataException("An MP4 box is outside the file bounds.");
            if (start == 0 && (!type.SequenceEqual("ftyp"u8) || size < (ulong)(headerSize + 8)))
                throw new InvalidDataException("Expected an MP4 file beginning with a complete ftyp box.");

            hasMovie |= type.SequenceEqual("moov"u8) && size > (ulong)headerSize;
            hasMedia |= type.SequenceEqual("mdat"u8) && size > (ulong)headerSize;
            source.Position = start + (long)size;
        }

        if (!hasMovie || !hasMedia)
            throw new InvalidDataException("Expected an MP4 file containing movie metadata and media data.");

        source.Position = 0;
    }
}
