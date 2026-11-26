using AssetsTools.NET;

namespace NewUAFGJ;

internal sealed class VideoClipReplacement
{
    private readonly AssetTypeValueField clip;
    private readonly AssetTypeValueField size;

    public VideoClipReplacement(AssetsFile file, AssetFileInfo asset, ReplacementTypeTree resolver, string? typeTreeSource)
    {
        var template = new AssetTypeTemplateField();
        template.FromTypeTree(resolver.Resolve(file, asset, typeTreeSource));
        clip = SerializedObject.ReadFields(file, asset, template);
        var source = clip["m_ExternalResources.m_Source"];
        var offset = clip["m_ExternalResources.m_Offset"];
        size = clip["m_ExternalResources.m_Size"];
        if (source.IsDummy || offset.IsDummy || size.IsDummy)
            throw new InvalidDataException("The VideoClip does not have a supported external resource reference.");

        ResourcePath = source.AsString;
        Offset = checked((long)offset.AsULong);
        Size = checked((long)size.AsULong);
    }

    public string ResourcePath { get; }
    public long Offset { get; }
    public long Size { get; }

    public byte[] ResizeResource(long length, bool bigEndian)
    {
        size.AsULong = checked((ulong)length);
        return clip.WriteToByteArray(bigEndian);
    }
}
