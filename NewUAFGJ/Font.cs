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
			modifiedBaseField = null;
			replacementData = Array.Empty<byte>();
			originalSerializedData = Array.Empty<byte>();

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

			if (afie.TypeId != 128)
			{
				throw new InvalidDataException(
					$"Unity Font importer requires TypeID=128, " +
					$"but target PID={afie.PathId} has TypeID={afie.TypeId}.");
			}

			DebugStr(
				$"[FONT] Importing Unity Font " +
				$"PID={afie.PathId}, " +
				$"asset='{assetName}', " +
				$"checked={checkedMode}");

			LogPhase(
				$"FONT import starting PID={afie.PathId}, " +
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
					"BaseField for Unity Font.");
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
					$"Original Unity Font serialized to zero bytes " +
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
			// RECONSTRUCT FONT STRUCTURE FROM THE DUMP
			//
			// IMPORTANT:
			//
			// A Font TXT is intentionally allowed to be PARTIAL.
			//
			// Therefore:
			//
			//   - arrays present in the TXT are resized to the dump size;
			//   - arrays absent from the TXT are cleared;
			//   - omitted scalar fields are not required to exist in
			//     the dump;
			//   - scalar fields that ARE present are structurally checked.
			// ========================================================

			DebugStr(
				"[FONT] Synchronizing array structure " +
				"from partial dump.");

			DebugStr(
	"[FONT] Synchronizing array structure from partial dump.");

			SynchronizeFontDumpArrayStructure(
				inputFile,
				baseField);

			DebugStr(
				"[FONT] Building Font-specific partial scalar mapping.");

			List<DumpTargetMatch> matches =
				BuildFontDumpTargetMatches(
					inputFile,
					baseField);

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
					$"Modified Unity Font serialized to zero bytes " +
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
				$"originalBytes={originalSerializedData.Length}, " +
				$"newBytes={replacementData.Length}.");

			return true;
		}
	}
}