using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ.Tests;

internal static class UnityFixture
{
    public const string EntryName = "CAB-synthetic";
    public const string ResourceName = "synthetic.resS";

    public static void WriteAssets(
        Stream stream,
        uint formatVersion,
        bool typeTree,
        bool bigEndian,
        Hash128? typeHash,
        params (long PathId, string Name, byte[] Payload)[] texts)
    {
        var file = CreateFile(formatVersion, typeTree, bigEndian, MakeTextAssetType(typeHash ?? Hash128.NewBlankHash()));

        foreach (var (pathId, name, payload) in texts)
        {
            using var data = new MemoryStream();
            using var writer = new AssetsFileWriter(data) { BigEndian = bigEndian };
            WriteAlignedBytes(writer, System.Text.Encoding.UTF8.GetBytes(name));
            WriteAlignedBytes(writer, payload);

            AddAsset(file, pathId, data.ToArray());
        }

        WriteFile(file, stream);
    }

    public static byte[] CreateMonoBehaviourAssets(bool bigEndian, params long[] pathIds) =>
        CreateMonoBehaviourAssets(bigEndian, true, Hash128.NewBlankHash(), Hash128.NewBlankHash(), "2022.3.0f1", pathIds);

    public static byte[] CreateMonoBehaviourAssets(
        bool bigEndian,
        bool hasTypeTree,
        Hash128 typeHash,
        Hash128 scriptHash,
        string unityVersion,
        params long[] pathIds)
    {
        var type = MakeMonoBehaviourType(typeHash);
        type.ScriptIdHash = scriptHash;
        var file = CreateFile(22, hasTypeTree, bigEndian, type, unityVersion);
        foreach (long pathId in pathIds)
        {
            using var data = new MemoryStream();
            using var writer = new AssetsFileWriter(data) { BigEndian = bigEndian };
            WriteAlignedBytes(writer, "behaviour"u8.ToArray());
            writer.Write(0);
            writer.Write(109L);
            writer.Write(2);
            for (int i = 0; i < 2; i++)
            {
                writer.Write(i + 1);
                WriteAlignedBytes(writer, "original"u8.ToArray());
                writer.Write(0f);
                writer.Write((byte)0);
                writer.Align();
            }

            AddAsset(file, pathId, data.ToArray());
        }

        using var stream = new MemoryStream();
        WriteFile(file, stream);
        return stream.ToArray();
    }

    public static byte[] CreateMonoBehaviourAliasAssets(long pathId)
    {
        var type = MakeType(AssetClassID.MonoBehaviour, Hash128.NewBlankHash(),
        [
            (0, "MonoBehaviour", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "ColorRGBA", "m_Color", 16, false, false),
            (2, "float", "r", 4, false, false),
            (2, "float", "g", 4, false, false),
            (2, "float", "b", 4, false, false),
            (2, "float", "a", 4, false, false),
            (1, "ColorRGBA", "m_PackedColor", 4, false, false),
            (2, "unsigned int", "rgba", 4, false, false),
            (1, "Vector4f", "m_Vector", 16, false, false),
            (2, "float", "x", 4, false, false),
            (2, "float", "y", 4, false, false),
            (2, "float", "z", 4, false, false),
            (2, "float", "w", 4, false, false),
            (1, "vector", "m_Empty", -1, false, false),
            (2, "Array", "Array", -1, true, false),
            (3, "int", "size", 4, false, false),
            (3, "int", "data", 4, false, false)
        ]);
        var file = CreateFile(22, true, false, type);
        using var data = new MemoryStream();
        using var writer = new AssetsFileWriter(data);
        WriteAlignedBytes(writer, "behaviour"u8.ToArray());
        writer.Write(new byte[40]);
        AddAsset(file, pathId, data.ToArray());

        using var stream = new MemoryStream();
        WriteFile(file, stream);
        return stream.ToArray();
    }

    public static byte[] CreateSdfFontAssets(long pathId, bool hasTypeTree = true, bool hasGlyphs = true)
    {
        var type = MakeType(AssetClassID.MonoBehaviour, Hash128.NewBlankHash(),
        [
            (0, "MonoBehaviour", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "Glyph", "m_GlyphTable", -1, false, false),
            (2, "Array", "Array", -1, true, false),
            (3, "int", "size", 4, false, false),
            (3, "Glyph", "data", 4, false, false),
            (4, "unsigned int", "m_Index", 4, false, false),
            (1, "TMP_Character", "m_CharacterTable", -1, false, false),
            (2, "Array", "Array", -1, true, false),
            (3, "int", "size", 4, false, false),
            (3, "TMP_Character", "data", 4, false, false),
            (4, "unsigned int", "m_Unicode", 4, false, false),
            (1, "vector", "m_AtlasTextures", -1, false, false),
            (2, "Array", "Array", -1, true, false),
            (3, "int", "size", 4, false, false),
            ..PointerNodes(3, "Texture2D", "data")
        ]);
        return CreateFontFixture(type, pathId, writer =>
        {
            writer.Write(hasGlyphs ? 1 : 0);
            if (hasGlyphs)
                writer.Write(23u);  // Glyph index.
            writer.Write(hasGlyphs ? 1 : 0);
            if (hasGlyphs)
                writer.Write(65u);  // Unicode scalar value.
            writer.Write(1);    // Atlas count.
            writer.Write(0);
            writer.Write(44L);
        }, hasTypeTree);
    }

    public static byte[] CreateFontAssets(long pathId)
    {
        var type = MakeType(AssetClassID.Font, Hash128.NewBlankHash(),
        [
            (0, "Font", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            ..PointerNodes(1, "Material", "m_DefaultMaterial"),
            (1, "vector", "m_FontData", -1, false, true),
            (2, "Array", "Array", -1, true, true),
            (3, "int", "size", 4, false, false),
            (3, "UInt8", "data", 1, false, false),
            (1, "vector", "m_FontNames", -1, false, false),
            (2, "Array", "Array", -1, true, true),
            (3, "int", "size", 4, false, false),
            ..StringNodes(3, "data"),
            (1, "vector", "m_FallbackFonts", -1, false, true),
            (2, "Array", "Array", -1, true, true),
            (3, "int", "size", 4, false, false),
            ..PointerNodes(3, "Font", "data")
        ]);
        return CreateFontFixture(type, pathId, writer =>
        {
            writer.Write(0);
            writer.Write(23L); // Material PathID.
            WriteAlignedBytes(writer, [1, 2, 3, 4]);
            writer.Write(0);   // Font name count.
            writer.Write(1);   // Fallback font count.
            writer.Write(0);
            writer.Write(44L);
        });
    }

    public static byte[] CreateLegacyTextMeshProAssets(long pathId) =>
        CreateStructuredAsset(pathId, AssetClassID.MonoBehaviour,
        [
            (0, "MonoBehaviour", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "FaceInfo", "m_fontInfo", 4, false, false),
            (2, "float", "AtlasWidth", 4, false, false),
            ..PointerNodes(1, "$Texture2D", "atlas"),
            (1, "TMP_Glyph", "m_glyphInfoList", -1, false, false),
            (2, "Array", "Array", -1, true, false),
            (3, "int", "size", 4, false, false),
            (3, "TMP_Glyph", "data", 4, false, false),
            (4, "int", "id", 4, false, false),
            (1, "KerningTable", "m_kerningInfo", -1, false, false),
            (2, "KerningPair", "kerningPairs", -1, false, false),
            (3, "Array", "Array", -1, true, false),
            (4, "int", "size", 4, false, false),
            (4, "KerningPair", "data", 4, false, false),
            (5, "float", "xOffset", 4, false, false)
        ],
        writer =>
        {
            WriteAlignedBytes(writer, "test font"u8.ToArray());
            writer.Write(256f);
            writer.Write(0);
            writer.Write(44L);
            writer.Write(1);
            writer.Write(65);
            writer.Write(1);
            writer.Write(2f);
        });

    private static byte[] CreateFontFixture(TypeTreeType type, long pathId, Action<AssetsFileWriter> writeFont,
        bool hasTypeTree = true)
    {
        var file = CreateFile(22, hasTypeTree, false, type);
        using var data = new MemoryStream();
        using var writer = new AssetsFileWriter(data);
        WriteAlignedBytes(writer, "test font"u8.ToArray());
        writeFont(writer);
        AddAsset(file, pathId, data.ToArray());

        using var stream = new MemoryStream();
        WriteFile(file, stream);
        return stream.ToArray();
    }

    private static AssetsFile CreateFile(
        uint formatVersion, bool typeTree, bool bigEndian, TypeTreeType type, string unityVersion = "2022.3.0f1") =>
        new AssetsFile
        {
            Header = new AssetsFileHeader { Version = formatVersion, Endianness = bigEndian },
            Metadata = new AssetsFileMetadata
            {
                UnityVersion = unityVersion,
                TargetPlatform = 19,
                TypeTreeEnabled = typeTree,
                TypeTreeTypes = [type],
                AssetInfos = [],
                ScriptTypes = [],
                Externals = [],
                RefTypes = [],
                UserInformation = ""
            }
        };

    private static void AddAsset(AssetsFile file, long pathId, byte[] data, int typeIndex = 0)
    {
        var asset = new AssetFileInfo { PathId = pathId, TypeIdOrIndex = typeIndex };
        asset.SetNewData(data);
        file.Metadata.AddAssetInfo(asset);
    }

    private static void WriteFile(AssetsFile file, Stream stream)
    {
        var output = new AssetsFileWriter(stream);
        file.Write(output);
        output.Flush();
    }

    public static byte[] CreateAssets(params (long PathId, string Name, byte[] Payload)[] texts)
    {
        using var stream = new MemoryStream();
        WriteAssets(stream, 22, true, false, null, texts);
        return stream.ToArray();
    }

    public static byte[] CreateTextureAssets(
        int width,
        int height,
        int imageSize,
        string streamPath,
        ulong streamOffset,
        long pathId,
        int format = 12,
        byte[]? inlineData = null,
        int mipCount = 1)
    {
        var file = CreateFile(22, true, false, MakeTextureType());
        using (var data = new MemoryStream())
        {
            using var writer = new AssetsFileWriter(data);
            WriteAlignedBytes(writer, "test texture"u8.ToArray());
            writer.Write(width);
            writer.Write(height);
            writer.Write(imageSize);
            writer.Write(0);  // stripped mip levels
            writer.Write(format);
            writer.Write(mipCount);
            writer.Write(1);  // image count
            writer.Write(2);  // 2D
            WriteAlignedBytes(writer, inlineData ?? []);
            writer.Write(streamOffset);
            writer.Write(streamPath.Length == 0 ? 0u : (uint)imageSize);
            WriteAlignedBytes(writer, System.Text.Encoding.UTF8.GetBytes(streamPath));
            AddAsset(file, pathId, data.ToArray());
        }

        using var stream = new MemoryStream();
        WriteFile(file, stream);
        return stream.ToArray();
    }

    public static byte[] CreateSpriteAssets(long pathId) =>
        CreateStructuredAsset(pathId, AssetClassID.Sprite,
        [
            (0, "Sprite", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "vector", "m_Indices", -1, false, false),
            (2, "Array", "Array", -1, true, true),
            (3, "int", "size", 4, false, false),
            (3, "UInt8", "data", 1, false, false),
            (1, "vector", "m_Records", -1, false, false),
            (2, "Array", "Array", -1, true, true),
            (3, "int", "size", 4, false, false),
            (3, "Record", "data", -1, false, false),
            (4, "int", "id", 4, false, false)
        ],
        writer =>
        {
            WriteAlignedBytes(writer, "logo"u8.ToArray());
            WriteAlignedBytes(writer, [1]);
            writer.Write(1);
            writer.Write(99);
        });

    public static byte[] CreateRectTransformAssets(long pathId) =>
        CreateStructuredAsset(pathId, AssetClassID.RectTransform,
        [
            (0, "RectTransform", "Base", -1, false, false),
            (1, "PPtr<Transform>", "m_Father", 12, false, false),
            (2, "int", "m_FileID", 4, false, false),
            (2, "SInt64", "m_PathID", 8, false, false),
            (1, "Vector2f", "m_AnchoredPosition", 8, false, false),
            (2, "float", "x", 4, false, false),
            (2, "float", "y", 4, false, false)
        ],
        writer =>
        {
            writer.Write(0);
            writer.Write(40L);
            writer.Write(1f);
            writer.Write(2f);
        });

    public static byte[] CreateGameObjectAssets(long pathId) =>
        CreateStructuredAsset(pathId, AssetClassID.GameObject,
        [
            (0, "GameObject", "Base", -1, false, false),
            (1, "vector", "m_Component", -1, false, false),
            (2, "Array", "Array", -1, true, true),
            (3, "int", "size", 4, false, false),
            (3, "ComponentPair", "data", 12, false, false),
            ..PointerNodes(4, "Component", "component"),
            ..StringNodes(1, "m_Name"),
            (1, "bool", "m_IsActive", 1, false, false)
        ],
        writer =>
        {
            writer.Write(1);
            writer.Write(0);
            writer.Write(40L);
            WriteAlignedBytes(writer, "button"u8.ToArray());
            writer.Write(true);
        });

    public static byte[] CreateMaterialAssets(long pathId) =>
        CreateStructuredAsset(pathId, AssetClassID.Material,
        [
            (0, "Material", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            ..PointerNodes(1, "Shader", "m_Shader"),
            (1, "float", "m_Amount", 4, false, false)
        ],
        writer =>
        {
            WriteAlignedBytes(writer, "test material"u8.ToArray());
            writer.Write(1);
            writer.Write(42L);
            writer.Write(1f);
        });

    public static byte[] CreateStrippedMaterialAssets(long pathId, string packagePath, bool matching)
    {
        const int classId = (int)AssetClassID.Material;
        const string unityVersion = "2022.3.0f1";
        var version = new UnityVersion(unityVersion);
        var package = new ClassPackageFile
        {
            Header = new ClassPackageHeader { Magic = "TPK*", FileVersion = 2 },
            TpkTypeTree = new ClassPackageTypeTree
            {
                Versions = [version],
                CommonString = new ClassPackageCommonString { VersionInformation = [] },
                StringTable = new ClassDatabaseStringTable { Strings = ["Material", "Base", "float", "m_Amount"] },
                ClassInformation =
                [
                    new ClassPackageClassInfo
                    {
                        ClassId = classId,
                        Classes = [new(version, new ClassPackageType
                        {
                            ClassId = classId, Name = 0, BaseName = 1,
                            ReleaseRootNode = 0, Flags = ClassFileTypeFlags.HasReleaseRootNode
                        })]
                    }
                ],
                Nodes =
                [
                    new ClassPackageTypeNode { TypeName = 0, FieldName = 1, Version = 1, ByteSize = -1, SubNodes = [1] },
                    new ClassPackageTypeNode { TypeName = 2, FieldName = 3, Version = 1, ByteSize = 4, SubNodes = [] }
                ]
            }
        };
        package.Write(packagePath, ClassFileCompressionType.Lz4);

        // MD4 of the fixture's type and field names, sizes, flags, versions and alignment.
        var hash = new Hash128(Convert.FromHexString(matching
            ? "be5df3b67fee4a4bced102042899769a"
            : "be5df3b67fee4a4bced102042899769b"));
        var type = MakeType(AssetClassID.Material, hash, []);
        var file = CreateFile(22, false, false, type, unityVersion);
        AddAsset(file, pathId, [0, 0, 128, 63]); // 1.0f, little endian.
        using var stream = new MemoryStream();
        WriteFile(file, stream);
        return stream.ToArray();
    }

    public static byte[] CreateVideoClipAssets(long pathId, string resourcePath, long offset, int size) =>
        CreateStructuredAsset(pathId, AssetClassID.VideoClip,
        [
            (0, "VideoClip", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "unsigned int", "Width", 4, false, false),
            (1, "unsigned int", "Height", 4, false, false),
            (1, "StreamedResource", "m_ExternalResources", -1, false, false),
            ..StringNodes(2, "m_Source"),
            (2, "UInt64", "m_Offset", 8, false, false),
            (2, "UInt64", "m_Size", 8, false, false),
            (1, "bool", "m_HasSplitAlpha", 1, false, false)
        ],
        writer =>
        {
            WriteAlignedBytes(writer, "test clip"u8.ToArray());
            writer.Write(1920u);
            writer.Write(1080u);
            WriteAlignedBytes(writer, System.Text.Encoding.UTF8.GetBytes(resourcePath));
            writer.Write((ulong)offset);
            writer.Write((ulong)size);
            writer.Write(false);
        });

    public static byte[] WithAtlasMaterials(byte[] assets, long atlasId, long localId, long externalId)
    {
        using var reader = new AssetsFileReader(new MemoryStream(assets));
        var file = new AssetsFile();
        file.Read(reader);
        var materialType = MakeType(AssetClassID.Material, Hash128.NewBlankHash(),
        [
            (0, "Material", "Base", -1, false, false),
            (1, "UnityPropertySheet", "m_SavedProperties", -1, false, false),
            (2, "map", "m_TexEnvs", -1, false, false),
            (3, "Array", "Array", -1, true, false),
            (4, "int", "size", 4, false, false),
            (4, "pair", "data", -1, false, false),
            ..StringNodes(5, "first"),
            (5, "UnityTexEnv", "second", -1, false, false),
            ..PointerNodes(6, "Texture", "m_Texture"),
            (2, "map", "m_Floats", -1, false, false),
            (3, "Array", "Array", -1, true, false),
            (4, "int", "size", 4, false, false),
            (4, "pair", "data", -1, false, false),
            ..StringNodes(5, "first"),
            (5, "float", "second", 4, false, false)
        ]);
        int typeIndex = file.Metadata.TypeTreeTypes.Count;
        file.Metadata.TypeTreeTypes.Add(materialType);
        WriteMaterial(localId, fileId: 0);
        WriteMaterial(externalId, fileId: 1);

        using var stream = new MemoryStream();
        file.Write(new AssetsFileWriter(stream));
        return stream.ToArray();

        void WriteMaterial(long pathId, int fileId)
        {
            using var data = new MemoryStream();
            using var writer = new AssetsFileWriter(data);
            writer.Write(1);
            WriteAlignedBytes(writer, "_MainTex"u8.ToArray());
            writer.Write(fileId);
            writer.Write(atlasId);
            writer.Write(2);
            WriteAlignedBytes(writer, "_TextureWidth"u8.ToArray());
            writer.Write(1f);
            WriteAlignedBytes(writer, "_TextureHeight"u8.ToArray());
            writer.Write(1f);
            AddAsset(file, pathId, data.ToArray(), typeIndex);
        }
    }

    private static byte[] CreateStructuredAsset(
        long pathId, AssetClassID classId,
        (byte Level, string Type, string Name, int Size, bool IsArray, bool IsAligned)[] layout,
        Action<AssetsFileWriter> writeAsset)
    {
        var file = CreateFile(22, true, false, MakeType(classId, Hash128.NewBlankHash(), layout));
        using var data = new MemoryStream();
        using var writer = new AssetsFileWriter(data);
        writeAsset(writer);
        AddAsset(file, pathId, data.ToArray());
        using var stream = new MemoryStream();
        WriteFile(file, stream);
        return stream.ToArray();
    }

    public static void WriteBundle(string path, AssetBundleCompressionType compression, params (string Name, byte[] Data, bool IsSerialized)[] files)
    {
        var bundle = new AssetBundleFile
        {
            Header = new AssetBundleHeader
            {
                Signature = "UnityFS",
                Version = 7,
                EngineVersion = "2022.3.0f1",
                GenerationVersion = "5.x.x",
                FileStreamHeader = new AssetBundleFSHeader { Flags = AssetBundleFSHeaderFlags.HasDirectoryInfo }
            },
            BlockAndDirInfo = new AssetBundleBlockAndDirInfo { DirectoryInfos = [] },
            DataReader = new AssetsFileReader(new MemoryStream())
        };
        using var emptyReader = bundle.DataReader;

        foreach (var (name, data, isSerialized) in files)
        {
            var entry = AssetBundleDirectoryInfo.Create(name, isSerialized);
            entry.SetNewData(data);
            bundle.BlockAndDirInfo.DirectoryInfos.Add(entry);
        }

        using var destination = new AssetsFileWriter(path);
        if (compression == AssetBundleCompressionType.None)
        {
            bundle.Write(destination);
            return;
        }

        using var uncompressed = new MemoryStream();
        bundle.Write(new AssetsFileWriter(uncompressed));

        var toPack = new AssetBundleFile();
        toPack.Read(new AssetsFileReader(uncompressed));
        toPack.Pack(destination, compression);
        toPack.Close();
    }

    public static (string Name, byte[] Payload) ReadText(AssetsFile file, long pathId)
    {
        AssetFileInfo info = file.GetAssetInfo(pathId);
        var reader = file.Reader;
        reader.Position = info.GetAbsoluteByteOffset(file);
        string name = System.Text.Encoding.UTF8.GetString(ReadAlignedBytes(reader));
        byte[] payload = ReadAlignedBytes(reader);
        return (name, payload);
    }

    private static byte[] ReadAlignedBytes(AssetsFileReader reader)
    {
        byte[] bytes = reader.ReadBytes(reader.ReadInt32());
        reader.Align();
        return bytes;
    }

    private static void WriteAlignedBytes(AssetsFileWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Align();
    }

    private static TypeTreeType MakeTextAssetType(Hash128 typeHash) =>
        MakeType(AssetClassID.TextAsset, typeHash,
        [
            (0, "TextAsset", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            ..StringNodes(1, "m_Script")
        ]);

    private static TypeTreeType MakeTextureType() =>
        MakeType(AssetClassID.Texture2D, Hash128.NewBlankHash(),
        [
            (0, "Texture2D", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "int", "m_Width", 4, false, false),
            (1, "int", "m_Height", 4, false, false),
            (1, "unsigned int", "m_CompleteImageSize", 4, false, false),
            (1, "int", "m_MipsStripped", 4, false, false),
            (1, "int", "m_TextureFormat", 4, false, false),
            (1, "int", "m_MipCount", 4, false, false),
            (1, "int", "m_ImageCount", 4, false, false),
            (1, "int", "m_TextureDimension", 4, false, false),
            (1, "TypelessData", "image data", -1, true, true),
            (2, "int", "size", 4, false, false),
            (2, "UInt8", "data", 1, false, false),
            (1, "StreamingInfo", "m_StreamData", -1, false, false),
            (2, "UInt64", "offset", 8, false, false),
            (2, "unsigned int", "size", 4, false, false),
            ..StringNodes(2, "path")
        ]);

    private static TypeTreeType MakeMonoBehaviourType(Hash128 typeHash) =>
        MakeType(AssetClassID.MonoBehaviour, typeHash,
        [
            (0, "MonoBehaviour", "Base", -1, false, false),
            ..StringNodes(1, "m_Name"),
            (1, "PPtr<MonoScript>", "m_Script", 12, false, false),
            (2, "int", "m_FileID", 4, false, false),
            (2, "SInt64", "m_PathID", 8, false, false),
            (1, "ExampleRecord", "records", -1, false, false),
            (2, "Array", "Array", -1, true, false),
            (3, "int", "size", 4, false, false),
            (3, "ExampleRecord", "data", -1, false, false),
            (4, "int", "id", 4, false, false),
            ..StringNodes(4, "text"),
            (4, "float", "wait", 4, false, false),
            (4, "UInt8", "enabled", 1, false, true)
        ]);

    private static (byte Level, string Type, string Name, int Size, bool IsArray, bool IsAligned)[] StringNodes(byte level, string name) =>
    [
        (level, "string", name, -1, false, false),
        ((byte)(level + 1), "Array", "Array", -1, true, true),
        ((byte)(level + 2), "int", "size", 4, false, false),
        ((byte)(level + 2), "char", "data", 1, false, false)
    ];

    private static (byte Level, string Type, string Name, int Size, bool IsArray, bool IsAligned)[] PointerNodes(
        byte level, string type, string name) =>
    [
        (level, $"PPtr<{type}>", name, 12, false, false),
        ((byte)(level + 1), "int", "m_FileID", 4, false, false),
        ((byte)(level + 1), "SInt64", "m_PathID", 8, false, false)
    ];

    private static TypeTreeType MakeType(
        AssetClassID classId,
        Hash128 typeHash,
        (byte Level, string Type, string Name, int Size, bool IsArray, bool IsAligned)[] layout)
    {
        var strings = new System.Text.StringBuilder();
        var nodes = new List<TypeTreeNode>();
        foreach (var (level, type, name, size, isArray, isAligned) in layout)
        {
            uint typeOffset = (uint)strings.Length;
            strings.Append(type).Append('\0');
            uint nameOffset = (uint)strings.Length;
            strings.Append(name).Append('\0');
            nodes.Add(new TypeTreeNode
            {
                Level = level,
                Version = 1,
                TypeStrOffset = typeOffset,
                NameStrOffset = nameOffset,
                ByteSize = size,
                Index = (uint)nodes.Count,
                TypeFlags = isArray ? TypeTreeNodeFlags.Array : TypeTreeNodeFlags.None,
                MetaFlags = isAligned ? 0x4000u : 0
            });
        }

        return new TypeTreeType
        {
            TypeId = (int)classId,
            ScriptTypeIndex = ushort.MaxValue,
            ScriptIdHash = Hash128.NewBlankHash(),
            TypeHash = typeHash,
            TypeDependencies = [],
            TypeBlob = new TypeTreeBlob(),
            Nodes = nodes,
            StringBuffer = strings.ToString()
        };
    }
}
