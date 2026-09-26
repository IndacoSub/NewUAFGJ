using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.IO;
using System.Collections.Generic;

namespace UAFGJ
{
	partial class Program
	{
		// ============================================================
		// UNITY FONT - TYPEID 128
		// ============================================================

		private static bool LooksLikeFontDump(
	string inputFile)
		{
			if (string.IsNullOrWhiteSpace(inputFile) ||
				!File.Exists(inputFile))
			{
				return false;
			}

			bool hasFontInfo =
				false;

			bool hasGlyphInfo =
				false;

			bool hasFontCreationSettings =
				false;

			try
			{
				using (var reader =
					new StreamReader(
						inputFile,
						true))
				{
					while (true)
					{
						string line =
							reader.ReadLine();

						if (line == null)
							break;

						if (!hasFontInfo &&
							line.IndexOf(
								"m_fontInfo",
								StringComparison.OrdinalIgnoreCase) >= 0)
						{
							hasFontInfo = true;
						}

						if (!hasGlyphInfo &&
							line.IndexOf(
								"m_glyphInfoList",
								StringComparison.OrdinalIgnoreCase) >= 0)
						{
							hasGlyphInfo = true;
						}

						if (!hasFontCreationSettings &&
							line.IndexOf(
								"fontCreationSettings",
								StringComparison.OrdinalIgnoreCase) >= 0)
						{
							hasFontCreationSettings = true;
						}

						if (hasFontInfo &&
							hasGlyphInfo &&
							hasFontCreationSettings)
						{
							DebugStr(
								$"[FONT] Dump detected as TMP_FontAsset: " +
								$"fontInfo={hasFontInfo}, " +
								$"glyphInfo={hasGlyphInfo}, " +
								$"fontCreationSettings={hasFontCreationSettings}");

							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[FONT] Font dump detection failed for " +
					$"'{inputFile}': " +
					$"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			DebugStr(
				$"[FONT] Dump is not recognized as TMP_FontAsset: " +
				$"fontInfo={hasFontInfo}, " +
				$"glyphInfo={hasGlyphInfo}, " +
				$"fontCreationSettings={hasFontCreationSettings}");

			return false;
		}

		private static bool ImportUnityFont(
			string inputFile,
			AssetsManager am,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			string assetName,
			out AssetTypeValueField modifiedBaseField,
			out byte[] replacementData,
			out byte[] originalSerializedData)
		{
			return ImportUnityFontInternal(
				inputFile,
				am,
				afie,
				assetInst,
				assetName,
				false,
				out modifiedBaseField,
				out replacementData,
				out originalSerializedData);
		}

		private static bool ImportUnityFontChecked(
			string inputFile,
			AssetsManager am,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			string assetName,
			out AssetTypeValueField modifiedBaseField,
			out byte[] replacementData,
			out byte[] originalSerializedData)
		{
			return ImportUnityFontInternal(
				inputFile,
				am,
				afie,
				assetInst,
				assetName,
				true,
				out modifiedBaseField,
				out replacementData,
				out originalSerializedData);
		}

		private static bool ImportUnityFontInternal(
	string inputFile,
	AssetsManager am,
	AssetFileInfo afie,
	AssetsFileInstance assetInst,
	string assetName,
	bool checkedMode,
	out AssetTypeValueField modifiedBaseField,
	out byte[] replacementData,
	out byte[] originalSerializedData)
		{
			modifiedBaseField =
				null;

			replacementData =
				Array.Empty<byte>();

			originalSerializedData =
				Array.Empty<byte>();

			if (assetInst == null)
			{
				throw new InvalidOperationException(
					"assetInst is null.");
			}

			if (am == null)
			{
				throw new InvalidOperationException(
					"AssetsManager is null.");
			}

			if (afie == null)
			{
				throw new InvalidOperationException(
					"AssetFileInfo is null.");
			}

			if (!File.Exists(inputFile))
			{
				throw new FileNotFoundException(
					"TXT input not found.",
					inputFile);
			}

			bool isUnityFontType =
				afie.TypeId == 128;

			bool isTmpFontAsset =
				afie.TypeId == 114;

			if (!isUnityFontType &&
				!isTmpFontAsset)
			{
				throw new InvalidDataException(
					$"Font importer requires TypeID=128 or TypeID=114, " +
					$"but target PID={afie.PathId} has TypeID={afie.TypeId}.");
			}

			string targetKind =
				isTmpFontAsset
					? "TMP_FontAsset (MonoBehaviour)"
					: "Unity Font";

			DebugStr(
				$"[FONT] Importing {targetKind} " +
				$"PID={afie.PathId}, " +
				$"asset='{assetName}', " +
				$"checked={checkedMode}");

			LogPhase(
				$"FONT import starting PID={afie.PathId}, " +
				$"TypeID={afie.TypeId}, " +
				$"checked={checkedMode}.");

			// ========================================================
			// LOAD BASEFIELD
			// ========================================================

			DebugStr(
				$"[FONT] Requesting BaseField from AssetsTools.NET " +
				$"for PID={afie.PathId}.");

			AssetTypeValueField baseField =
				am.GetBaseField(
					assetInst,
					afie);

			if (baseField == null ||
				baseField.IsDummy)
			{
				throw new InvalidDataException(
					"AssetsTools.NET returned a null/dummy " +
					"BaseField for Font/TMP_FontAsset.");
			}

			// ========================================================
			// ORIGINAL DATA
			// ========================================================

			originalSerializedData =
				baseField.WriteToByteArray();

			if (originalSerializedData == null ||
				originalSerializedData.Length == 0)
			{
				throw new InvalidDataException(
					$"Original Font serialized to zero bytes " +
					$"for PID={afie.PathId}.");
			}

			DebugStr(
				$"[FONT] Original serialized size=" +
				$"{originalSerializedData.Length} bytes " +
				$"SHA256={Sha256Hex(originalSerializedData)}");

			// ========================================================
			// READ DUMP
			// ========================================================

			List<DumpScalar> dumpScalars =
				ReadDumpScalars(
					inputFile);

			DebugStr(
				$"[FONT] Dump scalar count={dumpScalars.Count}");

			// ========================================================
			// FONT STRUCTURE RECONSTRUCTION
			//
			// IMPORTANT:
			//
			// The TXT dump may be partial.
			//
			// Arrays present in the dump:
			//     -> resized to the dump count.
			//
			// Arrays absent from the dump:
			//     -> cleared.
			//
			// Scalars present in the dump:
			//     -> structurally checked.
			//
			// Scalars absent from the dump:
			//     -> allowed.
			// ========================================================

			DebugStr(
				"[FONT] Synchronizing array structure " +
				"from partial dump.");

			SynchronizeFontDumpArrayStructure(
				inputFile,
				baseField);

			DebugStr(
				"[FONT] Building Font-specific partial scalar mapping.");

			List<DumpTargetMatch> matches =
				BuildFontDumpTargetMatches(
					inputFile,
					baseField);

			if (matches == null)
			{
				throw new InvalidDataException(
					"[FONT] Font scalar mapping returned null.");
			}

			DebugStr(
				$"[FONT] Font scalar mapping produced " +
				$"{matches.Count} matches.");

			foreach (DumpTargetMatch match in matches)
			{
				if (match == null ||
					match.Dump == null ||
					match.Target == null ||
					match.Target.Field == null)
				{
					throw new InvalidDataException(
						"FONT scalar mapping produced a null match.");
				}

				try
				{
					ApplyDumpValue(
						match.Target.Field,
						match.Dump);
				}
				catch (Exception ex)
				{
					throw new InvalidDataException(
						$"Unable to apply FONT field " +
						$"path='{match.Dump.Path}', " +
						$"line={match.Dump.LineNumber}.",
						ex);
				}
			}

			// ========================================================
			// OPTIONAL CHECKED VALIDATION
			// ========================================================

			if (checkedMode)
			{
				DebugStr(
					"[FONT] Running PARTIAL checked validation.");

				ValidateFontDumpAgainstBaseField(
					inputFile,
					baseField);
			}

			// ========================================================
			// SERIALIZE
			// ========================================================

			replacementData =
				baseField.WriteToByteArray();

			if (replacementData == null ||
				replacementData.Length == 0)
			{
				throw new InvalidDataException(
					$"Modified Font serialized to zero bytes " +
					$"for PID={afie.PathId}.");
			}

			DebugStr(
				$"[FONT] Modified Font serialized: " +
				$"{replacementData.Length} bytes " +
				$"SHA256={Sha256Hex(replacementData)}");

			modifiedBaseField =
				baseField;

			LogPhase(
				$"FONT import finished PID={afie.PathId}; " +
				$"TypeID={afie.TypeId}; " +
				$"originalBytes={originalSerializedData.Length}, " +
				$"newBytes={replacementData.Length}.");

			return true;
		}
	}
}