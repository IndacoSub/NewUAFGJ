using AssetsTools.NET;

namespace NewUAFGJ;

internal static class TextAssetReplacement
{
    private static readonly byte[] NameAndScriptTypeHash = Convert.FromHexString("486ba4e15dbd6aea8ac1a064305889c8");
    private static readonly AssetTypeTemplateField NameAndScriptTemplate = new()
    {
        Name = "Base",
        Type = "TextAsset",
        Children = [AlignedString("m_Name"), AlignedString("m_Script")]
    };

    public static byte[] Build(AssetsFile file, AssetFileInfo asset, byte[] payload)
    {
        AssetTypeValueField field = SerializedObject.ReadFields(file, asset, GetTextAssetTemplate(file, asset));
        if (field["m_Name"].Value?.ValueType != AssetValueType.String ||
            field["m_Script"].Value?.ValueType != AssetValueType.String)
        {
            throw new InvalidDataException("The TextAsset does not have a supported serialized layout.");
        }

        field["m_Script"].AsByteArray = payload;
        return field.WriteToByteArray(file.Header.Endianness);
    }

    private static AssetTypeTemplateField GetTextAssetTemplate(AssetsFile file, AssetFileInfo asset)
    {
        TypeTreeType type = file.Metadata.TypeTreeTypes[asset.TypeIdOrIndex];
        if (file.Metadata.TypeTreeEnabled && type.TypeBlob?.Nodes is { Count: > 0 })
        {
            var template = new AssetTypeTemplateField();
            template.FromTypeTree(type);
            return template;
        }

        if (type.TypeHash.data.AsSpan().SequenceEqual(NameAndScriptTypeHash))
            return NameAndScriptTemplate;

        throw new InvalidDataException("TextAsset replacement requires an embedded TypeTree or a recognized TextAsset type hash.");
    }

    private static AssetTypeTemplateField AlignedString(string name) => new()
    {
        Name = name,
        Type = "string",
        ValueType = AssetValueType.String,
        HasValue = true,
        // AssetsTools.NET aligns string values to four bytes when no array metadata is supplied.
        Children = []
    };
}
