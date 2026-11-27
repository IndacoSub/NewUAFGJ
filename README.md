# NewerUAFGJ

NewerUAFGJ is a personal-use, and unofficial, .NET 10 command-line tool for replacing assets in Unity games. 

It is a rewrite of NewUAFGJ (found in the 'main' branch), itself a rewrite of UAFGJ. 

It works with Unity serialized files (formats 17–22) and UnityFS bundles with uncompressed, LZ4 or LZMA data.

> **AI USAGE DISCLAIMER**
>
> This project has been developed with the assistance of Artificial Intelligence tools.
> AI was used during development for code generation, refactoring, debugging, API migration, documentation, and troubleshooting.
>
> The generated code has been reviewed, adapted, and integrated by **Team DAIX**. AI assistance does not imply that every generated component is error-free or guaranteed to behave correctly in every environment.
>
> This disclaimer also applies to this README and other documentation that may have been generated or assisted by AI.

## Build

Install the .NET SDK specified in [global.json](global.json).

NewerUAFGJ uses AssetsTools.NET by nesrak1 to read and write Unity assets.

The build uses the revision pinned by the Git submodule. [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET)

```sh
git submodule update --init --recursive
dotnet build NewUAFGJ/NewUAFGJ.csproj -c Release
dotnet test --solution NewUAFGJ.slnx -c Release
```

On Windows, the Release executable is at `NewUAFGJ/bin/Release/net10.0/NewUAFGJ.exe`.

## Replace an asset

```sh
dotnet run --project NewUAFGJ -c Release -- game.bundle translation.txt -900000000000000001
dotnet run --project NewUAFGJ -c Release -- game.bundle logo.png 127
```

Use a working copy for single replacements. The tool validates the staged file before replacing the source and preserves bundle compression. A PathID is a signed 64-bit integer. For textures, text payloads and video clips, omit it or pass `-` when there is a single matching asset. Structured dumps require an explicit PathID. FileID `0` selects a local object and `-` leaves FileID unspecified.

| Replacement | Input |
| --- | --- |
| TextAsset | Payload bytes or a UTF-8 UABEA TextAsset dump. Use `--raw` to import a file verbatim. |
| Structured asset | UABEA dump of a MonoBehaviour, Sprite, RectTransform, GameObject, Font or Material. This includes supported TextMeshPro glyph tables. |
| Texture2D | PNG. Supports RGB24, RGBA32, BC1, BC3, BC7 and single-mip Alpha8. The importer preserves the texture format and mip count. |
| VideoClip | MP4. A bundled clip must reference a complete MP4 resource from offset zero. Standalone MP4 resources are also supported. |

Use `--downscale` to fit a larger color PNG with compatible proportions to the target texture. Use `--resize-atlas` to take an Alpha8 atlas's dimensions from the PNG. Add `--sync-atlas-materials` to update `_TextureWidth` and `_TextureHeight` on materials in the same serialized entry whose local `_MainTex` references that atlas. Import the corresponding TextMeshPro dump alongside its atlas.

### Text and structured dumps

An ordinary TextAsset replacement supplies the bytes for `m_Script`. For UABEA TextAsset dumps, the importer reads the quoted `m_Script` field and decodes its escapes. Use `--raw` if the file itself is the payload, even when its contents resemble a dump. An empty payload clears the TextAsset.

Keep the field structure of a structured dump and preserve its object references. Modern TextMeshPro dumps support changing the glyph and character tables and the used and free glyph rectangles. Legacy TextMeshPro dumps support changing glyph and kerning tables.

### Textures and video

PNG replacements use the target texture dimensions by default. RGB24 requires opaque pixels. Alpha8 uses the PNG alpha channel. Use the atlas options above when changing dimensions, and make sure the font dump describes the new atlas layout.

For same-size streamed textures in UnityFS, the tool replaces the texture's byte range in the bundle resource. A resized atlas is stored inline. In standalone serialized files, replacing a sidecar-backed texture stores the replacement inline when mipmap streaming is disabled.

For video, use an MP4 with media properties compatible with the original. The importer updates the referenced resource size and preserves the other clip metadata.

### Planned support

- TGA (`.tga`) as an input format for Texture2D replacements.

## TypeTrees and classdata.tpk

An embedded TypeTree supplies the serialized layout. For supported native classes without an embedded tree, pass a compatible serialized file, UnityFS bundle or TPK class package with `--typetree-source`. A TPK entry must match the target's Unity version and type hash.

`classdata.tpk` is optional. The CLI reads a TPK when explicitly supplied as the TypeTree source. Its filename is arbitrary. Relative paths resolve from the current working directory in a single command and from the plan directory in a batch.

```sh
dotnet run --project NewUAFGJ -c Release -- --typetree-source schemas/classdata.tpk game.assets logo.png 127
```

A stripped MonoBehaviour requires a compatible embedded TypeTree. The Unity version, serialized format, TypeID, TypeHash and ScriptIdHash must match. The tool can discover matching trees in game files and bundles. Use `--typetree-root` or batch `typeTreeRoots` to direct the search, or pass an explicit donor with `--typetree-source`. Conflicting definitions stop the import. A TPK cannot supply a MonoBehaviour layout. For supported TextMeshPro fonts, a complete original UABEA dump can also serve as the schema when it reproduces the original serialized bytes exactly.

## Batch replacements

Group replacements by source so the tool opens, writes and compresses each bundle once. Paths are relative to the plan. Create output directories before running it.

```json
{
  "typeTreeRoots": ["Game_Data"],
  "maxWorkers": 1,
  "sources": [
    {
      "source": "Game_Data/example.bundle",
      "output": "staging/example.bundle",
      "operations": [
        { "replacement": "translation.txt", "pathId": -900000000000000001 },
        { "replacement": "logo.png", "pathId": 127, "entry": "CAB-example" }
      ]
    }
  ]
}
```

```sh
dotnet run --project NewUAFGJ -c Release -- --profile batch plan.json
```

Operations within a source run in order. Each source is published after all its replacements pass verification. A plan with several sources can produce outputs for successful sources before another source fails. Omit `output` for in-place replacement. Set `maxWorkers` to process independent sources concurrently and `sameSourceDelaySeconds` to add an optional pause within each source.

Use `entry` to select a serialized entry in a bundle. Individual operations also accept `fileId`, `typeTreeSource`, `raw`, `downscale`, `resizeAtlas` and `syncAtlasMaterials`. Each of those properties is optional.

Use `--profile` for timing details on both single and batch commands. It reports stages such as decoding, texture encoding, bundle compression and verification.

## Troubleshooting

- If several objects match, provide a PathID. In batch mode, use `entry` when the same PathID appears in multiple serialized entries.
- If TypeTree resolution fails, check the target's Unity version and the donor's layout. For stripped MonoBehaviours, point the search at the original game files that contain matching embedded trees.
- If a PNG has different dimensions, check whether it is a larger color image eligible for `--downscale` or an Alpha8 atlas that needs an explicit resize.
- If an interrupted import leaves a `<source>.newuafgj.tmp` file, confirm that no process is using it before deleting it and retrying.

Success returns exit code 0. Errors return exit code 1 and report a `[FATAL]` diagnostic.

## Legal / Rights Notice

This project does not claim ownership of any third-party intellectual property contained within any game.

## License

[ISC License](LICENSE)
