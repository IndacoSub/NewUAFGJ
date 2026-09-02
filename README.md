# NewUAFGJ

## About

**NewUAFGJ** is a custom Unity asset manipulation and automation tool developed by **Team DAIX** for personal use.

The [original UAFGJ](https://github.com/IndacoSub/UABEA/tree/custom/UAFGJ) was created to enable Team DAIX's translation of Danganronpa V3 to support the Nintendo Switch version.

The updated project was created as part of an **amateur, personal translation project for Tokyo Psychodemic**.

This is an **unofficial fan translation**. It is **not official, sponsored, endorsed, authorized, or affiliated** in any way with the developers, publishers, or rights holders of Tokyo Psychodemic.

There are currently **no plans to publicly release the translation**.

NewUAFGJ itself is a development and automation tool used to assist with the processing of locally stored game assets.

> **AI USAGE DISCLAIMER**
>
> This project has been developed with the assistance of Artificial Intelligence tools.
> AI was used during development for code generation, refactoring, debugging, API migration, documentation, and troubleshooting.
>
> The generated code has been reviewed, adapted, and integrated by **Team DAIX**. AI assistance does not imply that every generated component is error-free or guaranteed to behave correctly in every environment.
>
> This disclaimer also applies to this README and other documentation that may have been generated or assisted by AI.

## What we did

The project started from an older UABEA / AssetsTools.NET based workflow and was progressively rebuilt into a more focused standalone tool.

The main goal was to create a reliable automated pipeline for the personal translation work, reducing the amount of repetitive manual editing required when working with Unity assets.

### Unity AssetBundle handling

NewUAFGJ can locate and modify assets contained inside Unity AssetBundles.

Assets can be identified using serialized information such as PathID and asset type, allowing specific resources to be replaced programmatically.

The writing process uses temporary staging files and validation before replacing the original bundle.

### Texture2D importing

A custom PNG import pipeline was implemented for Unity `Texture2D` assets.

The importer can:

* load PNG images;
* resize them when necessary;
* preserve the required texture orientation;
* update the relevant Unity `Texture2D` fields;
* replace embedded image data;
* handle compressed formats such as **DXT1 / BC1**.

Dedicated encoding logic was also added for formats that are not supported by the managed encoder available in the installed AssetsTools.NET.Texture runtime.

### TextAsset replacement

The tool can replace serialized Unity `TextAsset` contents programmatically.

This is particularly useful for translating game text while keeping the surrounding Unity asset structure intact.

### MonoBehaviour processing

NewUAFGJ also handles serialized `MonoBehaviour` data.

A significant part of the work involved Unity TypeTrees and custom serialized classes, since generic class database information is not always sufficient for complex game-specific MonoBehaviours.

The project therefore makes use of managed assembly information when generating the structures required to read and write custom serialized data.

### AssetsTools.NET migration

The original code relied on an older generation of AssetsTools.NET APIs.

The project was progressively migrated to the newer **AssetsTools.NET 3.x** API.

This required updating several parts of the project, including:

* asset replacement;
* AssetBundle writing;
* compression handling;
* asset offsets;
* TypeTree handling;
* texture encoding;
* MonoBehaviour serialization.

The migration was performed to reduce dependency on obsolete APIs and provide a cleaner foundation for the tool.

## Why this project exists

The tool automates repetitive asset modification tasks so that translated files can be processed consistently and efficiently.

It is primarily a private utility for the contributors to this repository rather than a general-purpose Unity modding framework.

## Scope and limitations

Compatibility with miscellaneous Unity games, Unity versions, AssetBundles, or custom MonoBehaviour structures is not guaranteed.

Serialized `MonoBehaviour` layouts and TypeTrees can vary considerably between games and Unity versions.

## Credits

Developed by **Team DAIX**.

The project makes use of the [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) ecosystem and other open-source libraries and tools, by nesrak1.

## Legal / Rights Notice

This project does not claim ownership of any third-party intellectual property contained within any game.

---

### License

This repository is licensed under the ISC license.
