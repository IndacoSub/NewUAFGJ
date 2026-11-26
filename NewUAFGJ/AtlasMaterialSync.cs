using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal static class AtlasMaterialSync
{
    public static Dictionary<long, byte[]> Apply(AssetsFile file, long atlasId, (int Width, int Height) size,
        ReplacementTypeTree resolver, string? typeTreeSource)
    {
        var templates = new Dictionary<int, AssetTypeTemplateField>();
        var updated = new Dictionary<long, byte[]>();
        foreach (var asset in file.GetAssetsOfType(AssetClassID.Material))
        {
            if (!templates.TryGetValue(asset.TypeIdOrIndex, out var template))
            {
                template = new AssetTypeTemplateField();
                template.FromTypeTree(resolver.Resolve(file, asset, typeTreeSource));
                templates.Add(asset.TypeIdOrIndex, template);
            }

            var material = SerializedObject.ReadFields(file, asset, template);
            var textures = material["m_SavedProperties.m_TexEnvs.Array"];
            if (textures.IsDummy)
                continue;

            var texture = FindProperty(textures, "_MainTex")?["m_Texture"];
            if (texture is null || texture.IsDummy ||
                texture["m_FileID"].AsInt != 0 || texture["m_PathID"].AsLong != atlasId)
            {
                continue;
            }

            var floats = material["m_SavedProperties.m_Floats.Array"];
            if (floats.IsDummy)
                continue;

            var width = FindProperty(floats, "_TextureWidth");
            var height = FindProperty(floats, "_TextureHeight");
            if (width?.Value?.ValueType != AssetValueType.Float || height?.Value?.ValueType != AssetValueType.Float)
                continue;
            if (width.AsFloat == size.Width && height.AsFloat == size.Height)
                continue;

            width.AsFloat = size.Width;
            height.AsFloat = size.Height;
            byte[] data = material.WriteToByteArray(file.Header.Endianness);
            asset.SetNewData(data);
            updated.Add(asset.PathId, data);
        }
        return updated;
    }

    private static AssetTypeValueField? FindProperty(AssetTypeValueField properties, string name) =>
        properties.Children.SingleOrDefault(property => property["first"].AsString == name)?["second"];
}
