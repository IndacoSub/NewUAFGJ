using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.IO;

namespace UAFGJ
{
	partial class Program
	{
		// ============================================================
		// MATERIAL - TYPEID 21
		// ============================================================

		private static bool ImportMaterial(string inputFile,
										   AssetsTools.NET.AssetTypeValueField baseField,
										   AssetFileInfo afie, string fileKind,
										   out byte[] originalSerializedData,
										   out byte[] replacementData)
		{
			originalSerializedData = Array.Empty<byte>();

			replacementData = Array.Empty<byte>();

			if (afie == null)
			{
				throw new InvalidOperationException("[MATERIAL] AssetFileInfo is null.");
			}

			if (afie.TypeId != 21)
			{
				throw new InvalidDataException($"[MATERIAL] Material importer requires TypeID=21, " +
											   $"but target PID={afie.PathId} has TypeID={afie.TypeId}.");
			}

			if (baseField == null || baseField.IsDummy)
			{
				throw new InvalidDataException($"[MATERIAL] BaseField is null/dummy " +
											   $"for PID={afie.PathId}.");
			}

			if (!File.Exists(inputFile))
			{
				throw new FileNotFoundException("[MATERIAL] TXT input not found.", inputFile);
			}

			string normalizedKind = fileKind?.Trim() ?? "";

			if (string.IsNullOrWhiteSpace(normalizedKind))
			{
				normalizedKind = "MATERIAL_FULL_CHECKED";
			}

			if (!string.Equals(normalizedKind, "MATERIAL_FULL", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(normalizedKind, "MATERIAL_FULL_CHECKED",
							   StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"[MATERIAL] Unsupported fileKind " + $"'{fileKind}'. " +
											   "Expected MATERIAL_FULL or MATERIAL_FULL_CHECKED.");
			}

			bool checkedMode = string.Equals(normalizedKind, "MATERIAL_FULL_CHECKED",
											 StringComparison.OrdinalIgnoreCase);

			DebugStr($"[MATERIAL] Importing Material " + $"PID={afie.PathId}, " +
					 $"asset='{GetAssetName(baseField)}', " + $"checked={checkedMode}");

			LogPhase($"MATERIAL import starting PID={afie.PathId}, " + $"checked={checkedMode}.");

			// ========================================================
			// ORIGINAL SERIALIZED DATA
			// ========================================================

			originalSerializedData = baseField.WriteToByteArray();

			if (originalSerializedData == null || originalSerializedData.Length == 0)
			{
				throw new InvalidDataException($"[MATERIAL] Original serialized Material " +
											   $"has zero bytes for PID={afie.PathId}.");
			}

			DebugStr($"[MATERIAL] Original serialized size=" + $"{originalSerializedData.Length} bytes " +
					 $"SHA256={Sha256Hex(originalSerializedData)}");

			// ========================================================
			// DUMP INFORMATION
			// ========================================================

			var dumpScalars = ReadDumpScalars(inputFile);

			DebugStr($"[MATERIAL] Dump scalar count=" + $"{dumpScalars.Count}");

			var dumpArrays = ReadDumpArrayInfos(inputFile);

			DebugStr($"[MATERIAL] Dump array count=" + $"{dumpArrays.Count}");

			// ========================================================
			// APPLY FULL MATERIAL DUMP
			//
			// Material TXT is treated as a complete serialized
			// object. The existing generic checked mapper is used.
			//
			// This preserves:
			//
			//   m_Name
			//   m_Shader
			//   m_ShaderKeywords
			//   m_LightmapFlags
			//   m_EnableInstancingVariants
			//   m_DoubleSidedGI
			//   m_CustomRenderQueue
			//   m_SavedProperties
			//   m_TexEnvs
			//   m_Floats
			//   m_Colors
			// ========================================================

			DebugStr("[MATERIAL] Applying serialized Material TXT " +
					 "through generic structural mapper.");

			replacementData = ApplyTextDumpToBaseField(inputFile, baseField);

			if (replacementData == null || replacementData.Length == 0)
			{
				throw new InvalidDataException($"[MATERIAL] Modified Material serialized " +
											   $"to zero bytes for PID={afie.PathId}.");
			}

			DebugStr($"[MATERIAL] Modified Material serialized: " + $"{replacementData.Length} bytes " +
					 $"SHA256={Sha256Hex(replacementData)}");

			LogPhase($"MATERIAL import finished PID={afie.PathId}; " +
					 $"originalBytes={originalSerializedData.Length}, " +
					 $"newBytes={replacementData.Length}.");

			return true;
		}
	}
}