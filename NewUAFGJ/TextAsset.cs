using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;

namespace UAFGJ;

partial class Program
{
    private static bool ImportTextAssetCustom(
        string inputFile,
        AssetsManager am,
        AssetFileInfo afie,
        AssetsFileInstance assetInst,
        string assetName)
    {
        if (!File.Exists(inputFile))
            throw new FileNotFoundException("Text input file not found.", inputFile);

        AssetTypeValueField baseField = am.GetBaseField(assetInst, afie);
        if (baseField == null || baseField.IsDummy)
            throw new InvalidDataException("TextAsset BaseField is unavailable.");

        byte[] data = File.ReadAllBytes(inputFile);
        baseField["m_Name"].AsString = Path.GetFileNameWithoutExtension(inputFile);
        baseField["m_Script"].AsByteArray = data;

        byte[] savedAsset = baseField.WriteToByteArray();
        afie.SetNewData(savedAsset);

        using MemoryStream ms = new MemoryStream();
        using (AssetsFileWriter writer = new AssetsFileWriter(ms))
        {
            assetInst.file.Write(writer);
        }

        string temp = assetName + ".uafgj_textasset_" + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, ms.ToArray());
        am.UnloadAllAssetsFiles(true);
        ReplaceFileWithRetry(temp, assetName);
        return true;
    }
}
