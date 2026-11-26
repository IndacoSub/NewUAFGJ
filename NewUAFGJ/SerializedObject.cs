using AssetsTools.NET;

namespace NewUAFGJ;

internal static class SerializedObject
{
    public static byte[] ReadBytes(AssetsFile file, AssetFileInfo asset)
    {
        file.Reader.Position = asset.GetAbsoluteByteOffset(file);
        int size = checked((int)asset.ByteSize);
        byte[] serialized = file.Reader.ReadBytes(size);
        if (serialized.Length != size)
            throw new InvalidDataException("The serialized object is truncated.");

        return serialized;
    }

    public static AssetTypeValueField ReadFields(AssetsFile file, AssetFileInfo asset, AssetTypeTemplateField template)
    {
        byte[] serialized = ReadBytes(file, asset);
        using var reader = new ObjectReader(serialized) { BigEndian = file.Header.Endianness };
        AssetTypeValueField field = template.MakeValue(reader);
        if (reader.Position != serialized.Length)
            throw new InvalidDataException("The TypeTree does not describe the entire serialized object.");

        return field;
    }

    private sealed class ObjectReader(byte[] data) : AssetsFileReader(new MemoryStream(data))
    {
        public override byte[] ReadBytes(int count)
        {
            if (count < 0 || count > BaseStream.Length - Position)
                throw new InvalidDataException("A field extends beyond the bounds of the serialized object.");

            return base.ReadBytes(count);
        }
    }
}
