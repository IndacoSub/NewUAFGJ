using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

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

		private sealed class TargetAssetCandidate
		{
			public int FileId;
			public AssetsFileInstance File;
			public AssetFileInfo Info;
		}

		private static bool TryParseFileId(
	string specificFileId,
	out int fileId)
		{
			if (string.IsNullOrWhiteSpace(specificFileId))
			{
				fileId = 0;
				return false;
			}

			return int.TryParse(
				specificFileId,
				out fileId);
		}

		private static bool AreSameResolvedAsset(
	TargetAssetCandidate a,
	TargetAssetCandidate b)
		{
			if (a == null ||
				b == null ||
				a.File == null ||
				b.File == null ||
				a.Info == null ||
				b.Info == null)
			{
				return false;
			}

			// Stessa AssetsFileInstance + stesso PID + stesso TypeID.
			if (ReferenceEquals(a.File, b.File) &&
				a.Info.PathId == b.Info.PathId &&
				a.Info.TypeId == b.Info.TypeId)
			{
				return true;
			}

			// Fallback: stesso file fisico, stesso PID e stesso TypeID.
			string aName =
				a.File.name ?? "";

			string bName =
				b.File.name ?? "";

			if (string.Equals(
					aName,
					bName,
					StringComparison.OrdinalIgnoreCase) &&
				a.Info.PathId == b.Info.PathId &&
				a.Info.TypeId == b.Info.TypeId)
			{
				return true;
			}

			return false;
		}

		private static List<TargetAssetCandidate> FindPathIdCandidates(
	AssetsManager am,
	AssetsFileInstance relativeTo,
	long pathId,
	int typeId)
		{
			List<TargetAssetCandidate> candidates =
				new List<TargetAssetCandidate>();

			if (relativeTo == null ||
				am == null)
			{
				return candidates;
			}

			// ============================================================
			// LOCAL FILEID 0
			// ============================================================

			AssetFileInfo localInfo =
				relativeTo.file.AssetInfos.FirstOrDefault(
					a =>
						a.PathId == pathId &&
						a.TypeId == typeId);

			if (localInfo != null)
			{
				candidates.Add(
					new TargetAssetCandidate
					{
						FileId = 0,
						File = relativeTo,
						Info = localInfo
					});
			}

			// ============================================================
			// EXTERNAL FILE IDS
			// ============================================================

			for (int fileId = 1;
				 fileId <= relativeTo.file.Metadata.Externals.Count;
				 fileId++)
			{
				try
				{
					var ext =
						am.GetExtAsset(
							relativeTo,
							fileId,
							pathId,
							true);

					if (ext.file == null ||
						ext.info == null)
					{
						continue;
					}

					if (ext.info.TypeId != typeId)
					{
						continue;
					}

					TargetAssetCandidate externalCandidate =
						new TargetAssetCandidate
						{
							FileId = fileId,
							File = ext.file,
							Info = ext.info
						};

					// --------------------------------------------------------
					// IMPORTANTISSIMO:
					//
					// FileID=1 può risolvere allo stesso asset fisico già
					// trovato come FileID=0.
					//
					// In quel caso NON è una seconda candidate.
					// --------------------------------------------------------

					bool duplicate =
						candidates.Any(
							existing =>
								AreSameResolvedAsset(
									existing,
									externalCandidate));

					if (duplicate)
					{
						DebugStr(
							$"[TARGET] Duplicate external reference ignored: " +
							$"FileID={fileId}, " +
							$"PID={pathId}, " +
							$"TypeID={typeId}, " +
							$"File='{ext.file.name}'.");

						continue;
					}

					candidates.Add(
						externalCandidate);
				}
				catch (Exception ex)
				{
					DebugStr(
						$"[TARGET] Failed resolving FileID={fileId}, " +
						$"PID={pathId}: " +
						$"{ex.GetType().Name}: {ex.Message}");
				}
			}

			return candidates;
		}

		private static bool SelectTargetCandidate(
	List<TargetAssetCandidate> candidates,
	string specificFileId,
	long pathId,
	int typeId,
	out AssetsFileInstance targetFile,
	out AssetFileInfo targetInfo,
	out int selectedFileId)
		{
			targetFile = null;
			targetInfo = null;
			selectedFileId = 0;

			if (candidates == null)
			{
				candidates =
					new List<TargetAssetCandidate>();
			}

			// ============================================================
			// DEDUPLICATE SAFETY PASS
			// ============================================================

			List<TargetAssetCandidate> uniqueCandidates =
				new List<TargetAssetCandidate>();

			foreach (TargetAssetCandidate candidate in candidates)
			{
				if (candidate == null ||
					candidate.File == null ||
					candidate.Info == null)
				{
					continue;
				}

				bool duplicate =
					uniqueCandidates.Any(
						existing =>
							AreSameResolvedAsset(
								existing,
								candidate));

				if (duplicate)
				{
					DebugStr(
						$"[TARGET] Duplicate candidate ignored: " +
						$"FileID={candidate.FileId}, " +
						$"PID={candidate.Info.PathId}, " +
						$"TypeID={candidate.Info.TypeId}, " +
						$"File='{candidate.File.name}'.");

					continue;
				}

				uniqueCandidates.Add(
					candidate);
			}

			candidates =
				uniqueCandidates;

			DebugStr(
				$"[TARGET] Candidates for PID={pathId}, " +
				$"TypeID={typeId}: {candidates.Count}");

			foreach (TargetAssetCandidate candidate in candidates)
			{
				DebugStr(
					$"[TARGET] Candidate: " +
					$"FileID={candidate.FileId}, " +
					$"PID={candidate.Info.PathId}, " +
					$"TypeID={candidate.Info.TypeId}, " +
					$"File='{candidate.File.name}'");
			}

			// ============================================================
			// NORMALIZE FILE ID
			// ============================================================

			string normalizedFileId =
				specificFileId?.Trim() ?? "";

			if (normalizedFileId == "-")
			{
				normalizedFileId = "";
			}

			bool hasExplicitFileId =
				!string.IsNullOrWhiteSpace(
					normalizedFileId);

			int requestedFileId = 0;

			if (hasExplicitFileId &&
				!int.TryParse(
					normalizedFileId,
					out requestedFileId))
			{
				DisplayStr(
					$"[TARGET] Invalid FileID '{specificFileId}'. " +
					"Expected an integer or '-'.");

				return false;
			}

			// ============================================================
			// EXPLICIT FILE ID
			// ============================================================

			if (hasExplicitFileId)
			{
				DebugStr(
					$"[TARGET] Explicit FileID requested: " +
					$"{requestedFileId}");

				TargetAssetCandidate selected =
					candidates.FirstOrDefault(
						c =>
							c.FileId == requestedFileId);

				if (selected == null)
				{
					DisplayStr(
						$"[TARGET] FileID={requestedFileId} does not exist " +
						$"for PID={pathId}, TypeID={typeId}.");

					return false;
				}

				targetFile =
					selected.File;

				targetInfo =
					selected.Info;

				selectedFileId =
					selected.FileId;

				DebugStr(
					$"[TARGET] Selected: " +
					$"FileID={selected.FileId}, " +
					$"PID={selected.Info.PathId}, " +
					$"TypeID={selected.Info.TypeId}, " +
					$"File='{selected.File.name}'");

				return true;
			}

			// ============================================================
			// NO FILE ID
			// ============================================================

			if (candidates.Count == 0)
			{
				DisplayStr(
					$"[TARGET] No asset found for " +
					$"PID={pathId}, TypeID={typeId}.");

				return false;
			}

			if (candidates.Count == 1)
			{
				targetFile =
					candidates[0].File;

				targetInfo =
					candidates[0].Info;

				selectedFileId =
					candidates[0].FileId;

				DebugStr(
					$"[TARGET] Unique target selected automatically: " +
					$"FileID={selectedFileId}, " +
					$"PID={pathId}, " +
					$"TypeID={typeId}, " +
					$"File='{targetFile.name}'");

				return true;
			}

			// ============================================================
			// REAL AMBIGUITY
			// ============================================================

			DisplayStr(
				$"[FATAL] AMBIGUOUS TARGET: " +
				$"PID={pathId}, TypeID={typeId} " +
				$"matches {candidates.Count} distinct assets.");

			DisplayStr(
				"[TARGET] FileID is required.");

			foreach (TargetAssetCandidate candidate in candidates)
			{
				DisplayStr(
					$"[TARGET]   FileID={candidate.FileId}, " +
					$"PID={candidate.Info.PathId}, " +
					$"TypeID={candidate.Info.TypeId}, " +
					$"File='{candidate.File.name}'");
			}

			return false;
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

		// ============================================================
		// TEXTASSET IMPORT
		//
		// TYPEID = 49
		//
		// IMPORTANTE:
		// Questo TextAsset contiene byte binari (Lua bytecode).
		// NON usare:
		//     File.ReadAllText()
		//     Encoding.UTF8
		//     AsString
		//
		// Usare esclusivamente:
		//     File.ReadAllBytes()
		//     m_Script.AsByteArray
		//
		// m_Name viene PRESERVATO.
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

			// ------------------------------------------------------------
			// BASIC VALIDATION
			// ------------------------------------------------------------

			if (string.IsNullOrWhiteSpace(inputFile))
			{
				DebugStr(
					"[TXT] TextAsset input path is empty.");

				return false;
			}

			if (!File.Exists(inputFile))
			{
				DebugStr(
					$"[TXT] TextAsset replacement file does not exist: " +
					$"{inputFile}");

				return false;
			}

			if (afie == null)
			{
				DebugStr(
					"[TXT] AssetFileInfo is null.");

				return false;
			}

			if (afie.TypeId != 49)
			{
				DebugStr(
					$"[TXT] ImportTextAssetRaw called for wrong TypeID=" +
					$"{afie.TypeId}. Expected TypeID=49.");

				return false;
			}

			if (baseField == null ||
				baseField.IsDummy)
			{
				DebugStr(
					$"[TXT] TextAsset BaseField is null/dummy. " +
					$"PID={afie.PathId}");

				return false;
			}

			// ------------------------------------------------------------
			// ORIGINAL PAYLOAD
			// ------------------------------------------------------------

			try
			{
				originalSerializedData =
					baseField.WriteToByteArray();
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not serialize original TextAsset " +
					$"PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			if (originalSerializedData == null ||
				originalSerializedData.Length == 0)
			{
				DebugStr(
					$"[TXT] Original TextAsset PID={afie.PathId} " +
					"serialized to zero bytes.");

				return false;
			}

			DebugStr(
				$"[TXT] ORIGINAL TextAsset: " +
				$"PID={afie.PathId}, " +
				$"TypeID={afie.TypeId}, " +
				$"bytes={originalSerializedData.Length}, " +
				$"SHA256={Sha256Hex(originalSerializedData)}");

			// ------------------------------------------------------------
			// ACCESS FIELDS
			// ------------------------------------------------------------

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
					$"for PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			if (nameField == null ||
				nameField.IsDummy)
			{
				DebugStr(
					$"[TXT] TextAsset PID={afie.PathId} has " +
					"no usable m_Name field.");

				return false;
			}

			if (scriptField == null ||
				scriptField.IsDummy)
			{
				DebugStr(
					$"[TXT] TextAsset PID={afie.PathId} has " +
					"no usable m_Script field.");

				return false;
			}

			// ------------------------------------------------------------
			// PRESERVE m_Name
			// ------------------------------------------------------------

			string originalName = "";

			try
			{
				originalName =
					nameField.AsString ?? "";
			}
			catch
			{
				originalName = "";
			}

			DebugStr(
				$"[TXT] Preserving TextAsset m_Name='{originalName}'.");

			// ------------------------------------------------------------
			// READ RAW BINARY INPUT
			// ------------------------------------------------------------

			byte[] inputBytes;

			try
			{
				inputBytes =
					File.ReadAllBytes(inputFile);
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not read binary TextAsset source " +
					$"'{inputFile}': " +
					$"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			if (inputBytes == null ||
				inputBytes.Length == 0)
			{
				DebugStr(
					$"[TXT] TextAsset replacement '{inputFile}' " +
					"contains zero bytes.");

				return false;
			}

			DebugStr(
				$"[TXT] RAW TextAsset source: " +
				$"file='{inputFile}', " +
				$"bytes={inputBytes.Length}, " +
				$"SHA256={Sha256Hex(inputBytes)}");

			// ------------------------------------------------------------
			// CRITICAL:
			//
			// This is a binary TextAsset.
			//
			// DO NOT:
			//     AsString = ...
			//
			// DO:
			//     AsByteArray = ...
			// ------------------------------------------------------------

			try
			{
				scriptField.AsByteArray =
					inputBytes;
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Failed assigning binary TextAsset.m_Script " +
					$"for PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());

				return false;
			}

			// Re-assign original name explicitly.
			//
			// This prevents any future importer change from accidentally
			// deriving m_Name from the replacement filename.
			try
			{
				nameField.AsString =
					originalName;
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Failed restoring original m_Name " +
					$"'{originalName}' for PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			// ------------------------------------------------------------
			// SERIALIZE MODIFIED TEXTASSET
			// ------------------------------------------------------------

			try
			{
				replacementData =
					baseField.WriteToByteArray();
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not serialize modified TextAsset " +
					$"PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());

				return false;
			}

			if (replacementData == null ||
				replacementData.Length == 0)
			{
				DebugStr(
					$"[TXT] Modified TextAsset PID={afie.PathId} " +
					"serialized to zero bytes.");

				return false;
			}

			DebugStr(
				$"[TXT] MODIFIED TextAsset: " +
				$"PID={afie.PathId}, " +
				$"TypeID={afie.TypeId}, " +
				$"bytes={replacementData.Length}, " +
				$"SHA256={Sha256Hex(replacementData)}");

			// ------------------------------------------------------------
			// VERIFY m_Script AFTER ASSIGNMENT
			// ------------------------------------------------------------

			try
			{
				byte[] verifyScript =
					scriptField.AsByteArray;

				if (verifyScript == null)
				{
					DebugStr(
						$"[TXT] Verification failed: " +
						"m_Script.AsByteArray returned null.");

					return false;
				}

				DebugStr(
					$"[TXT] VERIFY m_Script: " +
					$"bytes={verifyScript.Length}, " +
					$"SHA256={Sha256Hex(verifyScript)}");

				if (verifyScript.Length != inputBytes.Length)
				{
					DebugStr(
						$"[FATAL] TextAsset m_Script length mismatch: " +
						$"actual={verifyScript.Length}, " +
						$"expected={inputBytes.Length}");

					return false;
				}

				string actualScriptSha =
					Sha256Hex(verifyScript);

				string expectedScriptSha =
					Sha256Hex(inputBytes);

				if (!string.Equals(
					actualScriptSha,
					expectedScriptSha,
					StringComparison.OrdinalIgnoreCase))
				{
					DebugStr(
						"[FATAL] TextAsset m_Script SHA256 mismatch " +
						"immediately after import.");

					return false;
				}
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[TXT] Could not verify TextAsset m_Script " +
					$"for PID={afie.PathId}: " +
					$"{ex.GetType().Name}: {ex.Message}");

				return false;
			}

			DebugStr(
				$"[TXT] Binary TextAsset import PASSED: " +
				$"PID={afie.PathId}, " +
				$"originalName='{originalName}', " +
				$"replacementBytes={inputBytes.Length}, " +
				$"serializedBytes={replacementData.Length}");

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
	ref string asset,
	ref string assetfile_name,
	string specific_pathid,
	string specific_fileid,
	string fileKind,
	out byte[] rawReplacementData,
	out byte[] originalSerializedData)
		{
			rawReplacementData =
				Array.Empty<byte>();

			originalSerializedData =
				Array.Empty<byte>();

			// ============================================================
			// BASIC VALIDATION
			// ============================================================

			if (assetInst == null)
			{
				DebugStr(
					"[FATAL] AssetsFileInstance is null.");

				return false;
			}

			if (am == null)
			{
				DebugStr(
					"[FATAL] AssetsManager is null.");

				return false;
			}

			if (!File.Exists(inputFile))
			{
				DebugStr(
					$"[FATAL] Replacement file does not exist: {inputFile}");

				return false;
			}

			// ============================================================
			// NORMALIZE FILE ID
			// ============================================================

			string normalizedFileId =
				specific_fileid?.Trim() ?? "";

			if (normalizedFileId == "-")
			{
				normalizedFileId = "";
			}

			bool hasExplicitFileId =
				!string.IsNullOrWhiteSpace(
					normalizedFileId);

			int requestedFileId = 0;

			if (hasExplicitFileId &&
				!int.TryParse(
					normalizedFileId,
					out requestedFileId))
			{
				DisplayStr(
					$"[FATAL] Invalid FileID '{specific_fileid}'. " +
					"Expected an integer or '-'.");

				return false;
			}

			// ============================================================
			// PATH ID
			// ============================================================

			long wantedPathId;

			bool hasWantedPathId =
				TryParsePathId(
					specific_pathid,
					out wantedPathId);

			// ============================================================
			// EXACT PATH ID
			// ============================================================

			if (hasWantedPathId)
			{
				DebugStr(
					$"[TXT] Searching assets in '{assetfile_name}' " +
					$"for exact PID {wantedPathId}, " +
					$"FileID='{normalizedFileId}'.");

				List<TargetAssetCandidate> candidates =
					new List<TargetAssetCandidate>();

				// --------------------------------------------------------
				// LOCAL FILEID 0
				// --------------------------------------------------------

				AssetFileInfo localInfo =
					assetInst.file.AssetInfos.FirstOrDefault(
						a =>
							a.PathId == wantedPathId);

				if (localInfo != null)
				{
					candidates.Add(
						new TargetAssetCandidate
						{
							FileId = 0,
							File = assetInst,
							Info = localInfo
						});
				}

				// --------------------------------------------------------
				// EXTERNAL FILE IDS
				// --------------------------------------------------------

				for (int fileId = 1;
					 fileId <= assetInst.file.Metadata.Externals.Count;
					 fileId++)
				{
					try
					{
						var ext =
							am.GetExtAsset(
								assetInst,
								fileId,
								wantedPathId,
								true);

						if (ext.file == null ||
							ext.info == null)
						{
							continue;
						}

						TargetAssetCandidate externalCandidate =
							new TargetAssetCandidate
							{
								FileId = fileId,
								File = ext.file,
								Info = ext.info
							};

						// ----------------------------------------------------
						// IGNORE EXTERNAL REFERENCE IF IT RESOLVES TO THE SAME
						// PHYSICAL ASSET ALREADY PRESENT AS FILEID=0.
						// ----------------------------------------------------

						bool duplicate =
							candidates.Any(
								existing =>
									AreSameResolvedAsset(
										existing,
										externalCandidate));

						if (duplicate)
						{
							DebugStr(
								$"[TXT] Duplicate resolved reference ignored: " +
								$"FileID={fileId}, " +
								$"PID={wantedPathId}, " +
								$"TypeID={ext.info.TypeId}, " +
								$"File='{ext.file.name}'.");
							continue;
						}

						candidates.Add(
							externalCandidate);
					}
					catch (Exception ex)
					{
						DebugStr(
							$"[FATAL] Failed resolving FileID={fileId}, " +
							$"PID={wantedPathId}: " +
							$"{ex.GetType().Name}: {ex.Message}");
					}
				}

				// --------------------------------------------------------
				// SAFETY DEDUP
				// --------------------------------------------------------

				List<TargetAssetCandidate> uniqueCandidates =
					new List<TargetAssetCandidate>();

				foreach (TargetAssetCandidate candidate in candidates)
				{
					if (candidate == null)
						continue;

					bool duplicate =
						uniqueCandidates.Any(
							existing =>
								AreSameResolvedAsset(
									existing,
									candidate));

					if (duplicate)
						continue;

					uniqueCandidates.Add(
						candidate);
				}

				candidates =
					uniqueCandidates;

				DebugStr(
					$"[TXT] Candidates for PID={wantedPathId}: " +
					$"{candidates.Count}");

				foreach (TargetAssetCandidate candidate in candidates)
				{
					DebugStr(
						$"[TXT] Candidate: " +
						$"FileID={candidate.FileId}, " +
						$"PID={candidate.Info.PathId}, " +
						$"TypeID={candidate.Info.TypeId}, " +
						$"File='{candidate.File.name}'");
				}

				TargetAssetCandidate selectedCandidate = null;

				// ========================================================
				// EXPLICIT FILE ID
				// ========================================================

				if (hasExplicitFileId)
				{
					DebugStr(
						$"[TXT] Explicit FileID requested: " +
						$"{requestedFileId}");

					selectedCandidate =
						candidates.FirstOrDefault(
							c =>
								c.FileId == requestedFileId);

					if (selectedCandidate == null)
					{
						DisplayStr(
							$"[TXT] FileID={requestedFileId} not found " +
							$"for PID={wantedPathId}.");

						return false;
					}
				}

				// ========================================================
				// NO FILE ID
				// ========================================================

				else
				{
					if (candidates.Count == 0)
					{
						DisplayStr(
							$"[TXT] Could not find any asset " +
							$"with path ID {wantedPathId}.");

						return false;
					}

					if (candidates.Count == 1)
					{
						selectedCandidate =
							candidates[0];
					}
					else
					{
						// ----------------------------------------------------
						// Prefer FileID=0 ONLY when there is no distinct
						// external asset of the same TypeID.
						// ----------------------------------------------------

						TargetAssetCandidate localCandidate =
							candidates.FirstOrDefault(
								c =>
									c.FileId == 0);

						if (localCandidate != null)
						{
							int localTypeId =
								localCandidate.Info.TypeId;

							bool anotherSameType =
								candidates.Any(
									c =>
										c.FileId != 0 &&
										c.Info.TypeId ==
											localTypeId);

							if (!anotherSameType)
							{
								selectedCandidate =
									localCandidate;
							}
						}

						if (selectedCandidate == null)
						{
							DisplayStr(
								$"[FATAL] AMBIGUOUS TARGET: " +
								$"PID={wantedPathId} resolves to " +
								$"{candidates.Count} distinct assets.");

							DisplayStr(
								"[TXT] FileID is required.");

							return false;
						}
					}
				}

				// ========================================================
				// APPLY SELECTED TARGET
				// ========================================================

				assetInst =
					selectedCandidate.File;

				afie =
					selectedCandidate.Info;

				assetfile_name =
					selectedCandidate.File.name;

				DebugStr(
					$"[TXT] Selected target: " +
					$"FileID={selectedCandidate.FileId}, " +
					$"PID={afie.PathId}, " +
					$"TypeID={afie.TypeId}, " +
					$"File='{assetfile_name}'");

				// ========================================================
				// TEXTASSET TYPEID=49
				// ========================================================

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
							$"[TXT] TextAsset PID={wantedPathId} " +
							"returned a null/dummy BaseField.");

						return false;
					}

					atvf =
						textAssetField;

					string textAssetName =
						GetAssetName(
							textAssetField);

					DebugStr(
						$"[TXT] Target is binary TextAsset: " +
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

				// ========================================================
				// GAMEOBJECT TYPEID=1
				// ========================================================

				if (afie.TypeId == 1)
				{
					if (!string.Equals(
							fileKind,
							"GAMEOBJECT_FULL",
							StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(
							fileKind,
							"GAMEOBJECT_FULL_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DisplayStr(
							$"[GAMEOBJECT] Unsupported fileKind " +
							$"'{fileKind}'.");

						return false;
					}

					AssetsTools.NET.AssetTypeValueField modifiedBaseField;

					byte[] originalData;

					bool success;

					if (string.Equals(
						fileKind,
						"GAMEOBJECT_FULL",
						StringComparison.OrdinalIgnoreCase))
					{
						success =
							ImportGameObjectFull(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);
					}
					else
					{
						success =
							ImportGameObjectFullChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);
					}

					if (!success)
						return false;

					atvf =
						modifiedBaseField;

					originalSerializedData =
						originalData;

					return true;
				}

				// ========================================================
				// MONOBEHAVIOUR TYPEID=114
				// ========================================================

				if (afie.TypeId == 114)
				{
					ushort monoId;

					try
					{
						monoId =
							assetInst.file.GetScriptIndex(
								afie);
					}
					catch
					{
						monoId = 0;
					}

					DebugStr(
						$"[TXT] MonoScriptIndex={monoId} " +
						$"(0x{monoId:X4}).");

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"MONOBEHAVIOUR_FULL_CHECKED";
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_TEXT",
						StringComparison.OrdinalIgnoreCase))
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourTextOnly(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

						return true;
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_TEXT_CHECKED",
						StringComparison.OrdinalIgnoreCase))
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourTextOnlyChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

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
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourFull(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

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
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourFullChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

						return true;
					}

					DisplayStr(
						$"[TXT] Unsupported MonoBehaviour fileKind " +
						$"'{fileKind}'.");

					return false;
				}

				// ========================================================
				// FONT TYPEID=128
				// ========================================================

				if (afie.TypeId == 128)
				{
					AssetsTools.NET.AssetTypeValueField fontField;

					try
					{
						fontField =
							am.GetBaseField(
								assetInst,
								afie);
					}
					catch (Exception ex)
					{
						DisplayStr(
							$"[FONT] Failed reading Unity Font PID " +
							$"{wantedPathId}: " +
							$"{ex.GetType().Name}: {ex.Message}");

						return false;
					}

					if (fontField == null ||
						fontField.IsDummy)
					{
						DisplayStr(
							$"[FONT] Unity Font PID={wantedPathId} " +
							"returned a null/dummy BaseField.");

						return false;
					}

					atvf =
						fontField;

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"FONT_CHECKED";
					}

					if (!string.Equals(
							fileKind,
							"FONT",
							StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(
							fileKind,
							"FONT_CHECKED",
							StringComparison.OrdinalIgnoreCase))
					{
						DisplayStr(
							$"[FONT] Unsupported fileKind " +
							$"'{fileKind}'.");

						return false;
					}

					AssetsTools.NET.AssetTypeValueField modifiedBaseField;
					byte[] originalData;

					bool success;

					if (string.Equals(
						fileKind,
						"FONT",
						StringComparison.OrdinalIgnoreCase))
					{
						success =
							ImportUnityFont(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);
					}
					else
					{
						success =
							ImportUnityFontChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);
					}

					if (!success)
						return false;

					atvf =
						modifiedBaseField;

					originalSerializedData =
						originalData;

					return true;
				}

				// ========================================================
				// RECTTRANSFORM TYPEID=224
				// ========================================================

				if (afie.TypeId == 224)
				{
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

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"RECTTRANSFORM_FULL_CHECKED";
					}

					return ImportRectTransform(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}

				// ========================================================
				// SPRITE TYPEID=213
				// ========================================================

				if (afie.TypeId == 213)
				{
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

					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"SPRITE_FULL";
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

				DisplayStr(
					$"[TXT] Asset PID={afie.PathId} has unsupported " +
					$"TypeID={afie.TypeId}.");

				return false;
			}

			// ============================================================
			// FALLBACK BY NAME
			// ============================================================

			string targetName =
				Path.GetFileNameWithoutExtension(
					inputFile).Trim();

			DebugStr(
				$"[TXT] No valid PathID supplied. " +
				$"Searching supported serialized assets in " +
				$"'{assetfile_name}' by name '{targetName}'.");

			int candidatesScanned = 0;

			foreach (var inf in assetInst.file.AssetInfos.Where(
				a =>
					a.TypeId == 49 ||
					a.TypeId == 114 ||
					a.TypeId == 128 ||
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
				catch
				{
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

				// ========================================================
				// TEXTASSET
				// ========================================================

				if (afie.TypeId == 49)
				{
					return ImportTextAssetRaw(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}

				// ========================================================
				// MONOBEHAVIOUR
				// ========================================================

				if (afie.TypeId == 114)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"MONOBEHAVIOUR_FULL_CHECKED";
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_TEXT",
						StringComparison.OrdinalIgnoreCase))
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourTextOnly(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

						return true;
					}

					if (string.Equals(
						fileKind,
						"MONOBEHAVIOUR_TEXT_CHECKED",
						StringComparison.OrdinalIgnoreCase))
					{
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourTextOnlyChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

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
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourFull(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

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
						AssetsTools.NET.AssetTypeValueField modifiedBaseField;
						byte[] originalData;

						bool success =
							ImportMonoBehaviourFullChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);

						if (!success)
							return false;

						atvf =
							modifiedBaseField;

						originalSerializedData =
							originalData;

						return true;
					}

					return false;
				}

				// ========================================================
				// FONT
				// ========================================================

				if (afie.TypeId == 128)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"FONT_CHECKED";
					}

					AssetsTools.NET.AssetTypeValueField modifiedBaseField;
					byte[] originalData;

					bool success;

					if (string.Equals(
						fileKind,
						"FONT",
						StringComparison.OrdinalIgnoreCase))
					{
						success =
							ImportUnityFont(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);
					}
					else
					{
						success =
							ImportUnityFontChecked(
								inputFile,
								am,
								afie,
								assetInst,
								assetfile_name,
								out modifiedBaseField,
								out rawReplacementData,
								out originalData);
					}

					if (!success)
						return false;

					atvf =
						modifiedBaseField;

					originalSerializedData =
						originalData;

					return true;
				}

				// ========================================================
				// RECTTRANSFORM
				// ========================================================

				if (afie.TypeId == 224)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"RECTTRANSFORM_FULL_CHECKED";
					}

					return ImportRectTransform(
						inputFile,
						atvf,
						afie,
						fileKind,
						out originalSerializedData,
						out rawReplacementData);
				}

				// ========================================================
				// SPRITE
				// ========================================================

				if (afie.TypeId == 213)
				{
					if (string.IsNullOrWhiteSpace(fileKind))
					{
						fileKind =
							"SPRITE_FULL";
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
	ref string asset,
	ref string assetfile_name,
	string specificPathId,
	string specificFileId,
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

			// ============================================================
			// EXACT PATH ID
			// ============================================================

			if (hasWantedPathId)
			{
				DebugStr(
					$"[PNG] Resolving Texture2D target: " +
					$"PID={wantedPathId}, " +
					$"TypeID={(int)AssetClassID.Texture2D}, " +
					$"FileID='{specificFileId}'.");

				List<TargetAssetCandidate> candidates =
					FindPathIdCandidates(
						am,
						assetInst,
						wantedPathId,
						(int)AssetClassID.Texture2D);

				AssetsFileInstance targetFile;
				AssetFileInfo targetInfo;
				int selectedFileId;

				if (!SelectTargetCandidate(
					candidates,
					specificFileId,
					wantedPathId,
					(int)AssetClassID.Texture2D,
					out targetFile,
					out targetInfo,
					out selectedFileId))
				{
					return false;
				}

				AssetsTools.NET.AssetTypeValueField candidate;

				try
				{
					candidate =
						am.GetBaseField(
							targetFile,
							targetInfo,
							AssetReadFlags.ForceFromCldb);
				}
				catch (Exception ex)
				{
					DisplayStr(
						$"[PNG] Failed reading Texture2D PID={wantedPathId}, " +
						$"FileID={selectedFileId}: " +
						$"{ex.GetType().Name}: {ex.Message}");

					DebugStr(
						ex.ToString());

					return false;
				}

				if (candidate == null ||
					candidate.IsDummy)
				{
					DisplayStr(
						$"[PNG] Texture2D PID={wantedPathId}, " +
						$"FileID={selectedFileId} returned a null/dummy BaseField.");

					return false;
				}

				assetInst =
					targetFile;

				afie =
					targetInfo;

				atvf =
					candidate;

				assetfile_name =
					targetFile.name;

				string name =
					GetAssetName(
						candidate);

				Console.WriteLine(
					$"[TEX-BEFORE] " +
					$"file='{targetFile.name}' " +
					$"pathId={targetInfo.PathId} " +
					$"typeId={targetInfo.TypeId} " +
					$"name='{name}'");

				DebugStr(
					$"[PNG] Selected Texture2D: " +
					$"FileID={selectedFileId}, " +
					$"PID={targetInfo.PathId}, " +
					$"Name='{name}', " +
					$"TypeID={targetInfo.TypeId}, " +
					$"File='{targetFile.name}'.");

				DebugStr(
					$"[PNG] Importing '{inputFile}' into " +
					$"Texture2D '{name}'.");

				return true;
			}

			// ============================================================
			// FALLBACK BY NAME
			// ============================================================

			DebugStr(
				$"[PNG] No valid PathID supplied. " +
				$"Searching Texture2D by name '{targetName}'.");

			int candidatesScanned = 0;

			foreach (var inf in assetInst.file.GetAssetsOfType(
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

					assetfile_name =
						assetInst.name;

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