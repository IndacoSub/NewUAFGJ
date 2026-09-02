using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace UAFGJ
{
	partial class Program
	{
		// ============================================================
		// PATH ID
		// ============================================================

		private static bool TryParsePathId(
			string specificPathId,
			out long pathId)
		{
			if (string.IsNullOrWhiteSpace(
				specificPathId))
			{
				pathId = 0;
				return false;
			}

			return long.TryParse(
				specificPathId,
				out pathId);
		}


		// ============================================================
		// ASSET NAME
		// ============================================================

		private static string GetAssetName(
			AssetsTools.NET.AssetTypeValueField field)
		{
			try
			{
				if (field == null ||
					field.IsDummy)
				{
					return "";
				}

				var nameField =
					field["m_Name"];

				if (nameField == null ||
					nameField.IsDummy)
				{
					return "";
				}

				return nameField.AsString ?? "";
			}
			catch
			{
				return "";
			}
		}


		// ============================================================
		// TEXTASSET IMPORT
		// ============================================================

		private static bool ImportTextAssetRaw(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField,
			AssetFileInfo afie,
			string fileKind,
			out byte[] originalSerializedData,
			out byte[] replacementData)
		{
			originalSerializedData =
				Array.Empty<byte>();

			replacementData =
				Array.Empty<byte>();

			if (baseField == null ||
				baseField.IsDummy)
			{
				DebugStr(
					"[TXT] TextAsset BaseField is null/dummy.");

				return false;
			}

			try
			{
				originalSerializedData =
					baseField.WriteToByteArray();
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not serialize original TextAsset " +
					$"PID={afie.PathId}: {ex}");

				return false;
			}

			DebugStr(
				$"[TXT] Original TextAsset serialized size=" +
				$"{originalSerializedData.Length} " +
				$"SHA256={Sha256Hex(originalSerializedData)}");

			AssetsTools.NET.AssetTypeValueField nameField;
			AssetsTools.NET.AssetTypeValueField scriptField;

			try
			{
				nameField =
					baseField["m_Name"];

				scriptField =
					baseField["m_Script"];
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not access TextAsset fields " +
					$"for PID={afie.PathId}: {ex}");

				return false;
			}

			if (nameField == null ||
				nameField.IsDummy ||
				scriptField == null ||
				scriptField.IsDummy)
			{
				DebugStr(
					$"[TXT] TextAsset PID={afie.PathId} " +
					"does not have valid m_Name/m_Script fields.");

				return false;
			}

			string text;

			try
			{
				text =
					File.ReadAllText(
						inputFile,
						new UTF8Encoding(
							encoderShouldEmitUTF8Identifier: false,
							throwOnInvalidBytes: false));
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not read TextAsset source " +
					$"'{inputFile}': {ex}");

				return false;
			}

			if (text.Length > 0 &&
				text[0] == '\uFEFF')
			{
				text =
					text.Substring(1);
			}

			bool looksLikeExportDump =
				text.StartsWith(
					"0 TextAsset Base",
					StringComparison.Ordinal);

			if (looksLikeExportDump)
			{
				DebugStr(
					"[TXT] Detected UABEA TextAsset Export Dump.");

				string[] lines =
					text.Replace(
						"\r\n",
						"\n")
						.Replace(
							"\r",
							"\n")
						.Split('\n');

				string dumpedName =
					null;

				string dumpedScript =
					null;

				foreach (string rawLine in lines)
				{
					string line =
						rawLine;

					const string namePrefix =
						" 1 string m_Name = ";

					if (line.StartsWith(
						namePrefix,
						StringComparison.Ordinal))
					{
						string value =
							line.Substring(
								namePrefix.Length)
								.Trim();

						dumpedName =
							ParseDumpString(
								value);

						continue;
					}

					const string scriptPrefix =
						" 1 string m_Script = ";

					if (line.StartsWith(
						scriptPrefix,
						StringComparison.Ordinal))
					{
						string value =
							line.Substring(
								scriptPrefix.Length)
								.Trim();

						dumpedScript =
							ParseDumpString(
								value);

						continue;
					}
				}

				if (dumpedScript == null)
				{
					DisplayStr(
						"[TXT] File looks like a UABEA TextAsset Export Dump " +
						"but m_Script could not be parsed.");

					return false;
				}

				if (dumpedName != null)
				{
					try
					{
						nameField.AsString =
							dumpedName;

						DebugStr(
							$"[TXT] Imported m_Name from dump: " +
							$"'{dumpedName}'");
					}
					catch (Exception ex)
					{
						DebugStr(
							$"[TXT] Failed assigning m_Name from dump: {ex}");

						return false;
					}
				}

				text =
					dumpedScript;

				DebugStr(
					$"[TXT] Extracted m_Script from UABEA dump. " +
					$"Length={text.Length}");
			}
			else
			{
				DebugStr(
					"[TXT] Input is raw TextAsset text; " +
					"no dump wrapper detected.");
			}

			DebugStr(
				$"[TXT] Original TextAsset m_Script length=" +
				$"{scriptField.AsString?.Length ?? 0}");

			DebugStr(
				$"[TXT] New TextAsset m_Script length=" +
				$"{text.Length}");

			try
			{
				scriptField.AsString =
					text;
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Failed assigning TextAsset.m_Script " +
					$"for PID={afie.PathId}: {ex}");

				return false;
			}

			try
			{
				replacementData =
					baseField.WriteToByteArray();
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not serialize modified TextAsset " +
					$"PID={afie.PathId}: {ex}");

				return false;
			}

			if (replacementData.Length == 0)
			{
				DebugStr(
					$"[TXT] Modified TextAsset PID={afie.PathId} " +
					"serialized to zero bytes.");

				return false;
			}

			DebugStr(
				$"[TXT] Modified TextAsset serialized size=" +
				$"{replacementData.Length} " +
				$"SHA256={Sha256Hex(replacementData)}");

			return true;
		}


		// ============================================================
		// RECTTRANSFORM IMPORT
		// ============================================================

		private static bool ImportRectTransform(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField,
			AssetFileInfo afie,
			string fileKind,
			out byte[] originalSerializedData,
			out byte[] replacementData)
		{
			originalSerializedData =
				Array.Empty<byte>();

			replacementData =
				Array.Empty<byte>();

			if (baseField == null ||
				baseField.IsDummy)
			{
				DisplayStr(
					$"[RECTTRANSFORM] PID={afie?.PathId} " +
					"returned a null/dummy BaseField.");

				return false;
			}

			if (!string.Equals(
					fileKind,
					"RECTTRANSFORM_FULL",
					StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(
					fileKind,
					"RECTTRANSFORM_FULL_CHECKED",
					StringComparison.OrdinalIgnoreCase))
			{
				DisplayStr(
					$"[RECTTRANSFORM] Unsupported fileKind '{fileKind}'.");

				return false;
			}

			try
			{
				originalSerializedData =
					baseField.WriteToByteArray();
			}
			catch (Exception ex)
			{
				DisplayStr(
					$"[RECTTRANSFORM] Could not serialize original " +
					$"RectTransform PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());

				return false;
			}

			DebugStr(
				$"[RECTTRANSFORM] Original serialized asset: " +
				$"PID={afie.PathId}, " +
				$"TypeID={afie.TypeId}, " +
				$"bytes={originalSerializedData.Length}, " +
				$"SHA256={Sha256Hex(originalSerializedData)}");

			try
			{
				replacementData =
					ApplyTextDumpToBaseField(
						inputFile,
						baseField);
			}
			catch (Exception ex)
			{
				DisplayStr(
					$"[RECTTRANSFORM] Failed reconstructing " +
					$"RectTransform PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());

				return false;
			}

			if (replacementData == null ||
				replacementData.Length == 0)
			{
				DisplayStr(
					$"[RECTTRANSFORM] Reconstructed RectTransform " +
					$"PID={afie.PathId} has zero serialized bytes.");

				return false;
			}

			DebugStr(
				$"[RECTTRANSFORM] Reconstructed full asset: " +
				$"PID={afie.PathId}, " +
				$"bytes={replacementData.Length}, " +
				$"SHA256={Sha256Hex(replacementData)}");

			return true;
		}


		// ============================================================
		// SPRITE IMPORT
		//
		// TYPEID = 213
		//
		// The ENTIRE Sprite is reconstructed from the dump.
		// ============================================================

		private static bool ImportSprite(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			string fileKind,
			out byte[] originalSerializedData,
			out byte[] replacementData)
		{
			originalSerializedData =
				Array.Empty<byte>();

			replacementData =
				Array.Empty<byte>();

			if (baseField == null ||
				baseField.IsDummy)
			{
				DisplayStr(
					$"[SPRITE] PID={afie?.PathId} " +
					"returned a null/dummy BaseField.");

				return false;
			}

			if (!string.Equals(
					fileKind,
					"SPRITE_FULL",
					StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(
					fileKind,
					"SPRITE_FULL_CHECKED",
					StringComparison.OrdinalIgnoreCase))
			{
				DisplayStr(
					$"[SPRITE] Unsupported fileKind '{fileKind}'.");

				return false;
			}

			try
			{
				originalSerializedData =
					baseField.WriteToByteArray();
			}
			catch (Exception ex)
			{
				DisplayStr(
					$"[SPRITE] Could not serialize original Sprite " +
					$"PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());

				return false;
			}

			DebugStr(
				$"[SPRITE] Original serialized asset: " +
				$"PID={afie.PathId}, " +
				$"TypeID={afie.TypeId}, " +
				$"bytes={originalSerializedData.Length}, " +
				$"SHA256={Sha256Hex(originalSerializedData)}");

			DebugRawVsBaseFieldSprite(
				assetInst,
				afie,
				baseField);

			try
			{
				replacementData =
					ApplyTextDumpToBaseField(
						inputFile,
						baseField);
			}
			catch (Exception ex)
			{
				DisplayStr(
					$"[SPRITE] Failed reconstructing Sprite " +
					$"PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());

				return false;
			}

			if (replacementData == null ||
				replacementData.Length == 0)
			{
				DisplayStr(
					$"[SPRITE] Reconstructed Sprite " +
					$"PID={afie.PathId} has zero serialized bytes.");

				return false;
			}

			DebugStr(
				$"[SPRITE] Reconstructed full asset: " +
				$"PID={afie.PathId}, " +
				$"bytes={replacementData.Length}, " +
				$"SHA256={Sha256Hex(replacementData)}");

			return true;
		}


		// ============================================================
		// MAIN TXT FINDER
		// ============================================================

		private static bool FindTXTFile(
			string inputFile,
			ref AssetsFileInstance assetInst,
			ref AssetFileInfo afie,
			ref AssetsTools.NET.AssetTypeValueField atvf,
			ref AssetsManager am,
			string asset,
			string assetfile_name,
			string specific_pathid,
			string fileKind,
			out byte[] rawReplacementData,
			out byte[] originalSerializedData)
		{
			rawReplacementData =
				Array.Empty<byte>();

			originalSerializedData =
				Array.Empty<byte>();

			if (assetInst == null)
			{
				DebugStr(
					"[TXT] AssetsFileInstance is null.");

				return false;
			}

			if (am == null)
			{
				DebugStr(
					"[TXT] AssetsManager is null.");

				return false;
			}

			if (!File.Exists(inputFile))
			{
				DebugStr(
					$"[TXT] Replacement file does not exist: {inputFile}");

				return false;
			}

			long wantedPathId;

			bool hasWantedPathId =
				TryParsePathId(
					specific_pathid,
					out wantedPathId);

			// ========================================================
			// EXACT PATH ID
			// ========================================================

			if (hasWantedPathId)
			{
				DebugStr(
					$"[TXT] Searching assets in '{assetfile_name}' " +
					$"for exact PID {wantedPathId}");

				AssetFileInfo exactMatch =
					assetInst.file.AssetInfos.FirstOrDefault(
						a =>
							a.PathId ==
							wantedPathId);

				if (exactMatch == null)
				{
					DisplayStr(
						$"[TXT] Could not find any asset " +
						$"with path ID {wantedPathId}.");

					return false;
				}

				afie =
					exactMatch;

				DebugStr(
					$"[TXT] Exact PID found: " +
					$"PID={afie.PathId}, " +
					$"TypeID={afie.TypeId}");

				// ====================================================
				// TEXTASSET - TYPEID 49
				// ====================================================

				if (afie.TypeId == 49)
				{
					AssetsTools.NET.AssetTypeValueField textAssetField;

					try
					{
						textAssetField =
							am.GetBaseField(
								assetInst,
								afie);
					}
					catch (Exception ex)
					{
						DisplayStr(
							$"[TXT] Failed reading TextAsset PID " +
							$"{wantedPathId}: " +
							$"{ex.GetType().Name}: {ex.Message}");

						DebugStr(
							ex.ToString());

						return false;
					}

					if (textAssetField == null ||
						textAssetField.IsDummy)
					{
						DisplayStr(
							$"[TXT] TextAsset PID {wantedPathId} " +
							"returned a null/dummy BaseField.");

						return false;
					}

					atvf =
						textAssetField;

					string textAssetName =
						GetAssetName(
							textAssetField);

					DebugStr(
						$"[TXT] Target is TextAsset: " +
						$"PID={afie.PathId}, " +
						$"Name='{textAssetName}', " +
						$"TypeID={afie.TypeId}");

					return ImportTextAssetRaw(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}


				// ====================================================
				// MONOBEHAVIOUR - TYPEID 114
				// ====================================================

				if (afie.TypeId == 114)
				{
					DebugStr(
						$"[TXT] Target is MonoBehaviour " +
						$"(TypeID=114), PID={afie.PathId}.");

					ushort monoId;

					try
					{
						monoId =
							assetInst.file.GetScriptIndex(
								afie);
					}
					catch (Exception ex)
					{
						DebugStr(
							$"[TXT] Could not read MonoScriptIndex " +
							$"for PID={afie.PathId}: {ex}");

						monoId =
							0;
					}

					DebugStr(
						$"[TXT] MonoScriptIndex={monoId} " +
						$"(0x{monoId:X4}).");

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_TEXT",
						StringComparison.OrdinalIgnoreCase))
					{
						DebugStr(
							"[TXT] Kind=MONOBEHAVIOUR_TEXT. " +
							"Using RAW m_text replacement.");

						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourTextOnly(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_TEXT_CHECKED",
						StringComparison.OrdinalIgnoreCase))
					{
						DebugStr(
							"[TXT] Kind=MONOBEHAVIOUR_TEXT_CHECKED. " +
							"Using RAW m_text replacement.");

						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourTextOnlyChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_FULL",
						StringComparison.OrdinalIgnoreCase) ||
						string.Equals(
							fileKind,
							"MONOBEHAVIOUR_FONT",
							StringComparison.OrdinalIgnoreCase))
					{
						DebugStr(
							$"[TXT] Kind={fileKind}. " +
							"Using FULL unchecked MonoBehaviour import.");

						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourFull(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_FULL_CHECKED",
						StringComparison.OrdinalIgnoreCase) ||
						string.Equals(
							fileKind,
							"MONOBEHAVIOUR_FONT_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DebugStr(
							$"[TXT] Kind={fileKind}. " +
							"Using FULL checked MonoBehaviour import.");

						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourFullChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						DebugStr(
							"[TXT] fileKind empty for MonoBehaviour; " +
							"using MONOBEHAVIOUR_FULL_CHECKED.");

						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourFullChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					DisplayStr(
						$"[TXT] Unsupported MonoBehaviour " +
						$"fileKind '{fileKind}'.");

					return false;
				}


				// ====================================================
				// RECTTRANSFORM - TYPEID 224
				// ====================================================

				if (afie.TypeId == 224)
				{
					DebugStr(
						$"[TXT] Target is RectTransform " +
						$"(TypeID=224), PID={afie.PathId}.");

					AssetsTools.NET.AssetTypeValueField rectField;

					try
					{
						rectField =
							am.GetBaseField(
								assetInst,
								afie);
					}
					catch (Exception ex)
					{
						DisplayStr(
							$"[RECTTRANSFORM] Failed reading PID " +
							$"{wantedPathId}: " +
							$"{ex.GetType().Name}: {ex.Message}");

						DebugStr(
							ex.ToString());

						return false;
					}

					if (rectField == null ||
						rectField.IsDummy)
					{
						DisplayStr(
							$"[RECTTRANSFORM] PID {wantedPathId} " +
							"returned a null/dummy BaseField.");

						return false;
					}

					atvf =
						rectField;

					string rectName =
						GetAssetName(
							rectField);

					DebugStr(
						$"[RECTTRANSFORM] Found RectTransform by exact PID: " +
						$"PID={afie.PathId}, " +
						$"Name='{rectName}', " +
						$"TypeID={afie.TypeId}");

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"RECTTRANSFORM_FULL_CHECKED";

						DebugStr(
							"[RECTTRANSFORM] fileKind empty; " +
							"using RECTTRANSFORM_FULL_CHECKED.");
					}

					if (!string.Equals(
						fileKind,
						"RECTTRANSFORM_FULL",
						StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(
							fileKind,
							"RECTTRANSFORM_FULL_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DisplayStr(
							$"[RECTTRANSFORM] Unsupported fileKind " +
							$"'{fileKind}'. " +
							"Expected RECTTRANSFORM_FULL or " +
							"RECTTRANSFORM_FULL_CHECKED.");

						return false;
					}

					return ImportRectTransform(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}


				// ====================================================
				// SPRITE - TYPEID 213
				// ====================================================

				if (afie.TypeId == 213)
				{
					DebugStr(
						$"[TXT] Target is Sprite " +
						$"(TypeID=213), PID={afie.PathId}.");

					AssetsTools.NET.AssetTypeValueField spriteField;

					try
					{
						spriteField =
							am.GetBaseField(
								assetInst,
								afie);
					}
					catch (Exception ex)
					{
						DisplayStr(
							$"[SPRITE] Failed reading PID " +
							$"{wantedPathId}: " +
							$"{ex.GetType().Name}: {ex.Message}");

						DebugStr(
							ex.ToString());

						return false;
					}

					if (spriteField == null ||
						spriteField.IsDummy)
					{
						DisplayStr(
							$"[SPRITE] PID {wantedPathId} " +
							"returned a null/dummy BaseField.");

						return false;
					}

					atvf =
						spriteField;

					string spriteName =
						GetAssetName(
							spriteField);

					DebugStr(
						$"[SPRITE] Found Sprite by exact PID: " +
						$"PID={afie.PathId}, " +
						$"Name='{spriteName}', " +
						$"TypeID={afie.TypeId}");

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"SPRITE_FULL";

						DebugStr(
							"[SPRITE] fileKind empty; " +
							"using SPRITE_FULL.");
					}

					if (!string.Equals(
						fileKind,
						"SPRITE_FULL",
						StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(
							fileKind,
							"SPRITE_FULL_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DisplayStr(
							$"[SPRITE] Unsupported fileKind " +
							$"'{fileKind}'. " +
							"Expected SPRITE_FULL or " +
							"SPRITE_FULL_CHECKED.");

						return false;
					}

					return ImportSprite(
						inputFile,
						atvf,
						afie,
						assetInst,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}


				// ====================================================
				// UNSUPPORTED TYPE
				// ====================================================

				DisplayStr(
					$"[TXT] Asset PID={afie.PathId} has unsupported " +
					$"TypeID={afie.TypeId}. " +
					"Supported TXT targets are: " +
					"TextAsset (49), " +
					"MonoBehaviour (114), " +
					"RectTransform (224), " +
					"Sprite (213).");

				return false;
			}


			// ========================================================
			// FALLBACK BY NAME
			// ========================================================

			string targetName =
				Path.GetFileNameWithoutExtension(
					inputFile).Trim();

			DebugStr(
				$"[TXT] No valid PathID supplied. " +
				$"Searching supported serialized assets in " +
				$"'{assetfile_name}' by name '{targetName}'.");

			int candidatesScanned =
				0;

			foreach (var inf
				in assetInst.file.AssetInfos.Where(
					a =>
						a.TypeId == 49 ||
						a.TypeId == 114 ||
						a.TypeId == 224 ||
						a.TypeId == 213))
			{
				candidatesScanned++;

				AssetsTools.NET.AssetTypeValueField candidate;

				try
				{
					candidate =
						am.GetBaseField(
							assetInst,
							inf);
				}
				catch (Exception ex)
				{
					DebugStr(
						$"[TXT] Failed reading candidate PID " +
						$"{inf.PathId}: " +
						$"{ex.GetType().Name}: {ex.Message}");

					continue;
				}

				if (candidate == null ||
					candidate.IsDummy)
				{
					continue;
				}

				string name =
					GetAssetName(
						candidate);

				if (!string.Equals(
					name?.Trim(),
					targetName,
					StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				afie =
					inf;

				atvf =
					candidate;

				DebugStr(
					$"[TXT] Found supported serialized asset by name: " +
					$"'{name}', " +
					$"PID={afie.PathId}, " +
					$"TypeID={afie.TypeId}");

				// ====================================================
				// TEXTASSET
				// ====================================================

				if (afie.TypeId == 49)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"TEXTASSET_FULL_CHECKED";
					}

					return ImportTextAssetRaw(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}

				// ====================================================
				// MONOBEHAVIOUR
				// ====================================================

				if (afie.TypeId == 114)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"MONOBEHAVIOUR_FULL_CHECKED";
					}

					if (fileKind == "MONOBEHAVIOUR_TEXT")
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourTextOnly(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (fileKind == "MONOBEHAVIOUR_TEXT_CHECKED")
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourTextOnlyChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (fileKind == "MONOBEHAVIOUR_FULL" ||
						fileKind == "MONOBEHAVIOUR_FONT")
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourFull(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					if (fileKind == "MONOBEHAVIOUR_FULL_CHECKED" ||
						fileKind == "MONOBEHAVIOUR_FONT_CHECKED")
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] monoOriginalData;

						bool success =
							ImportMonoBehaviourFullChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out monoOriginalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							monoOriginalData;

						return true;
					}

					DisplayStr(
						$"[TXT] Unsupported MonoBehaviour " +
						$"fileKind '{fileKind}'.");

					return false;
				}

				// ====================================================
				// RECTTRANSFORM
				// ====================================================

				if (afie.TypeId == 224)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"RECTTRANSFORM_FULL_CHECKED";
					}

					if (!string.Equals(
						fileKind,
						"RECTTRANSFORM_FULL",
						StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(
							fileKind,
							"RECTTRANSFORM_FULL_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DisplayStr(
							$"[RECTTRANSFORM] Unsupported fileKind " +
							$"'{fileKind}'.");

						return false;
					}

					return ImportRectTransform(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}

				// ====================================================
				// SPRITE
				// ====================================================

				if (afie.TypeId == 213)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"SPRITE_FULL";

						DebugStr(
							"[SPRITE] fileKind empty; " +
							"using SPRITE_FULL.");
					}

					if (!string.Equals(
						fileKind,
						"SPRITE_FULL",
						StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(
							fileKind,
							"SPRITE_FULL_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DisplayStr(
							$"[SPRITE] Unsupported fileKind " +
							$"'{fileKind}'.");

						return false;
					}

					return ImportSprite(
						inputFile,
						atvf,
						afie,
						assetInst,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}

				return false;
			}

			DisplayStr(
				$"[TXT] Could not find supported serialized asset " +
				$"'{targetName}' in '{assetfile_name}'. " +
				$"Candidates scanned: {candidatesScanned}.");

			return false;
		}


		// ============================================================
		// PNG FINDER
		// ============================================================

		private static bool FindPNGFile(
			string inputFile,
			ref AssetFileInfo afie,
			ref AssetsFileInstance assetInst,
			ref AssetsTools.NET.AssetTypeValueField atvf,
			ref AssetsManager am,
			string asset,
			string assetfile_name,
			string specificPathId,
			string fileKind)
		{
			if (assetInst == null)
			{
				DisplayStr(
					"[PNG] AssetsFileInstance is null.");

				return false;
			}

			if (am == null)
			{
				DisplayStr(
					"[PNG] AssetsManager is null.");

				return false;
			}

			if (!File.Exists(inputFile))
			{
				DisplayStr(
					$"[PNG] Replacement file does not exist: {inputFile}");

				return false;
			}

			string targetName =
				Path.GetFileNameWithoutExtension(
					inputFile).Trim();

			long wantedPathId;

			bool hasWantedPathId =
				TryParsePathId(
					specificPathId,
					out wantedPathId);

			if (hasWantedPathId)
			{
				DebugStr(
					$"[PNG] Searching Texture2D assets in " +
					$"'{assetfile_name}' for exact PID " +
					$"{wantedPathId}");

				AssetFileInfo exactMatch =
					assetInst.file.AssetInfos.FirstOrDefault(
						a =>
							a.PathId ==
							wantedPathId &&
							a.TypeId ==
							(int)AssetClassID.Texture2D);

				if (exactMatch == null)
				{
					DisplayStr(
						$"[PNG] Could not find Texture2D " +
						$"with path ID {wantedPathId}.");

					return false;
				}

				try
				{
					var candidate =
						am.GetBaseField(
							assetInst,
							exactMatch);

					if (candidate == null ||
						candidate.IsDummy)
					{
						DisplayStr(
							$"[PNG] Texture2D PID {wantedPathId} " +
							"returned a null/dummy BaseField.");

						return false;
					}

					afie =
						exactMatch;

					atvf =
						candidate;

					string name =
						GetAssetName(
							candidate);

					DebugStr(
						$"[PNG] Found Texture2D by exact PID: " +
						$"PID={exactMatch.PathId}, " +
						$"Name='{name}', " +
						$"TypeID={exactMatch.TypeId}");

					DebugStr(
						$"[PNG] Importing '{inputFile}' into " +
						$"Texture2D '{name}'.");

					return true;
				}
				catch (Exception ex)
				{
					DisplayStr(
						$"[PNG] Failed reading Texture2D PID " +
						$"{wantedPathId}: " +
						$"{ex.GetType().Name}: {ex.Message}");

					DebugStr(
						ex.ToString());

					return false;
				}
			}

			DebugStr(
				$"[PNG] No valid PathID supplied. " +
				$"Searching Texture2D by name '{targetName}'.");

			int candidatesScanned =
				0;

			foreach (var inf
				in assetInst.file.GetAssetsOfType(
					(int)AssetClassID.Texture2D))
			{
				candidatesScanned++;

				try
				{
					var candidate =
						am.GetBaseField(
							assetInst,
							inf);

					if (candidate == null ||
						candidate.IsDummy)
					{
						continue;
					}

					string name =
						GetAssetName(
							candidate);

					DebugStr(
						$"[PNG] Candidate #{candidatesScanned}: " +
						$"Name='{name}', " +
						$"PID={inf.PathId}");

					if (!string.Equals(
						name?.Trim(),
						targetName,
						StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					afie =
						inf;

					atvf =
						candidate;

					DebugStr(
						$"[PNG] Found Texture2D by name: " +
						$"Name='{name}', " +
						$"PID={inf.PathId}");

					return true;
				}
				catch (Exception ex)
				{
					DebugStr(
						$"[PNG] Failed reading candidate PID " +
						$"{inf.PathId}: " +
						$"{ex.GetType().Name}: {ex.Message}");

					DebugStr(
						ex.ToString());
				}
			}

			DisplayStr(
				$"[PNG] Couldn't find equivalent image for " +
				$"{asset} " +
				$"(Asset: {assetfile_name}, " +
				$"Texture: {targetName}). " +
				$"Texture2D candidates scanned: " +
				$"{candidatesScanned}");

			return false;
		}
	}
}