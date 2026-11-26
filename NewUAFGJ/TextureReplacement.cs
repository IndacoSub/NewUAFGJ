using AssetsTools.NET;

namespace NewUAFGJ;

internal sealed class TextureReplacement
{
    internal sealed record StreamLocation(string Path, long Offset, int Size);

    private readonly AssetTypeValueField field;
    private readonly AssetTypeValueField imageData;
    private readonly int width;
    private readonly int height;
    private readonly int format;
    private readonly int mipCount;
    private readonly (int Width, int Height) replacementSize;

    public TextureReplacement(AssetsFile file, AssetFileInfo asset, string? typeTreeSource, (int Width, int Height)? atlasSize)
    {
        var template = new AssetTypeTemplateField();
        template.FromTypeTree(ReplacementTypeTree.Resolve(file, asset, typeTreeSource));
        field = SerializedObject.ReadFields(file, asset, template);
        width = field["m_Width"].AsInt;
        height = field["m_Height"].AsInt;
        format = field["m_TextureFormat"].AsInt;
        mipCount = field["m_MipCount"].AsInt;
        replacementSize = atlasSize ?? (width, height);

        if (atlasSize is not null && format != TextureImage.Alpha8)
            throw new InvalidDataException("--resize-atlas requires an Alpha8 texture.");

        if (width <= 0 || height <= 0 || field["m_TextureDimension"].AsInt != 2 || field["m_ImageCount"].AsInt != 1)
            throw new InvalidDataException("Expected a single 2D image.");

        if (mipCount < 1 || (!field["m_MipsStripped"].IsDummy && field["m_MipsStripped"].AsInt != 0))
        {
            throw new InvalidDataException("Texture2D replacement requires a complete mip chain.");
        }

        var platformData = field["m_PlatformBlob.Array"];
        if (!platformData.IsDummy &&
            (platformData.Children.Count != 0 ||
                (platformData.Value?.ValueType == AssetValueType.ByteArray && platformData.AsByteArray.Length != 0)))
        {
            throw new InvalidDataException("The texture uses a platform-specific pixel layout.");
        }

        imageData = field["image data"];
        if (imageData.Value?.ValueType != AssetValueType.ByteArray)
            throw new InvalidDataException("Texture2D does not contain a supported image data field.");

        var stream = field["m_StreamData"];
        if (!stream.IsDummy && stream["size"].AsUInt > 0)
        {
            if (imageData.AsByteArray.Length != 0)
                throw new InvalidDataException("Texture2D contains both inline and streamed pixel data.");

            Stream = new StreamLocation(stream["path"].AsString, checked((long)stream["offset"].AsULong),
                checked((int)stream["size"].AsUInt));
        }
    }

    public StreamLocation? Stream { get; }
    public bool DimensionsChanged => replacementSize != (width, height);

    public byte[] Encode(byte[] png)
    {
        int originalSize = Stream?.Size ?? imageData.AsByteArray.Length;
        if (TextureImage.GetEncodedSize(width, height, format, mipCount) != originalSize)
            throw new InvalidDataException("Encoded image size differs from the original texture data.");

        return TextureImage.Encode(png, replacementSize.Width, replacementSize.Height, format, mipCount);
    }

    public byte[] ReplaceInline(byte[] png, bool bigEndian)
    {
        var streamingMipmaps = field["m_StreamingMipmaps"];
        if (Stream is not null && !streamingMipmaps.IsDummy && streamingMipmaps.AsBool)
            throw new InvalidDataException("This texture uses mipmap streaming and cannot be stored inline.");

        byte[] encoded = Encode(png);
        imageData.AsByteArray = encoded;
        if (DimensionsChanged)
        {
            field["m_Width"].AsInt = replacementSize.Width;
            field["m_Height"].AsInt = replacementSize.Height;
            field["m_CompleteImageSize"].AsInt = encoded.Length;
        }

        if (Stream is not null)
        {
            // A resized atlas or a standalone texture can store the replacement without changing the resource file.
            var stream = field["m_StreamData"];
            stream["path"].AsString = string.Empty;
            stream["offset"].AsULong = 0;
            stream["size"].AsUInt = 0;
        }

        return field.WriteToByteArray(bigEndian);
    }
}
