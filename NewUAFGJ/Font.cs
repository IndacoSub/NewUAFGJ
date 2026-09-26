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
		// FONT DUMP DETECTION
		// ============================================================

		private static bool LooksLikeFontDump(string inputFile)
		{
			if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
			{
				return false;
			}

			bool hasFontInfo = false;
			bool hasGlyphInfo = false;
			bool hasFontCreationSettings = false;

			try
			{
				using (var reader = new StreamReader(inputFile, true))
				{
					while (true)
					{
						string line = reader.ReadLine();

						if (line == null)
							break;

						if (!hasFontInfo &&
							line.IndexOf("m_fontInfo", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							hasFontInfo = true;
						}

						if (!hasGlyphInfo &&
							line.IndexOf("m_glyphInfoList", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							hasGlyphInfo = true;
						}

						if (!hasFontCreationSettings &&
							line.IndexOf("fontCreationSettings", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							hasFontCreationSettings = true;
						}

						if (hasFontInfo && hasGlyphInfo && hasFontCreationSettings)
						{
							DebugStr($"[FONT] Dump detected as TMP_FontAsset: " + $"fontInfo={hasFontInfo}, " +
									 $"glyphInfo={hasGlyphInfo}, " +
									 $"fontCreationSettings={hasFontCreationSettings}");

							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				DebugStr($"[FONT] Font dump detection failed for " +
						 $"'{inputFile}': " + $"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			DebugStr($"[FONT] Dump is not recognized as TMP_FontAsset: " + $"fontInfo={hasFontInfo}, " +
					 $"glyphInfo={hasGlyphInfo}, " + $"fontCreationSettings={hasFontCreationSettings}");

			return false;
		}

		// ============================================================
		// PUBLIC ENTRY POINTS
		// ============================================================

		private static bool ImportUnityFont(string inputFile, AssetsManager am, AssetFileInfo afie,
											AssetsFileInstance assetInst, string assetName,
											out AssetTypeValueField modifiedBaseField,
											out byte[] replacementData,
											out byte[] originalSerializedData)
		{
			return ImportUnityFontInternal(inputFile, am, afie, assetInst, assetName, false,
										   out modifiedBaseField, out replacementData,
										   out originalSerializedData);
		}

		private static bool ImportUnityFontChecked(string inputFile, AssetsManager am,
												   AssetFileInfo afie, AssetsFileInstance assetInst,
												   string assetName,
												   out AssetTypeValueField modifiedBaseField,
												   out byte[] replacementData,
												   out byte[] originalSerializedData)
		{
			return ImportUnityFontInternal(inputFile, am, afie, assetInst, assetName, true,
										   out modifiedBaseField, out replacementData,
										   out originalSerializedData);
		}

		// ============================================================
		// MAIN DISPATCH
		// ============================================================

		private static bool ImportUnityFontInternal(string inputFile, AssetsManager am,
													AssetFileInfo afie, AssetsFileInstance assetInst,
													string assetName, bool checkedMode,
													out AssetTypeValueField modifiedBaseField,
													out byte[] replacementData,
													out byte[] originalSerializedData)
		{
			modifiedBaseField = null;
			replacementData = Array.Empty<byte>();
			originalSerializedData = Array.Empty<byte>();

			if (assetInst == null)
				throw new InvalidOperationException("assetInst is null.");

			if (am == null)
				throw new InvalidOperationException("AssetsManager is null.");

			if (afie == null)
				throw new InvalidOperationException("AssetFileInfo is null.");

			if (!File.Exists(inputFile))
				throw new FileNotFoundException("TXT input not found.", inputFile);

			bool isUnityFontType = afie.TypeId == 128;
			bool isTmpFontAsset = afie.TypeId == 114;

			if (!isUnityFontType && !isTmpFontAsset)
			{
				throw new InvalidDataException($"Font importer requires TypeID=128 or TypeID=114, " +
											   $"but target PID={afie.PathId} has TypeID={afie.TypeId}.");
			}

			/*
			 * --------------------------------------------------------
			 * CRITICAL:
			 *
			 * TMP_FontAsset TypeID=114 gets its own importer.
			 *
			 * It MUST NOT use the generic structural dump mapper.
			 * --------------------------------------------------------
			 */

			if (isTmpFontAsset)
			{
				return ImportTmpFontAssetInternal(inputFile, am, afie, assetInst, assetName, checkedMode,
												  out modifiedBaseField, out replacementData,
												  out originalSerializedData);
			}

			/*
			 * --------------------------------------------------------
			 * TYPEID 128
			 *
			 * Keep the old generic Unity Font behavior here.
			 * --------------------------------------------------------
			 */

			return ImportUnityFontType128Internal(inputFile, am, afie, assetInst, assetName, checkedMode,
												  out modifiedBaseField, out replacementData,
												  out originalSerializedData);
		}

		// ============================================================
		// TMP FONT ASSET - TYPEID 114
		// ============================================================

		private static bool ImportTmpFontAssetInternal(string inputFile, AssetsManager am,
													   AssetFileInfo afie, AssetsFileInstance assetInst,
													   string assetName, bool checkedMode,
													   out AssetTypeValueField modifiedBaseField,
													   out byte[] replacementData,
													   out byte[] originalSerializedData)
		{
			modifiedBaseField = null;
			replacementData = Array.Empty<byte>();
			originalSerializedData = Array.Empty<byte>();

			DebugStr($"[FONT] Importing TMP_FontAsset specifically " +
					 $"PID={afie.PathId}, asset='{assetName}', checked={checkedMode}");

			LogPhase($"TMP_FontAsset import starting PID={afie.PathId}, checked={checkedMode}.");

			/*
			 * --------------------------------------------------------
			 * LOAD BASEFIELD
			 * --------------------------------------------------------
			 */

			AssetTypeValueField baseField = am.GetBaseField(assetInst, afie);

			if (baseField == null || baseField.IsDummy)
			{
				throw new InvalidDataException("AssetsTools.NET returned a null/dummy BaseField " +
											   "for TMP_FontAsset.");
			}

			/*
			 * --------------------------------------------------------
			 * ORIGINAL SERIALIZED DATA
			 * --------------------------------------------------------
			 */

			originalSerializedData = baseField.WriteToByteArray();

			if (originalSerializedData == null || originalSerializedData.Length == 0)
			{
				throw new InvalidDataException("Original TMP_FontAsset serialized to zero bytes.");
			}

			DebugStr($"[FONT] Original serialized size=" + $"{originalSerializedData.Length} bytes " +
					 $"SHA256={Sha256Hex(originalSerializedData)}");

			/*
			 * --------------------------------------------------------
			 * LOG IMPORTANT REFERENCES
			 *
			 * These MUST remain unchanged.
			 * --------------------------------------------------------
			 */

			LogTmpFontPPtr("BEFORE atlas", baseField, "atlas");

			LogTmpFontPPtr("BEFORE material", baseField, "material");

			/*
			 * --------------------------------------------------------
			 * SNAPSHOT EVERYTHING OUTSIDE:
			 *
			 *     m_fontInfo
			 *     m_glyphInfoList
			 *
			 * If anything else changes, this importer aborts.
			 * --------------------------------------------------------
			 */

			Dictionary<string, byte[]> protectedSnapshot = CaptureProtectedTmpFontFields(baseField);

			/*
			 * --------------------------------------------------------
			 * READ DUMP
			 * --------------------------------------------------------
			 */

			List<DumpScalar> allDumpScalars = ReadDumpScalars(inputFile);

			List<DumpScalar> editableDumpScalars = new List<DumpScalar>();

			for (int i = 0; i < allDumpScalars.Count; i++)
			{
				DumpScalar dump = allDumpScalars[i];

				if (dump == null)
					continue;

				if (IsTmpFontEditablePath(dump.Path))
				{
					editableDumpScalars.Add(dump);
				}
			}

			int ignoredScalarCount = allDumpScalars.Count - editableDumpScalars.Count;

			DebugStr($"[FONT] Dump scalars total={allDumpScalars.Count}, " +
					 $"editable={editableDumpScalars.Count}, " + $"ignored={ignoredScalarCount}");

			/*
			 * --------------------------------------------------------
			 * GLYPH ARRAY ONLY
			 *
			 * Do NOT resize:
			 *
			 *     fontWeights
			 *     fallbackFontAssets
			 *     kerningPairs
			 *
			 * Only m_glyphInfoList is synchronized.
			 * --------------------------------------------------------
			 */

			int glyphCount = GetTmpFontDumpGlyphCount(inputFile);

			DebugStr($"[FONT] TMP dump glyph count={glyphCount}");

			if (glyphCount <= 0)
			{
				throw new InvalidDataException($"[FONT] TMP dump contains an invalid " +
											   $"m_glyphInfoList count: {glyphCount}");
			}

			if (!TryResolveLogicalPath(baseField, "m_glyphInfoList",
									   out AssetTypeValueField glyphArrayField))
			{
				throw new InvalidDataException("[FONT] Target does not contain m_glyphInfoList.");
			}

			int originalGlyphCount = GetArrayChildCount(glyphArrayField);

			DebugStr($"[FONT] m_glyphInfoList target={originalGlyphCount}, " + $"dump={glyphCount}");

			if (originalGlyphCount != glyphCount)
			{
				ResizeTargetArray(glyphArrayField, glyphCount);
			}

			/*
			 * --------------------------------------------------------
			 * BUILD MATCHES ONLY FOR:
			 *
			 *     m_fontInfo
			 *     m_glyphInfoList
			 *
			 * Everything else is ignored.
			 * --------------------------------------------------------
			 */

			List<DumpTargetMatch> matches = BuildTmpFontEditableMatches(editableDumpScalars, baseField);

			DebugStr($"[FONT] TMP editable structural mapping produced " + $"{matches.Count} matches.");

			/*
			 * --------------------------------------------------------
			 * APPLY ONLY EDITABLE FONT DATA
			 * --------------------------------------------------------
			 */

			foreach (DumpTargetMatch match in matches)
			{
				if (match == null || match.Dump == null || match.Target == null ||
					match.Target.Field == null)
				{
					throw new InvalidDataException("[FONT] TMP mapping produced a null match.");
				}

				try
				{
					ApplyDumpValue(match.Target.Field, match.Dump);
				}
				catch (Exception ex)
				{
					throw new InvalidDataException($"[FONT] Unable to apply editable field " +
													   $"path='{match.Dump.Path}', " +
													   $"line={match.Dump.LineNumber}.",
												   ex);
				}
			}

			/*
			 * --------------------------------------------------------
			 * IMPORTANT:
			 *
			 * Do NOT touch:
			 *
			 *     atlas
			 *     material
			 *     m_kerningInfo
			 *     fallbackFontAssets
			 *     fontCreationSettings
			 *     m_CreationSettings
			 *     fontWeights
			 *     normalStyle
			 *     boldStyle
			 *     italicStyle
			 *     tabSize
			 *     etc.
			 * --------------------------------------------------------
			 */

			/*
			 * --------------------------------------------------------
			 * PROTECTED FIELD VALIDATION
			 * --------------------------------------------------------
			 */

			ValidateProtectedTmpFontFields(baseField, protectedSnapshot);

			LogTmpFontPPtr("AFTER atlas", baseField, "atlas");

			LogTmpFontPPtr("AFTER material", baseField, "material");

			/*
			 * --------------------------------------------------------
			 * CHECKED VALIDATION
			 * --------------------------------------------------------
			 */

			if (checkedMode)
			{
				DebugStr("[FONT] Running TMP-specific checked validation.");

				List<DumpTargetMatch> validationMatches =
					BuildTmpFontEditableMatches(editableDumpScalars, baseField);

				if (validationMatches.Count != matches.Count)
				{
					throw new InvalidDataException(
						$"[FONT] Validation match count changed: " + $"apply={matches.Count}, " +
						$"validate={validationMatches.Count}.");
				}

				for (int i = 0; i < validationMatches.Count; i++)
				{
					DumpTargetMatch match = validationMatches[i];

					if (match == null || match.Dump == null || match.Target == null ||
						match.Target.Field == null)
					{
						throw new InvalidDataException("[FONT] Validation encountered a null match.");
					}

					ValidateTmpFontDumpValue(match.Target.Field, match.Dump);
				}

				DebugStr($"[CHECK] TMP_FONT editable validation PASSED: " +
						 $"{validationMatches.Count} fields.");
			}

			/*
			 * --------------------------------------------------------
			 * SERIALIZE
			 * --------------------------------------------------------
			 */

			replacementData = baseField.WriteToByteArray();

			if (replacementData == null || replacementData.Length == 0)
			{
				throw new InvalidDataException("Modified TMP_FontAsset serialized to zero bytes.");
			}

			DebugStr($"[FONT] Modified TMP_FontAsset serialized: " + $"{replacementData.Length} bytes " +
					 $"SHA256={Sha256Hex(replacementData)}");

			modifiedBaseField = baseField;

			LogPhase($"TMP_FontAsset import finished PID={afie.PathId}; " +
					 $"originalBytes={originalSerializedData.Length}, " +
					 $"newBytes={replacementData.Length}.");

			return true;
		}

		// ============================================================
		// TMP EDITABLE VALUE VALIDATION
		// ============================================================
		//
		// Valida esclusivamente i valori che abbiamo deciso di importare:
		//
		//     m_fontInfo
		//     m_glyphInfoList[*]
		//
		// NON modifica il campo.
		// ============================================================

		private static void ValidateTmpFontDumpValue(AssetTypeValueField target, DumpScalar dump)
		{
			if (target == null || target.IsDummy || target.Value == null)
			{
				throw new InvalidDataException($"[FONT] TMP validation target is null/dummy " +
											   $"at path='{dump?.Path}'.");
			}

			if (dump == null)
			{
				throw new InvalidDataException("[FONT] TMP validation dump is null.");
			}

			AssetValueType valueType = target.Value.ValueType;

			// ========================================================
			// STRING
			// ========================================================

			if (valueType == AssetValueType.String)
			{
				string expected = ParseDumpString(dump.Value);

				string actual = target.AsString ?? "";

				if (!string.Equals(expected, actual, StringComparison.Ordinal))
				{
					throw new InvalidDataException($"[FONT] TMP string mismatch: " + $"path='{dump.Path}', " +
												   $"line={dump.LineNumber}, " + $"expected='{expected}', " +
												   $"actual='{actual}'.");
				}

				return;
			}

			// ========================================================
			// FLOAT
			// ========================================================

			if (valueType == AssetValueType.Float)
			{
				float expected = ParseSingle(dump.Value);

				float actual = target.AsFloat;

				if (!AreFloatsEqual(expected, actual))
				{
					throw new InvalidDataException(
						$"[FONT] TMP float mismatch: " + $"path='{dump.Path}', " +
						$"line={dump.LineNumber}, " + $"expected='{dump.Value}', " +
						$"actual='{actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}'.");
				}

				return;
			}

			// ========================================================
			// DOUBLE
			// ========================================================

			if (valueType == AssetValueType.Double)
			{
				double expected = ParseDouble(dump.Value);

				double actual = target.AsDouble;

				if (!AreDoublesEqual(expected, actual))
				{
					throw new InvalidDataException(
						$"[FONT] TMP double mismatch: " + $"path='{dump.Path}', " +
						$"line={dump.LineNumber}, " + $"expected='{dump.Value}', " +
						$"actual='{actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}'.");
				}

				return;
			}

			// ========================================================
			// BOOL
			// ========================================================

			if (valueType == AssetValueType.Bool)
			{
				bool expected = bool.Parse(dump.Value);

				bool actual = target.AsBool;

				if (expected != actual)
				{
					throw new InvalidDataException($"[FONT] TMP bool mismatch: " + $"path='{dump.Path}', " +
												   $"line={dump.LineNumber}, " + $"expected='{expected}', " +
												   $"actual='{actual}'.");
				}

				return;
			}

			// ========================================================
			// INTEGER TYPES
			// ========================================================

			if (valueType == AssetValueType.UInt8 || valueType == AssetValueType.Int8 ||
				valueType == AssetValueType.UInt16 || valueType == AssetValueType.Int16 ||
				valueType == AssetValueType.UInt32 || valueType == AssetValueType.Int32 ||
				valueType == AssetValueType.UInt64 || valueType == AssetValueType.Int64)
			{
				string actual = ReadFieldAsDumpValue(target);

				string expectedNormalized = NormalizeNumericLiteral(dump.Value);

				string actualNormalized = NormalizeNumericLiteral(actual);

				if (!NumericStringsEqual(expectedNormalized, actualNormalized, valueType))
				{
					throw new InvalidDataException(
						$"[FONT] TMP numeric mismatch: " + $"path='{dump.Path}', " +
						$"line={dump.LineNumber}, " + $"expected='{dump.Value}', " + $"actual='{actual}'.");
				}

				return;
			}

			// ========================================================
			// FALLBACK
			// ========================================================

			string actualValue = ReadFieldAsDumpValue(target);

			if (!string.Equals(actualValue, dump.Value, StringComparison.Ordinal))
			{
				throw new InvalidDataException($"[FONT] TMP value mismatch: " + $"path='{dump.Path}', " +
											   $"line={dump.LineNumber}, " + $"expected='{dump.Value}', " +
											   $"actual='{actualValue}'.");
			}
		}

		// ============================================================
		// TMP EDITABLE PATH FILTER
		// ============================================================

		private static bool IsTmpFontEditablePath(string path)
		{
			string canonical = CanonicalizeStructuralPath(path ?? "");

			if (string.IsNullOrEmpty(canonical))
				return false;

			/*
			 * Editable:
			 *
			 *     m_fontInfo
			 *     m_fontInfo/...
			 *
			 *     m_glyphInfoList
			 *     m_glyphInfoList[0]/...
			 */

			if (string.Equals(canonical, "m_fontInfo", StringComparison.Ordinal))
			{
				return true;
			}

			if (canonical.StartsWith("m_fontInfo/", StringComparison.Ordinal))
			{
				return true;
			}

			if (string.Equals(canonical, "m_glyphInfoList", StringComparison.Ordinal))
			{
				return true;
			}

			if (canonical.StartsWith("m_glyphInfoList[", StringComparison.Ordinal))
			{
				return true;
			}

			return false;
		}

		// ============================================================
		// TMP GLYPH COUNT
		// ============================================================

		private static int GetTmpFontDumpGlyphCount(string inputFile)
		{
			Dictionary<string, DumpArrayInfo> arrays =
				BuildUniqueDumpArrayInfoMap(ReadDumpArrayInfos(inputFile));

			foreach (DumpArrayInfo info in arrays.Values)
			{
				if (info == null || string.IsNullOrEmpty(info.Path))
				{
					continue;
				}

				string canonical = CanonicalizeStructuralPath(info.Path);

				if (string.Equals(canonical, "m_glyphInfoList", StringComparison.Ordinal))
				{
					return info.Count;
				}
			}

			throw new InvalidDataException(
				"[FONT] Dump does not contain m_glyphInfoList array information.");
		}

		// ============================================================
		// TMP TARGET ARRAY COUNT
		// ============================================================

		private static int GetArrayChildCount(AssetTypeValueField field)
		{
			if (field == null || field.IsDummy)
			{
				return 0;
			}

			AssetValueType type = GetFieldValueType(field);

			if (type == AssetValueType.Array)
			{
				return field.Children?.Count ?? 0;
			}

			/*
			 * Some AssetsTools.NET representations expose the
			 * actual Array node as a direct child.
			 */

			AssetTypeValueField arrayChild = FindDirectChildByName(field, "Array");

			if (arrayChild != null && !arrayChild.IsDummy &&
				GetFieldValueType(arrayChild) == AssetValueType.Array)
			{
				return arrayChild.Children?.Count ?? 0;
			}

			return 0;
		}

		// ============================================================
		// TMP STRUCTURAL MATCHING
		// ============================================================

		// ============================================================
		// TMP FINAL BUNDLE VALIDATION
		// ============================================================
		//
		// Valida ESCLUSIVAMENTE i dati che TMP_FontAsset importer
		// considera modificabili:
		//
		//     m_fontInfo
		//     m_glyphInfoList[*]
		//
		// NON valida:
		//
		//     atlas
		//     material
		//     boldStyle
		//     normalStyle
		//     italicStyle
		//     fontWeights
		//     fallbackFontAssets
		//     m_kerningInfo
		//     fontCreationSettings
		//     ecc.
		//
		// Questo è fondamentale perché il dump può contenere campi
		// appartenenti all'ambiente Unity che ha generato il dump,
		// mentre il font del gioco deve conservare i propri valori.
		// ============================================================

		private static void ValidateTmpFontDumpAgainstBaseField(
			string inputFile,
			AssetTypeValueField baseField)
		{
			if (string.IsNullOrWhiteSpace(inputFile))
			{
				throw new ArgumentException(
					"[FONT] TMP validation dump path is empty.",
					nameof(inputFile));
			}

			if (!File.Exists(inputFile))
			{
				throw new FileNotFoundException(
					"[FONT] TMP validation dump was not found.",
					inputFile);
			}

			if (baseField == null || baseField.IsDummy)
			{
				throw new InvalidDataException(
					"[FONT] TMP validation BaseField is null/dummy.");
			}

			List<DumpScalar> allDumpScalars =
				ReadDumpScalars(inputFile);

			List<DumpScalar> editableDumpScalars =
				new List<DumpScalar>();

			foreach (DumpScalar dump in allDumpScalars)
			{
				if (dump == null)
					continue;

				if (IsTmpFontEditablePath(dump.Path))
				{
					editableDumpScalars.Add(dump);
				}
			}

			DebugStr(
				$"[FONT] Final TMP validation: " +
				$"dumpTotal={allDumpScalars.Count}, " +
				$"editable={editableDumpScalars.Count}, " +
				$"ignored={allDumpScalars.Count - editableDumpScalars.Count}");

			/*
			 * ------------------------------------------------------------
			 * Make sure m_glyphInfoList has the dump structure before
			 * matching fields.
			 * ------------------------------------------------------------
			 */

			int glyphCount =
				GetTmpFontDumpGlyphCount(inputFile);

			if (!TryResolveLogicalPath(
					baseField,
					"m_glyphInfoList",
					out AssetTypeValueField glyphArrayField))
			{
				throw new InvalidDataException(
					"[FONT] Final validation target does not contain " +
					"m_glyphInfoList.");
			}

			int targetGlyphCount =
				GetArrayChildCount(glyphArrayField);

			if (targetGlyphCount != glyphCount)
			{
				throw new InvalidDataException(
					$"[FONT] Final validation glyph count mismatch: " +
					$"dump={glyphCount}, " +
					$"target={targetGlyphCount}.");
			}

			/*
			 * ------------------------------------------------------------
			 * Build only the editable TMP matches.
			 * ------------------------------------------------------------
			 */

			List<DumpTargetMatch> matches =
				BuildTmpFontEditableMatches(
					editableDumpScalars,
					baseField);

			DebugStr(
				$"[FONT] Final TMP validation matches=" +
				$"{matches.Count}");

			/*
			 * ------------------------------------------------------------
			 * Validate values.
			 * ------------------------------------------------------------
			 */

			foreach (DumpTargetMatch match in matches)
			{
				if (match == null ||
					match.Dump == null ||
					match.Target == null ||
					match.Target.Field == null)
				{
					throw new InvalidDataException(
						"[FONT] Final TMP validation produced a null match.");
				}

				ValidateTmpFontDumpValue(
					match.Target.Field,
					match.Dump);
			}

			DebugStr(
				$"[CHECK] TMP final editable validation PASSED: " +
				$"{matches.Count} fields.");
		}

		private static List<DumpTargetMatch> BuildTmpFontEditableMatches(
			List<DumpScalar> editableDumpScalars, AssetTypeValueField baseField)
		{
			if (editableDumpScalars == null)
			{
				throw new ArgumentNullException(nameof(editableDumpScalars));
			}

			if (baseField == null || baseField.IsDummy)
			{
				throw new InvalidDataException("TMP_FontAsset BaseField is null or dummy.");
			}

			List<ScalarFieldEntry> allTargets = CollectScalarFieldEntries(baseField);

			List<ScalarFieldEntry> editableTargets = new List<ScalarFieldEntry>();

			foreach (ScalarFieldEntry target in allTargets)
			{
				if (target == null)
					continue;

				if (IsTmpFontEditablePath(target.Path))
				{
					editableTargets.Add(target);
				}
			}

			DebugStr($"[FONT] TMP editable scalar mapping: " + $"dump={editableDumpScalars.Count}, " +
					 $"target={editableTargets.Count}");

			if (editableDumpScalars.Count != editableTargets.Count)
			{
				throw new InvalidDataException(
					$"[FONT] TMP editable scalar count mismatch: " + $"dump={editableDumpScalars.Count}, " +
					$"target={editableTargets.Count}.");
			}

			List<DumpTargetMatch> matches = new List<DumpTargetMatch>(editableDumpScalars.Count);

			for (int i = 0; i < editableDumpScalars.Count; i++)
			{
				DumpScalar dump = editableDumpScalars[i];

				ScalarFieldEntry target = editableTargets[i];

				if (dump == null || target == null || target.Field == null)
				{
					throw new InvalidDataException($"[FONT] Null TMP editable mapping " + $"at index {i}.");
				}

				string dumpType = NormalizeDumpType(dump.Type);

				string targetType = NormalizeDumpType(target.Type);

				bool typeCompatible =
					string.Equals(dumpType, targetType, StringComparison.OrdinalIgnoreCase);

				/*
				 * Unity font dumps can expose char while
				 * AssetsTools.NET may expose UInt8/SInt8.
				 */

				if (!typeCompatible &&
					string.Equals(dump.Type, "char", StringComparison.OrdinalIgnoreCase))
				{
					typeCompatible = string.Equals(targetType, "UInt8", StringComparison.OrdinalIgnoreCase) ||
									 string.Equals(targetType, "SInt8", StringComparison.OrdinalIgnoreCase);
				}

				if (!typeCompatible)
				{
					throw new InvalidDataException(
						$"[FONT] TMP type mismatch at dump line " +
						$"{dump.LineNumber}: " + $"dump='{dump.Type}', " + $"target='{target.Type}', " +
						$"dumpField='{dump.FieldName}', " + $"targetField='{target.FieldName}', " +
						$"dumpPath='{dump.Path}', " + $"targetPath='{target.Path}'.");
				}

				if (!string.Equals(dump.FieldName, target.FieldName, StringComparison.Ordinal))
				{
					throw new InvalidDataException(
						$"[FONT] TMP field-name mismatch at dump line " + $"{dump.LineNumber}: " +
						$"dumpField='{dump.FieldName}', " + $"targetField='{target.FieldName}', " +
						$"dumpPath='{dump.Path}', " + $"targetPath='{target.Path}'.");
				}

				matches.Add(new DumpTargetMatch { Dump = dump, Target = target });
			}

			return matches;
		}

		// ============================================================
		// PROTECTED TOP LEVEL FIELD SNAPSHOT
		// ============================================================

		private static Dictionary<string, byte[]> CaptureProtectedTmpFontFields(
			AssetTypeValueField baseField)
		{
			var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);

			if (baseField?.Children == null)
				return result;

			foreach (AssetTypeValueField child in baseField.Children)
			{
				if (child == null || child.IsDummy)
				{
					continue;
				}

				string name = child.TemplateField?.Name ?? child.FieldName ?? "";

				if (string.IsNullOrEmpty(name))
					continue;

				if (string.Equals(name, "m_fontInfo", StringComparison.Ordinal) ||
					string.Equals(name, "m_glyphInfoList", StringComparison.Ordinal))
				{
					continue;
				}

				byte[] bytes = child.WriteToByteArray();

				if (bytes == null)
				{
					throw new InvalidDataException($"[FONT] Could not snapshot protected " +
												   $"field '{name}'.");
				}

				result[name] = bytes;
			}

			DebugStr($"[FONT] Protected TMP top-level fields snapshotted: " + $"{result.Count}");

			return result;
		}

		// ============================================================
		// PROTECTED FIELD VALIDATION
		// ============================================================

		private static void ValidateProtectedTmpFontFields(AssetTypeValueField baseField,
														   Dictionary<string, byte[]> snapshot)
		{
			if (snapshot == null)
				throw new ArgumentNullException(nameof(snapshot));

			if (baseField?.Children == null)
			{
				throw new InvalidDataException("[FONT] Cannot validate protected fields: " +
											   "BaseField has no children.");
			}

			foreach (KeyValuePair<string, byte[]> item in snapshot)
			{
				string fieldName = item.Key;

				AssetTypeValueField field = baseField[fieldName];

				if (field == null || field.IsDummy)
				{
					throw new InvalidDataException($"[FONT] Protected field '{fieldName}' " +
												   "disappeared during TMP import.");
				}

				byte[] after = field.WriteToByteArray();

				if (!ByteArraysEqual(item.Value, after))
				{
					throw new InvalidDataException($"[FONT] Protected field '{fieldName}' " +
												   "was modified unexpectedly.");
				}
			}

			DebugStr("[CHECK] TMP protected fields remained byte-identical.");
		}

		// ============================================================
		// PPtr DEBUG
		// ============================================================

		private static void LogTmpFontPPtr(string label, AssetTypeValueField baseField,
										   string fieldName)
		{
			try
			{
				AssetTypeValueField pptr = baseField?[fieldName];

				if (pptr == null || pptr.IsDummy)
				{
					DebugStr($"[FONT] {label}: <missing>");
					return;
				}

				long fileId = ReadPPtrInteger(pptr["m_FileID"]);

				long pathId = ReadPPtrInteger(pptr["m_PathID"]);

				DebugStr($"[FONT] {label}: " + $"FileID={fileId}, PathID={pathId}");
			}
			catch (Exception ex)
			{
				DebugStr($"[FONT] {label}: unable to read PPtr: " + $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static long ReadPPtrInteger(AssetTypeValueField field)
		{
			if (field == null || field.IsDummy || field.Value == null)
			{
				return 0;
			}

			switch (field.Value.ValueType)
			{
				case AssetValueType.Int64:
					return field.AsLong;

				case AssetValueType.UInt64:
					return checked((long)field.AsULong);

				case AssetValueType.UInt32:
					return field.AsUInt;

				default:
					return field.AsInt;
			}
		}

		// ============================================================
		// BYTE ARRAY COMPARISON
		// ============================================================

		private static bool ByteArraysEqual(byte[] a, byte[] b)
		{
			if (ReferenceEquals(a, b))
				return true;

			if (a == null || b == null)
			{
				return false;
			}

			if (a.Length != b.Length)
				return false;

			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
					return false;
			}

			return true;
		}

		// ============================================================
		// TYPEID 128 FALLBACK
		// ============================================================

		private static bool ImportUnityFontType128Internal(
			string inputFile, AssetsManager am, AssetFileInfo afie, AssetsFileInstance assetInst,
			string assetName, bool checkedMode, out AssetTypeValueField modifiedBaseField,
			out byte[] replacementData, out byte[] originalSerializedData)
		{
			modifiedBaseField = null;
			replacementData = Array.Empty<byte>();
			originalSerializedData = Array.Empty<byte>();

			DebugStr($"[FONT] Importing Unity Font TypeID=128 " +
					 $"PID={afie.PathId}, asset='{assetName}', " + $"checked={checkedMode}");

			AssetTypeValueField baseField = am.GetBaseField(assetInst, afie);

			if (baseField == null || baseField.IsDummy)
			{
				throw new InvalidDataException(
					"AssetsTools.NET returned a null/dummy BaseField for Unity Font.");
			}

			originalSerializedData = baseField.WriteToByteArray();

			List<DumpScalar> dumpScalars = ReadDumpScalars(inputFile);

			DebugStr($"[FONT] TypeID=128 dump scalar count=" + $"{dumpScalars.Count}");

			/*
			 * Preserve the existing TypeID=128 behavior.
			 */
			SynchronizeFontDumpArrayStructure(inputFile, baseField);

			List<DumpTargetMatch> matches = BuildFontDumpTargetMatches(inputFile, baseField);

			foreach (DumpTargetMatch match in matches)
			{
				if (match == null || match.Dump == null || match.Target == null ||
					match.Target.Field == null)
				{
					throw new InvalidDataException("Unity Font mapping produced a null match.");
				}

				ApplyDumpValue(match.Target.Field, match.Dump);
			}

			if (checkedMode)
			{
				ValidateFontDumpAgainstBaseField(inputFile, baseField);
			}

			replacementData = baseField.WriteToByteArray();

			if (replacementData == null || replacementData.Length == 0)
			{
				throw new InvalidDataException("Modified Unity Font serialized to zero bytes.");
			}

			modifiedBaseField = baseField;

			return true;
		}
	}
}