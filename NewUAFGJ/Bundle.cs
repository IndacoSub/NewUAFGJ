using AssetsTools.NET.Extra;
using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Buffers.Binary;
using System.Globalization;

namespace UAFGJ
{
	partial class Program
	{
		// ============================================================
		// SNAPSHOT DATA
		// ============================================================

		private sealed class AssetFingerprint
		{
			public long PathId;
			public int TypeId;
			public long ByteSize;
			public ushort MonoScriptIndex;
			public string Name = "";
			public string SerializedSha256 = "";
		}

		private sealed class AssetsFileSnapshot
		{
			public string Name = "";
			public string Sha256 = "";
			public long SerializedLength;
			public List<AssetFingerprint> Assets = new List<AssetFingerprint>();
		}

		// ============================================================
		// FILE KIND NORMALIZATION
		// ============================================================

		private static string ResolveFileKindForTarget(string requestedKind, int typeId)
		{
			// ============================================================
			// EXPLICIT KIND
			// ============================================================

			if (!string.IsNullOrWhiteSpace(requestedKind))
			{
				return requestedKind.Trim();
			}

			// ============================================================
			// AUTOMATIC TXT / SERIALIZED ASSET MODE
			// ============================================================

			switch (typeId)
			{
				case 1:
					return "GAMEOBJECT_FULL_CHECKED";

				case 21:
					return "MATERIAL_FULL_CHECKED";

				case 49:
					return "TEXTASSET_FULL_CHECKED";

				case 114:
					return "MONOBEHAVIOUR_FULL_CHECKED";

				case 128:
					return "FONT_CHECKED";

				case 224:
					return "RECTTRANSFORM_FULL_CHECKED";

				case 213:
					return "SPRITE_FULL";

				default:
					throw new InvalidDataException($"[FATAL] No automatic TXT fileKind is defined " +
												   $"for TypeID={typeId}.");
			}
		}

		private static bool IsPngReplacement(string inputFile)
		{
			return !string.IsNullOrWhiteSpace(inputFile) &&
				   string.Equals(Path.GetExtension(inputFile), ".png",
								 StringComparison.OrdinalIgnoreCase);
		}

		private static string ResolveEffectiveFileKind(string requestedKind, int targetTypeId,
													   string inputFile)
		{
			// ============================================================
			// PNG / TEXTURE2D
			// ============================================================

			if (IsPngReplacement(inputFile))
			{
				DebugStr($"[CHECK] PNG replacement detected for " + $"TypeID={targetTypeId}; " +
						 "skipping TXT fileKind resolution.");

				if (targetTypeId != 28)
				{
					throw new InvalidDataException($"[FATAL] PNG replacement requires Texture2D " +
												   $"TypeID=28, but target TypeID={targetTypeId}.");
				}

				return "PNG";
			}

			string normalizedRequestedKind = requestedKind?.Trim() ?? "";

			// ============================================================
			// TMP_FONT AS MONOBEHAVIOUR
			//
			// These assets are TypeID=114, NOT TypeID=128.
			//
			// When the caller leaves fileKind empty, inspect the dump.
			// If it is a TMP_FontAsset dump, select the Font-specific
			// partial validation path.
			//
			// Also normalize a generic MONOBEHAVIOUR_FULL(_CHECKED)
			// request when the actual dump is unmistakably a Font dump.
			// ============================================================

			if (targetTypeId == 114 && LooksLikeFontDump(inputFile))
			{
				if (string.IsNullOrWhiteSpace(normalizedRequestedKind))
				{
					DebugStr("[CHECK] TypeID=114 dump recognized as TMP_FontAsset. " +
							 "Using MONOBEHAVIOUR_FONT_CHECKED automatically.");

					return "MONOBEHAVIOUR_FONT_CHECKED";
				}

				if (string.Equals(normalizedRequestedKind, "MONOBEHAVIOUR_FULL",
								  StringComparison.OrdinalIgnoreCase))
				{
					DebugStr("[CHECK] TypeID=114 TMP_FontAsset detected. " +
							 "Normalizing MONOBEHAVIOUR_FULL -> MONOBEHAVIOUR_FONT.");

					return "MONOBEHAVIOUR_FONT";
				}

				if (string.Equals(normalizedRequestedKind, "MONOBEHAVIOUR_FULL_CHECKED",
								  StringComparison.OrdinalIgnoreCase))
				{
					DebugStr("[CHECK] TypeID=114 TMP_FontAsset detected. " +
							 "Normalizing MONOBEHAVIOUR_FULL_CHECKED " + "-> MONOBEHAVIOUR_FONT_CHECKED.");

					return "MONOBEHAVIOUR_FONT_CHECKED";
				}

				if (string.Equals(normalizedRequestedKind, "MONOBEHAVIOUR_FONT",
								  StringComparison.OrdinalIgnoreCase) ||
					string.Equals(normalizedRequestedKind, "MONOBEHAVIOUR_FONT_CHECKED",
								  StringComparison.OrdinalIgnoreCase))
				{
					return normalizedRequestedKind;
				}
			}

			// ============================================================
			// EXPLICIT FILE KIND
			// ============================================================

			if (!string.IsNullOrWhiteSpace(normalizedRequestedKind))
			{
				return normalizedRequestedKind;
			}

			// ============================================================
			// STANDARD AUTOMATIC RESOLUTION
			// ============================================================

			return ResolveFileKindForTarget(normalizedRequestedKind, targetTypeId);
		}

		private static bool IsRawMonoBehaviourTextKind(string fileKind)
		{
			return string.Equals(fileKind, "MONOBEHAVIOUR_TEXT", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "MONOBEHAVIOUR_TEXT_CHECKED",
								 StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsRectTransformKind(string fileKind)
		{
			return string.Equals(fileKind, "RECTTRANSFORM_FULL", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "RECTTRANSFORM_FULL_CHECKED",
								 StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsSpriteKind(string fileKind)
		{
			return string.Equals(fileKind, "SPRITE_FULL", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "SPRITE_FULL_CHECKED", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsMonoBehaviourFullKind(string fileKind)
		{
			return string.Equals(fileKind, "MONOBEHAVIOUR_FULL", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "MONOBEHAVIOUR_FULL_CHECKED",
								 StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "MONOBEHAVIOUR_FONT", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "MONOBEHAVIOUR_FONT_CHECKED",
								 StringComparison.OrdinalIgnoreCase);
		}

		// ============================================================
		// GAMEOBJECT FILE KIND
		// ============================================================

		private static bool IsGameObjectFullKind(string fileKind)
		{
			return string.Equals(fileKind, "GAMEOBJECT_FULL", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(fileKind, "GAMEOBJECT_FULL_CHECKED", StringComparison.OrdinalIgnoreCase);
		}

		private sealed class BundleAssetFileCandidate
		{
			public int DirectoryIndex;
			public string Name = "";
			public AssetsFileInstance Instance = null;
		}

		private static List<BundleAssetFileCandidate> LoadAllSerializedAssetFilesFromBundle(
			AssetsManager am, BundleFileInstance bundleInst)
		{
			if (am == null)
				throw new ArgumentNullException(nameof(am));

			if (bundleInst == null || bundleInst.file == null)
				throw new ArgumentNullException(nameof(bundleInst));

			var result = new List<BundleAssetFileCandidate>();

			var directories = bundleInst.file.BlockAndDirInfo.DirectoryInfos;

			DebugStr($"[DISCOVERY] Bundle contains {directories.Count} directory entries.");

			for (int i = 0; i < directories.Count; i++)
			{
				var dir = directories[i];

				bool isSerializedAssets = (dir.Flags & 0x04u) != 0;

				DebugStr($"[DISCOVERY] directory[{i}] " + $"name='{dir.Name}', " +
						 $"flags=0x{dir.Flags:X8}, " + $"serialized={isSerializedAssets}");

				if (!isSerializedAssets)
				{
					DebugStr($"[DISCOVERY] Skipping non-serialized entry '{dir.Name}'.");
					continue;
				}

				int fileIndex = bundleInst.file.GetFileIndex(dir.Name);

				if (fileIndex < 0)
				{
					throw new InvalidDataException($"Serialized bundle entry '{dir.Name}' " +
												   "could not be resolved to a file index.");
				}

				AssetsFileInstance inst = am.LoadAssetsFileFromBundle(bundleInst, fileIndex, true);

				if (inst == null)
				{
					throw new InvalidDataException($"Could not load serialized assets file " +
												   $"'{dir.Name}' from bundle.");
				}

				DebugStr($"[DISCOVERY] Loaded serialized assets file " + $"'{dir.Name}', " +
						 $"assets={inst.file.AssetInfos.Count}, " +
						 $"unity={inst.file.Metadata.UnityVersion}");

				try
				{
					EnsureClassDatabaseIfNeeded(am, inst);
				}
				catch (Exception ex)
				{
					DebugStr($"[DISCOVERY] Class database setup failed for " +
							 $"'{dir.Name}': " + $"{ex.GetType().Name}: {ex.Message}");

					throw;
				}

				result.Add(
					new BundleAssetFileCandidate { DirectoryIndex = i, Name = dir.Name, Instance = inst });
			}

			if (result.Count == 0)
			{
				throw new InvalidDataException("Bundle contains no serialized assets files.");
			}

			DebugStr($"[DISCOVERY] Serialized assets files loaded: {result.Count}");

			return result;
		}

		private static bool FindTargetAcrossBundleAssetsFiles(
			string bundlePath, string inputFile, string specificPathId, string specificFileId,
			string fileKind, AssetsManager am, BundleFileInstance bundleInst,
			List<BundleAssetFileCandidate> candidates, out AssetsFileInstance targetAssetInst,
			out AssetFileInfo targetAfie, out AssetTypeValueField targetAtvf,
			out string targetAssetFileName, out byte[] rawReplacementData,
			out byte[] originalTargetData, out string videoResourceEntryName)
		{
			targetAssetInst = null;
			targetAfie = null;
			targetAtvf = null;
			targetAssetFileName = null;
			rawReplacementData = null;
			originalTargetData = null;
			videoResourceEntryName = null;

			// ============================================================
			// NORMALIZE FILE ID
			// ============================================================

			string normalizedFileId = specificFileId?.Trim() ?? "";

			if (normalizedFileId == "-")
			{
				normalizedFileId = "";
			}

			int matchCount = 0;

			foreach (BundleAssetFileCandidate bundleCandidate in candidates)
			{
				if (bundleCandidate == null || bundleCandidate.Instance == null)
				{
					continue;
				}

				AssetsFileInstance currentAssetInst = bundleCandidate.Instance;

				string currentDirectoryName = bundleCandidate.Name;

				DebugStr($"[DISCOVERY] Searching target in directory[" +
						 $"{bundleCandidate.DirectoryIndex}] " + $"'{currentDirectoryName}'.");

				// ============================================================
				// TEMP STATE
				// ============================================================

				AssetsFileInstance tempAssetInst = currentAssetInst;

				AssetFileInfo tempAfie = null;

				AssetTypeValueField tempAtvf = null;

				AssetsManager tempAm = am;

				string tempAsset = "";

				string tempAssetFileName = currentDirectoryName;

				byte[] tempReplacementData = null;

				byte[] tempOriginalData = null;

				string tempVideoResourceEntryName = null;

				bool found = false;

				string currentKind = fileKind ?? "";

				// ============================================================
				// EXPLICIT PNG
				// ============================================================

				if (string.Equals(currentKind, "PNG", StringComparison.OrdinalIgnoreCase))
				{
					found = FindPNGFile(inputFile, ref tempAfie, ref tempAssetInst, ref tempAtvf, ref tempAm,
										ref tempAsset, ref tempAssetFileName, specificPathId,
										normalizedFileId, currentKind);
				}

				// ============================================================
				// EXPLICIT TXT
				// ============================================================

				else if (string.Equals(currentKind, "TXT", StringComparison.OrdinalIgnoreCase))
				{
					found =
						FindTXTFile(inputFile, ref tempAssetInst, ref tempAfie, ref tempAtvf, ref tempAm,
									ref tempAsset, ref tempAssetFileName, specificPathId, normalizedFileId,
									currentKind, out tempReplacementData, out tempOriginalData);
				}

				// ============================================================
				// AUTOMATIC
				// ============================================================

				else
				{
					// --------------------------------------------------------
					// VIDEOCLIP AS RESOURCE
					// --------------------------------------------------------

					found = FindVideoClipAsResourceFile(
						inputFile, ref tempAssetInst, ref tempAfie, ref tempAtvf, ref tempAm, bundleInst,
						ref tempAssetFileName, specificPathId, normalizedFileId, currentKind,
						out tempReplacementData, out tempOriginalData, out tempVideoResourceEntryName);

					// --------------------------------------------------------
					// PNG
					// --------------------------------------------------------

					if (!found)
					{
						tempAssetInst = currentAssetInst;

						tempAfie = null;

						tempAtvf = null;

						tempAsset = "";

						tempAssetFileName = currentDirectoryName;

						found = FindPNGFile(inputFile, ref tempAfie, ref tempAssetInst, ref tempAtvf,
											ref tempAm, ref tempAsset, ref tempAssetFileName, specificPathId,
											normalizedFileId, currentKind);
					}

					// --------------------------------------------------------
					// TXT
					// --------------------------------------------------------

					if (!found)
					{
						tempAssetInst = currentAssetInst;

						tempAfie = null;

						tempAtvf = null;

						tempAsset = "";

						tempAssetFileName = currentDirectoryName;

						found =
							FindTXTFile(inputFile, ref tempAssetInst, ref tempAfie, ref tempAtvf, ref tempAm,
										ref tempAsset, ref tempAssetFileName, specificPathId, normalizedFileId,
										currentKind, out tempReplacementData, out tempOriginalData);
					}
				}

				// ============================================================
				// NO MATCH
				// ============================================================

				if (!found || tempAssetInst == null || tempAfie == null)
				{
					continue;
				}

				// ============================================================
				// VERIFY RESOLVED ASSETS FILE IS IN THIS BUNDLE
				// ============================================================

				bool assetFileBelongsToBundle = false;

				foreach (BundleAssetFileCandidate candidate in candidates)
				{
					if (candidate == null)
						continue;

					if (ReferenceEquals(candidate.Instance, tempAssetInst))
					{
						assetFileBelongsToBundle = true;

						break;
					}

					if (!string.IsNullOrWhiteSpace(candidate.Name) &&
						!string.IsNullOrWhiteSpace(tempAssetFileName) &&
						string.Equals(candidate.Name, tempAssetFileName,
									  StringComparison.OrdinalIgnoreCase))
					{
						assetFileBelongsToBundle = true;

						break;
					}
				}

				if (!assetFileBelongsToBundle)
				{
					DebugStr($"[DISCOVERY] Ignoring resolved asset outside current bundle: " +
							 $"file='{tempAssetFileName}', " + $"PID={tempAfie.PathId}, " +
							 $"TypeID={tempAfie.TypeId}");

					continue;
				}

				// ============================================================
				// DEDUP SAME UNDERLYING ASSET
				// ============================================================

				if (targetAssetInst != null && targetAfie != null)
				{
					bool sameInstance = ReferenceEquals(tempAssetInst, targetAssetInst) &&
										tempAfie.PathId == targetAfie.PathId &&
										tempAfie.TypeId == targetAfie.TypeId;

					bool sameFile = !string.IsNullOrWhiteSpace(tempAssetFileName) &&
									!string.IsNullOrWhiteSpace(targetAssetFileName) &&
									string.Equals(tempAssetFileName, targetAssetFileName,
												  StringComparison.OrdinalIgnoreCase) &&
									tempAfie.PathId == targetAfie.PathId &&
									tempAfie.TypeId == targetAfie.TypeId;

					if (sameInstance || sameFile)
					{
						DebugStr($"[DISCOVERY] TARGET DUPLICATE IGNORED: " +
								 $"same underlying asset already selected. " + $"file='{tempAssetFileName}', " +
								 $"PID={tempAfie.PathId}, " + $"TypeID={tempAfie.TypeId}");

						continue;
					}
				}

				// ============================================================
				// REAL MATCH
				// ============================================================

				matchCount++;

				DebugStr($"[DISCOVERY] TARGET MATCH #{matchCount}: " + $"file='{tempAssetFileName}', " +
						 $"PID={tempAfie.PathId}, " + $"TypeID={tempAfie.TypeId}, " +
						 $"Name='{GetAssetName(tempAtvf)}'");

				DebugStr($"[DISCOVERY] TARGET MATCH DATA: " + $"replacementBytes=" +
						 $"{(tempReplacementData?.Length ?? 0)}, " +
						 $"replacementSHA=" + $"{(tempReplacementData != null
								? Sha256Hex(tempReplacementData)
								: "<null>")}");

				// ============================================================
				// FIRST REAL MATCH
				// ============================================================

				if (matchCount == 1)
				{
					targetAssetInst = tempAssetInst;

					targetAfie = tempAfie;

					targetAtvf = tempAtvf;

					targetAssetFileName = tempAssetFileName;

					rawReplacementData = tempReplacementData;

					originalTargetData = tempOriginalData;

					videoResourceEntryName = tempVideoResourceEntryName;

					if (targetAfie.TypeId == 49)
					{
						DebugStr("[DISCOVERY] Selected binary TextAsset TypeID=49.");
					}

					continue;
				}

				// ============================================================
				// REAL AMBIGUITY
				// ============================================================

				throw new InvalidDataException(
					$"AMBIGUOUS BUNDLE TARGET: " + $"PID={specificPathId} matches more than one " +
					$"distinct serialized asset. " + $"Use FileID or otherwise disambiguate the target.");
			}

			// ============================================================
			// NO TARGET
			// ============================================================

			if (matchCount == 0)
			{
				DebugStr($"[DISCOVERY] No bundle target found for " + $"PID={specificPathId}, " +
						 $"FileID='{normalizedFileId}', " + $"kind='{fileKind}'.");

				return false;
			}

			DebugStr(
				$"[DISCOVERY] Bundle target resolved uniquely: " + $"file='{targetAssetFileName}', " +
				$"PID={targetAfie.PathId}, " + $"TypeID={targetAfie.TypeId}.");

			return true;
		}

		// ============================================================
		// HANDLE BUNDLE
		// ============================================================

		private static void HandleBundle(string ab, string input_file, string specific_pathid,
										 string specific_fileid, string fileKind)
		{
			LogPhase($"Bundle start: bundle='{ab}', " + $"input='{input_file}', " +
					 $"pathId='{specific_pathid}', " + $"fileId='{specific_fileid}', " +
					 $"kind='{fileKind}'.");

			string originalBundleSha = Sha256File(ab);

			long originalBundleLength = new FileInfo(ab).Length;

			DebugStr($"[CHECK] INPUT bundle length={originalBundleLength} " +
					 $"SHA256={originalBundleSha}");

			DebugStr($"[CHECK] INPUT replacement SHA256={Sha256File(input_file)} " +
					 $"length={new FileInfo(input_file).Length}");

			string classDataPath = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");

			DebugStr($"[CHECK] AppBase='{AppContext.BaseDirectory}'");

			DebugStr($"[CHECK] CurrentDirectory='{Environment.CurrentDirectory}'");

			DebugStr($"[CHECK] classdata.tpk='{classDataPath}'");

			DebugStr($"[CHECK] classdata.tpk SHA256=" + $"{(File.Exists(classDataPath)
						  ? Sha256File(classDataPath)
						  : "MISSING")}");

			string tempBundlePath = ab + ".uafgj_stage1_" + Guid.NewGuid().ToString("N") + ".tmp";

			string finalTempPath = ab + ".uafgj_stage2_" + Guid.NewGuid().ToString("N") + ".tmp";

			DebugStr($"[TEMP] stage1='{tempBundlePath}'");

			DebugStr($"[TEMP] stage2='{finalTempPath}'");

			CleanupStaleBundleStages(ab);

			DeleteFileIfExists(ab + "_temp");

			DeleteFileIfExists(ab + ".new");

			DeleteFileIfExists(ab + ".uafgj_tmp");

			DebugStr("[TEMP] Stale temporary cleanup completed.");

			AssetsManager am = new AssetsManager();

			RuntimeSetup.Configure(am, ab);

			try
			{
				// ====================================================
				// LOAD BUNDLE
				// ====================================================

				LogPhase("Loading bundle into AssetsManager.");

				BundleFileInstance bundleInst = GetBundleInst(am, ab);

				if (bundleInst == null)
				{
					throw new InvalidDataException("[FATAL] Could not load bundle.");
				}

				List<BundleAssetFileCandidate> bundleAssetFiles =
					LoadAllSerializedAssetFilesFromBundle(am, bundleInst);

				AssetsFileInstance assetInst = null;
				string assetfile_name = null;

				AssetBundleCompressionType originalCompression = bundleInst.originalCompression;

				DebugStr($"[CHECK] ORIGINAL bundle compression={originalCompression}; " +
						 $"current working bundle compression={bundleInst.file.GetCompressionType()}.");

				var originalDirectoryNames =
					bundleInst.file.BlockAndDirInfo.DirectoryInfos.Select(d => d.Name).ToList();

				DebugStr($"[CHECK] INPUT bundle compression=" + $"{originalCompression}; " +
						 $"directory entries={originalDirectoryNames.Count}");

				// ====================================================
				// IMPORT STATE
				// ====================================================

				AssetsTools.NET.AssetTypeValueField atvf = null;

				AssetFileInfo afie = null;

				byte[] rawReplacementData = null;

				byte[] originalTargetData = null;

				bool isVideoClipAsResource = IsVideoClipAsResourceKind(fileKind);

				bool isTextReplacement = !IsPngReplacement(input_file) && !isVideoClipAsResource;

				// ====================================================
				// IMPORT
				// ====================================================

				LogPhase($"Beginning import for kind='{fileKind}', " + $"input='{input_file}', " +
						 $"pathId='{specific_pathid}', " + $"fileId='{specific_fileid}'.");

				// ----------------------------------------------------
				// FIND TARGET ACROSS ALL SERIALIZED BUNDLE ENTRIES
				// ----------------------------------------------------

				string videoResourceEntryName = null;

				bool targetFound = FindTargetAcrossBundleAssetsFiles(
					ab, input_file, specific_pathid, specific_fileid, fileKind, am, bundleInst,
					bundleAssetFiles, out assetInst, out afie, out atvf, out assetfile_name,
					out rawReplacementData, out originalTargetData, out videoResourceEntryName);

				if (!targetFound)
				{
					Environment.ExitCode = 1;

					throw new InvalidDataException(
						$"[FATAL] Target asset was not found in bundle. " + $"PID={specific_pathid}, " +
						$"FileID='{specific_fileid}', " + $"input='{input_file}'.");
				}

				DebugStr($"[DISCOVERY] Selected target: " + $"assetFile='{assetfile_name}', " +
						 $"PID={afie.PathId}, " + $"TypeID={afie.TypeId}");

				// ----------------------------------------------------
				// VIDEOCLIP + RESOURCE
				// ----------------------------------------------------

				if (isVideoClipAsResource)
				{
					DebugStr($"[VIDEO] Prepared resource entry " + $"'{videoResourceEntryName}'.");
				}

				// ----------------------------------------------------
				// PNG / TEXTURE2D
				// ----------------------------------------------------

				else if (IsPngReplacement(input_file))
				{
					if (atvf == null || atvf.IsDummy)
					{
						throw new InvalidDataException("[PNG] Replacement target BaseField " +
													   "is null/dummy.");
					}

					int format = atvf["m_TextureFormat"].AsInt;

					if (!ImportTexturesCustom(ref atvf, input_file, format, fileKind))
					{
						throw new InvalidDataException("[FATAL] Texture import failed.");
					}

					rawReplacementData = atvf.WriteToByteArray();

					originalTargetData = null;

					DebugStr(
						$"[PNG] Imported Texture2D replacement: " + $"bytes={rawReplacementData.Length}, " +
						$"SHA256={Sha256Hex(rawReplacementData)}");
				}

				// ----------------------------------------------------
				// TXT / SERIALIZED ASSET
				// ----------------------------------------------------
				//
				// FindTargetAcrossBundleAssetsFiles() already called
				// FindTXTFile() on the correct AssetsFileInstance.
				//
				// Therefore there is NOTHING ELSE to import here.
				// rawReplacementData and originalTargetData have already
				// been populated by FindTXTFile().
				//
				else
				{
					DebugStr($"[TXT] Replacement prepared by bundle-wide target search: " +
							 $"bytes={rawReplacementData?.Length ?? 0}, " +
							 $"SHA256=" + $"{(rawReplacementData == null
									  ? "<null>"
									  : Sha256Hex(rawReplacementData))}");
				}

				// ====================================================
				// TARGET MUST EXIST
				// ====================================================

				if (afie == null)
				{
					throw new InvalidDataException("[FATAL] Replacement target is missing; " +
												   "refusing to write.");
				}

				// ====================================================
				// BEFORE SNAPSHOT
				// IMPORTANT:
				// This happens AFTER FileID resolution.
				// ====================================================

				LogPhase($"Capturing pre-import assets snapshot " + $"for target file '{assetfile_name}'.");

				AssetsFileSnapshot beforeSnapshot =
					CaptureAssetsFileSnapshot(am, assetInst, assetfile_name);

				DebugStr($"[CHECK] BEFORE assets '{assetfile_name}' " + $"SHA256={beforeSnapshot.Sha256} " +
						 $"serializedLength={beforeSnapshot.SerializedLength} " +
						 $"assets={beforeSnapshot.Assets.Count}");

				// ====================================================
				// RESOLVE EFFECTIVE FILE KIND
				// ====================================================

				string effectiveFileKind = ResolveEffectiveFileKind(fileKind, afie.TypeId, input_file);

				DebugStr($"[CHECK] Effective fileKind='{effectiveFileKind}' " +
						 $"for target TypeID={afie.TypeId}.");

				// ====================================================
				// VALIDATE IMPORT RESULT
				// ====================================================

				bool isPngReplacement =
					string.Equals(effectiveFileKind, "PNG", StringComparison.OrdinalIgnoreCase);

				bool rawMonoTextKind = !isPngReplacement && IsRawMonoBehaviourTextKind(effectiveFileKind);

				if (rawReplacementData == null || rawReplacementData.Length == 0)
				{
					throw new InvalidDataException("[FATAL] Replacement data is missing; " +
												   "refusing to write.");
				}

				if (!rawMonoTextKind && (atvf == null || atvf.IsDummy))
				{
					throw new InvalidDataException("[FATAL] Replacement BaseField is missing for " +
												   $"fileKind='{effectiveFileKind}'; " +
												   "refusing to write.");
				}

				DebugStr($"[CHECK] Replacement target accepted: " + $"PID={afie.PathId}, " +
						 $"TypeID={afie.TypeId}, " + $"kind='{effectiveFileKind}', " +
						 $"raw={rawMonoTextKind}");

				int expectedTargetTypeId = afie.TypeId;

				DebugStr($"[CHECK] Replacement mode=" + $"{(isTextReplacement
								? "TXT"
								: "PNG/GenericAsset")}, " +
						 $"PID={afie.PathId}, " + $"TypeID={expectedTargetTypeId}");

				// ====================================================
				// VERIFY TARGET IN BEFORE SNAPSHOT
				// ====================================================

				AssetFingerprint targetBefore =
					beforeSnapshot.Assets.FirstOrDefault(a => a.PathId == afie.PathId);

				if (targetBefore == null)
				{
					throw new InvalidDataException("[FATAL] Target PathID disappeared " +
												   "from pre-write snapshot.");
				}

				if (targetBefore.TypeId != expectedTargetTypeId)
				{
					throw new InvalidDataException($"[FATAL] Replacement target TypeID changed " +
												   $"before save: " + $"{targetBefore.TypeId}->" +
												   $"{expectedTargetTypeId}");
				}

				DebugStr($"[CHECK] TARGET BEFORE " + $"PID={targetBefore.PathId} " +
						 $"TypeID={targetBefore.TypeId} " + $"ByteSize={targetBefore.ByteSize} " +
						 $"ScriptIndex={targetBefore.MonoScriptIndex} " + $"Name='{targetBefore.Name}' " +
						 $"SHA256={targetBefore.SerializedSha256}");

				DebugStr($"[CHECK] TARGET AFTER " + $"PID={afie.PathId} " + $"TypeID={afie.TypeId} " +
						 $"bytes={rawReplacementData.Length} " + $"SHA256={Sha256Hex(rawReplacementData)}");

				LogPhase("Replacement data accepted; " + "beginning save pipeline.");

				// ====================================================
				// SAVE
				// ====================================================

				if (rawMonoTextKind)
				{
					DebugStr($"[SAVE] Using RAW MonoBehaviour saver " +
							 $"for fileKind='{effectiveFileKind}'.");

					SaveAssetBundleRaw(rawReplacementData, afie, assetInst, bundleInst, assetfile_name,
									   tempBundlePath);
				}
				else if (isVideoClipAsResource)
				{
					SaveAssetBundleVideoClipAsResource(atvf, afie, assetInst, bundleInst, assetfile_name,
													   tempBundlePath, input_file);
				}
				else
				{
					SaveAssetBundle(atvf, afie, assetInst, bundleInst, assetfile_name, tempBundlePath,
									Path.GetFileNameWithoutExtension(input_file));
				}

				// ====================================================
				// RELEASE SOURCE HANDLES
				// ====================================================

				LogPhase("Releasing source bundle handles before final pack.");

				if (!am.UnloadAllAssetsFiles(true))
				{
					DisplayStr("Could not unload all asset files!");
				}

				if (!am.UnloadAllBundleFiles())
				{
					DisplayStr("Could not unload all bundle files!");
				}

				// ====================================================
				// FINAL PACK
				// ====================================================

				LogPhase("Beginning final pack and validation.");

				PackBundlePreservingFormat(ab, assetfile_name, afie.PathId, specific_pathid, input_file,
										   effectiveFileKind, rawReplacementData, beforeSnapshot,
										   originalBundleSha, originalBundleLength, originalCompression,
										   originalDirectoryNames, expectedTargetTypeId, isTextReplacement,
										   tempBundlePath, finalTempPath);

				DisplayStr("Done!");
			}
			catch (Exception ex)
			{
				Environment.ExitCode = 1;

				DisplayStr("[FATAL] Bundle handling failed: " + ex.GetType().Name + ": " + ex.Message);

				DebugStr(ex.ToString());

				Environment.ExitCode = 1;
			}
			finally
			{
				try
				{
					am.UnloadAllAssetsFiles(true);
				}
				catch
				{
				}

				try
				{
					am.UnloadAllBundleFiles();
				}
				catch
				{
				}

				DeleteFileIfExists(tempBundlePath);

				DeleteFileIfExists(finalTempPath);
			}
		}

		// ============================================================
		// SAVE ASSET BUNDLE
		// ============================================================

		private static void SaveAssetBundle(AssetsTools.NET.AssetTypeValueField modifiedBaseField,
											AssetFileInfo afie, AssetsFileInstance assetInst,
											BundleFileInstance bundleInst, string assetfile_name,
											string tempBundle, string input_noext)
		{
			if (modifiedBaseField == null)
			{
				throw new InvalidOperationException("[FATAL] Modified base field is null.");
			}

			if (afie == null)
			{
				throw new InvalidOperationException("[FATAL] Asset info is null.");
			}

			if (assetInst == null)
			{
				throw new InvalidOperationException("[FATAL] Assets file instance is null.");
			}

			if (bundleInst == null)
			{
				throw new InvalidOperationException("[FATAL] Bundle instance is null.");
			}

			// ========================================================
			// SERIALIZE MODIFIED BASEFIELD EXPLICITLY
			// ========================================================

			byte[] replacementData = modifiedBaseField.WriteToByteArray();

			if (replacementData == null || replacementData.Length == 0)
			{
				throw new InvalidDataException(
					"[FATAL] Modified BaseField serialized to an empty payload.");
			}

			DebugStr($"[SAVE] Modified BaseField serialized explicitly: " +
					 $"{replacementData.Length} bytes " + $"SHA256={Sha256Hex(replacementData)}");

			DebugFindFloatPatterns("REPLACEMENT DATA", replacementData);

			DebugPayloadWindow("NEW textureRect", replacementData, 10404);

			DebugPayloadWindow("NEW old-offset", replacementData, 3176);

			// ========================================================
			// WRITE RAW SERIALIZED ASSET DATA
			// ========================================================

			DebugStr($"[SAVE] About to SetNewData: " + $"PID={afie.PathId}, " +
					 $"expectedBytes={replacementData.Length}, " +
					 $"expectedSHA256={Sha256Hex(replacementData)}");

			afie.SetNewData(replacementData);

			if (afie.Replacer == null)
			{
				throw new InvalidDataException($"[FATAL] SetNewData did not create a replacer " +
											   $"for PID={afie.PathId}.");
			}

			DebugStr($"[SAVE] Replacer installed: " + $"PID={afie.PathId}, " +
					 $"ReplacerType={afie.Replacer.GetType().Name}");

			byte[] newAssetData;

			using (var stream = new MemoryStream()) using (var writer = new AssetsFileWriter(stream))
			{
				assetInst.file.Write(writer);

				newAssetData = stream.ToArray();
			}

			DebugFindFloatPatterns("SERIALIZED ASSETS FILE", newAssetData);

			DebugStr($"[SAVE] Inner assets file serialized: " + $"bytes={newAssetData.Length}, " +
					 $"SHA256={Sha256Hex(newAssetData)}");

			DebugStr($"[SAVE] Expected target replacement: " + $"PID={afie.PathId}, " +
					 $"TypeID={afie.TypeId}, " + $"bytes={replacementData.Length}, " +
					 $"SHA256={Sha256Hex(replacementData)}");

			DebugStr($"[SAVE] Inner assets file size=" + $"{newAssetData.Length} " +
					 $"SHA256={Sha256Hex(newAssetData)}");

			// ========================================================
			// UPDATE BUNDLE DIRECTORY ENTRY
			// ========================================================

			int dirIndex = bundleInst.file.GetFileIndex(assetfile_name);

			if (dirIndex < 0)
			{
				throw new InvalidDataException($"[FATAL] Bundle entry not found: {assetfile_name}");
			}

			bundleInst.file.BlockAndDirInfo.DirectoryInfos[dirIndex].SetNewData(newAssetData);

			var dirInfo = bundleInst.file.BlockAndDirInfo.DirectoryInfos[dirIndex];

			DebugStr($"[SAVE] Bundle directory replacement: " + $"name='{dirInfo.Name}', " +
					 $"originalSize={dirInfo.DecompressedSize}, " +
					 $"replacerType={dirInfo.ReplacerType}, " +
					 $"replacerSize={dirInfo.Replacer?.GetSize() ?? -1}");

			// ========================================================
			// WRITE TEMPORARY BUNDLE
			// ========================================================

			DeleteFileIfExists(tempBundle);

			using (var fileStream = new FileStream(
					   tempBundle, FileMode.CreateNew, FileAccess.Write,
					   FileShare.None)) using (var bunWriter = new AssetsFileWriter(fileStream))
			{
				bundleInst.file.Write(bunWriter);
			}

			DebugStr("[SAVE] Stage1 bundle write completed.");

			try
			{
				DebugBundleEntryBytes(bundleInst, assetfile_name, "STAGE1 IN-MEMORY DIRECTORY ENTRY",
									  newAssetData);
			}
			catch (Exception ex)
			{
				DebugStr($"[BUNDLE DEBUG] STAGE1 in-memory verification failed: " +
						 $"{ex.GetType().Name}: {ex.Message}");
			}

			DebugStr($"[SAVE] Temporary bundle written: {tempBundle}");
		}

		// ============================================================
		// SAVE ASSET BUNDLE - RAW
		// ============================================================

		private static void SaveAssetBundleRaw(byte[] replacementData, AssetFileInfo afie,
											   AssetsFileInstance assetInst,
											   BundleFileInstance bundleInst, string assetfile_name,
											   string tempBundle)
		{
			if (replacementData == null || replacementData.Length == 0)
			{
				throw new InvalidDataException("[FATAL] Raw replacement data is empty.");
			}

			if (afie == null || assetInst == null || bundleInst == null)
			{
				throw new InvalidDataException("[FATAL] Asset/bundle state is null.");
			}

			afie.SetNewData(replacementData);

			byte[] newAssetData;

			using (var stream = new MemoryStream()) using (var writer = new AssetsFileWriter(stream))
			{
				assetInst.file.Write(writer);

				newAssetData = stream.ToArray();
			}

			DebugStr($"[SAVE] RAW inner assets file size=" + $"{newAssetData.Length} " +
					 $"SHA256={Sha256Hex(newAssetData)}");

			int dirIndex = bundleInst.file.GetFileIndex(assetfile_name);

			if (dirIndex < 0)
			{
				throw new InvalidDataException($"[FATAL] Bundle entry not found: {assetfile_name}");
			}

			bundleInst.file.BlockAndDirInfo.DirectoryInfos[dirIndex].SetNewData(newAssetData);

			using (var fileStream = new FileStream(
					   tempBundle, FileMode.CreateNew, FileAccess.Write,
					   FileShare.None)) using (var bunWriter = new AssetsFileWriter(fileStream))
			{
				bundleInst.file.Write(bunWriter);
			}

			DebugStr($"[SAVE] Temporary bundle written: {tempBundle}");
		}

		// ============================================================
		// PACK + VALIDATION
		// ============================================================

		private static void PackBundlePreservingFormat(
			string realName, string assetfileName, long targetPathId, string specificPathId,
			string dumpPath, string fileKind, byte[] expectedTargetData,
			AssetsFileSnapshot beforeSnapshot, string originalBundleSha, long originalBundleLength,
			AssetBundleCompressionType originalCompression, List<string> originalDirectoryNames,
			int expectedTargetTypeId, bool isTextReplacement, string fakeName, string finalTemp)
		{
			if (!File.Exists(fakeName))
			{
				throw new FileNotFoundException("[FATAL] Temporary bundle missing.", fakeName);
			}

			DebugStr("[CHECK] ===== PRE-PACK CONTAINER VALIDATION =====");

			ValidateBundleContainer(fakeName);

			AssetsManager am = new AssetsManager();

			DeleteFileIfExists(finalTemp);

			try
			{
				BundleFileInstance bun = am.LoadBundleFile(fakeName);

				if (bun == null)
				{
					throw new InvalidDataException("[FATAL] Could not reopen temporary bundle.");
				}

				DebugStr("[PACK DEBUG] Reopened stage1 bundle.");

				try
				{
					byte[] stage1Entry = ReadBundleDirectoryEntryBytes(bun, assetfileName);

					DebugStr($"[PACK DEBUG] STAGE1 ENTRY RAW: " + $"name='{assetfileName}', " +
							 $"bytes={stage1Entry.Length}, " + $"SHA256={Sha256Hex(stage1Entry)}");

					DebugFindFloatPatterns("STAGE1 ASSETS ENTRY", stage1Entry);
				}
				catch (Exception ex)
				{
					DebugStr($"[PACK DEBUG] Could not read stage1 directory entry: " +
							 $"{ex.GetType().Name}: {ex.Message}");

					DebugStr(ex.ToString());
				}

				using (var stream = new FileStream(
						   finalTemp, FileMode.CreateNew, FileAccess.Write,
						   FileShare.None)) using (var writer = new AssetsFileWriter(stream))
				{
					DebugStr($"[PACK] Packing with original " + $"compression={originalCompression}.");

					DebugStr($"[PACK] Source staging bundle length=" + $"{new FileInfo(fakeName).Length}");

					bun.file.Pack(writer, originalCompression);

					DebugStr($"[PACK] Finished pack write to " + $"'{finalTemp}'.");
				}

				if (!am.UnloadAllBundleFiles())
				{
					DisplayStr("[PACK] Could not unload temporary bundle handles.");
				}

				string finalSha = Sha256File(finalTemp);

				DebugStr($"[CHECK] PACKED staging bundle length=" + $"{new FileInfo(finalTemp).Length} " +
						 $"SHA256={finalSha}");

				DebugStr("[CHECK] ===== FINAL CONTAINER VALIDATION =====");

				ValidateBundleContainer(finalTemp);

				ValidateFinalBundle(finalTemp, assetfileName, targetPathId, dumpPath, fileKind,
									expectedTargetData, beforeSnapshot, originalCompression,
									originalDirectoryNames, expectedTargetTypeId, isTextReplacement);

				if (string.Equals(finalSha, originalBundleSha, StringComparison.OrdinalIgnoreCase))
				{
					DebugStr("[CHECK] Whole-bundle SHA matches input. " +
							 "This is not treated as an import failure; " +
							 "the validated target payload is authoritative.");
				}
				else
				{
					DebugStr("[CHECK] Whole-bundle SHA differs from input. " + "Binary change detected.");
				}

				DebugStr("[CHECK] ===== ALL PRE-REPLACE CHECKS PASSED =====");

				DebugStr($"[CHECK] INPUT  SHA256={originalBundleSha} " + $"length={originalBundleLength}");

				DebugStr($"[CHECK] OUTPUT SHA256={finalSha} " + $"length={new FileInfo(finalTemp).Length}");

				LogFileState("[SAVE] FINAL STAGING BEFORE COMMIT", finalTemp);

				DebugStr($"[SAVE] Committing validated staging file " + $"to '{realName}'.");

				ReplaceFileWithRetry(finalTemp, realName);

				DebugStr("[SAVE] Commit operation returned successfully.");

				DebugStr("[SAVE] Reopening COMMITTED bundle for post-commit verification.");

				VerifyCommittedTargetAfterReopen(realName, assetfileName, targetPathId, expectedTargetData);

				string committedSha = Sha256File(realName);

				DebugStr($"[CHECK] COMMITTED bundle SHA256={committedSha} " +
						 $"length={new FileInfo(realName).Length}");

				if (!string.Equals(committedSha, finalSha, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("[FATAL] Committed bundle SHA256 differs from " +
												   "the validated staging file.");
				}
			}
			finally
			{
				try
				{
					am.UnloadAllAssetsFiles(true);
				}
				catch
				{
				}

				try
				{
					am.UnloadAllBundleFiles();
				}
				catch
				{
				}

				DeleteFileIfExists(fakeName);

				DeleteFileIfExists(finalTemp);
			}
		}

		private static void VerifyCommittedTargetAfterReopen(string bundlePath, string assetfileName,
															 long targetPathId,
															 byte[] expectedTargetData)
		{
			AssetsManager verifyManager = new AssetsManager();

			try
			{
				RuntimeSetup.Configure(verifyManager, bundlePath);

				BundleFileInstance bundle = verifyManager.LoadBundleFile(bundlePath, true);

				if (bundle == null)
				{
					throw new InvalidDataException(
						"[FATAL] Post-commit verification could not reopen bundle.");
				}

				DebugStr($"[POST-COMMIT] Bundle reopened: '{bundlePath}'.");

				int fileIndex = bundle.file.GetFileIndex(assetfileName);

				if (fileIndex < 0)
				{
					throw new InvalidDataException(
						$"[FATAL] Post-commit verification could not find assets file " +
						$"'{assetfileName}'.");
				}

				AssetsFileInstance inst = verifyManager.LoadAssetsFileFromBundle(bundle, fileIndex, true);

				if (inst == null)
				{
					throw new InvalidDataException(
						$"[FATAL] Post-commit verification could not reopen assets file " +
						$"'{assetfileName}'.");
				}

				AssetFileInfo targetInfo =
					inst.file.AssetInfos.FirstOrDefault(a => a.PathId == targetPathId);

				if (targetInfo == null)
				{
					throw new InvalidDataException(
						$"[FATAL] Post-commit verification could not find target PID " + $"{targetPathId}.");
				}

				byte[] rawTargetData = ReadRawAssetBytes(inst, targetInfo);

				DebugStr($"[POST-COMMIT] RAW TARGET: " + $"PID={targetInfo.PathId}, " +
						 $"TypeID={targetInfo.TypeId}, " + $"ByteSize={targetInfo.ByteSize}, " +
						 $"bytes={rawTargetData.Length}, " + $"SHA256={Sha256Hex(rawTargetData)}");

				DebugStr($"[POST-COMMIT] EXPECTED TARGET: " + $"bytes={expectedTargetData.Length}, " +
						 $"SHA256={Sha256Hex(expectedTargetData)}");

				ComparePayloads("[POST-COMMIT] RAW TARGET", rawTargetData, expectedTargetData);

				if (rawTargetData.Length != expectedTargetData.Length)
				{
					throw new InvalidDataException(
						$"[FATAL] Post-commit target length mismatch: " + $"actual={rawTargetData.Length}, " +
						$"expected={expectedTargetData.Length}");
				}

				string actualSha = Sha256Hex(rawTargetData);

				string expectedSha = Sha256Hex(expectedTargetData);

				if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("[FATAL] Post-commit raw target payload does not match " +
												   "the intended replacement data.");
				}

				DebugStr("[POST-COMMIT] VERIFIED: committed file contains " +
						 "the exact intended raw target payload.");

				// ------------------------------------------------------------
				// Optional semantic verification for Sprite only.
				// Raw payload verification above applies to ALL asset types,
				// including TextAsset and Texture2D.
				// ------------------------------------------------------------

				if (targetInfo.TypeId == 213)
				{
					AssetsTools.NET.AssetTypeValueField reopenedField =
						verifyManager.GetBaseField(inst, targetInfo);

					if (reopenedField == null || reopenedField.IsDummy)
					{
						throw new InvalidDataException(
							"[FATAL]  Could not obtain reopened Sprite BaseField post-commit.");
					}

					AssetsTools.NET.AssetTypeValueField reopenedTextureRect =
						reopenedField["m_RD"]["textureRect"];

					AssetsTools.NET.AssetTypeValueField reopenedTextureRectOffset =
						reopenedField["m_RD"]["textureRectOffset"];

					DebugStr("[POST-COMMIT] REOPENED Sprite textureRect: " +
							 $"x={reopenedTextureRect["x"].AsFloat}, " +
							 $"y={reopenedTextureRect["y"].AsFloat}, " +
							 $"width={reopenedTextureRect["width"].AsFloat}, " +
							 $"height={reopenedTextureRect["height"].AsFloat}");

					DebugStr("[POST-COMMIT] REOPENED Sprite textureRectOffset: " +
							 $"x={reopenedTextureRectOffset["x"].AsFloat}, " +
							 $"y={reopenedTextureRectOffset["y"].AsFloat}");
				}
				else
				{
					DebugStr($"[POST-COMMIT] Semantic Sprite verification skipped for " +
							 $"TypeID={targetInfo.TypeId}.");
				}
			}
			finally
			{
				try
				{
					verifyManager.UnloadAllAssetsFiles(true);
				}
				catch
				{
				}

				try
				{
					verifyManager.UnloadAllBundleFiles();
				}
				catch
				{
				}
			}
		}

		// ============================================================
		// CLEANUP
		// ============================================================

		private static void CleanupStaleBundleStages(string bundlePath)
		{
			try
			{
				string directory = Path.GetDirectoryName(bundlePath);

				string fileName = Path.GetFileName(bundlePath);

				if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) ||
					!Directory.Exists(directory))
				{
					return;
				}

				string[] patterns = { fileName + ".uafgj_stage1_*.tmp",

							  fileName + ".uafgj_stage2_*.tmp" };

				foreach (string pattern in patterns)
				{
					foreach (string path in Directory.GetFiles(directory, pattern))
					{
						DeleteFileIfExists(path);
					}
				}
			}
			catch (Exception ex)
			{
				DebugStr("[CLEANUP] Could not scan for stale staging files: " + ex.GetType().Name + ": " +
						 ex.Message);
			}
		}

		private static void DeleteFileIfExists(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return;

			try
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			catch (Exception ex)
			{
				DebugStr($"[CLEANUP] Could not delete temporary file " +
						 $"'{path}': " + $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		// ============================================================
		// FILE REPLACEMENT
		// ============================================================

		private static void ReplaceFileWithRetry(string sourcePath, string destinationPath)
		{
			const int maxAttempts = 10;
			const int initialDelayMs = 250;
			const int maxDelayMs = 2000;

			Exception lastError = null;

			if (!File.Exists(sourcePath))
			{
				throw new FileNotFoundException("[FATAL] Replacement file does not exist.", sourcePath);
			}

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				try
				{
					if (File.Exists(destinationPath))
					{
						try
						{
							File.Replace(sourcePath, destinationPath, null, true);
						}
						catch (PlatformNotSupportedException)
						{
							File.Move(sourcePath, destinationPath, true);
						}
						catch (NotSupportedException)
						{
							File.Move(sourcePath, destinationPath, true);
						}
					}
					else
					{
						File.Move(sourcePath, destinationPath);
					}

					return;
				}
				catch (IOException ex)
				{
					lastError = ex;
				}
				catch (UnauthorizedAccessException ex)
				{
					lastError = ex;
				}

				if (attempt < maxAttempts)
				{
					int delayMs = Math.Min(initialDelayMs * attempt, maxDelayMs);

					DebugStr($"[SAVE] Destination temporarily unavailable; " +
							 $"retry {attempt}/{maxAttempts - 1} " + $"after {delayMs} ms.");

					Thread.Sleep(delayMs);
				}
			}

			throw new IOException($"[FATAL] Could not replace '{destinationPath}' " +
									  $"after {maxAttempts} attempts.",
								  lastError);
		}

		// ============================================================
		// CONTAINER VALIDATION
		// ============================================================

		private static void ValidateBundleContainer(string bundlePath)
		{
			AssetsManager validator = new AssetsManager();

			try
			{
				BundleFileInstance bundle = validator.LoadBundleFile(bundlePath, true);

				if (bundle == null)
				{
					throw new InvalidDataException("[FATAL] Bundle could not be reopened: " + bundlePath);
				}

				AssetBundleCompressionType compression = bundle.file.GetCompressionType();

				int dirCount = bundle.file.BlockAndDirInfo.DirectoryInfos.Count;

				DebugStr($"[CHECK] Container OK: " + $"path={bundlePath}");

				DebugStr($"[CHECK] Signature={bundle.file.Header.Signature}, " +
						 $"UnityVersion={bundle.file.Header.EngineVersion}, " +
						 $"compression={compression}, " + $"dirs={dirCount}, " +
						 $"blocks={bundle.file.BlockAndDirInfo.BlockInfos.Length}");

				int assetsCount = 0;

				for (int i = 0; i < dirCount; i++)
				{
					var dir = bundle.file.BlockAndDirInfo.DirectoryInfos[i];

					/*
					 * IMPORTANTE:
					 *
					 * Non usare IsAssetsFile() qui.
					 *
					 * IsAssetsFile() è una detection euristica sui byte.
					 * Un .resS/resource può produrre una falsa positività.
					 *
					 * Il bundle directory flag 0x04 identifica direttamente
					 * un serialized assets file.
					 */
					bool isSerializedAssets = (dir.Flags & 0x04u) != 0;

					DebugStr($"[CHECK]   directory[{i}] " + $"name='{dir.Name}', " +
							 $"flags=0x{dir.Flags:X8}, " + $"serialized={isSerializedAssets}");

					if (!isSerializedAssets)
					{
						DebugStr($"[CHECK]   skipping non-serialized bundle entry: " + $"'{dir.Name}'");

						continue;
					}

					int fileIndex = bundle.file.GetFileIndex(dir.Name);

					if (fileIndex < 0)
					{
						throw new InvalidDataException($"[FATAL] Serialized bundle entry not found: " +
													   $"{dir.Name}");
					}

					AssetsFileInstance inst = validator.LoadAssetsFileFromBundle(bundle, fileIndex, true);

					if (inst == null)
					{
						throw new InvalidDataException("[FATAL] Could not load serialized assets entry: " +
													   dir.Name);
					}

					assetsCount++;

					DebugStr($"[CHECK]   assets file '{dir.Name}' loaded; " +
							 $"asset count={inst.file.AssetInfos.Count}, " +
							 $"unity={inst.file.Metadata.UnityVersion}");
				}

				DebugStr($"[CHECK] Serialized asset files successfully loaded: " + $"{assetsCount}");
			}
			finally
			{
				try
				{
					validator.UnloadAllAssetsFiles(true);
				}
				catch
				{
				}

				try
				{
					validator.UnloadAllBundleFiles();
				}
				catch
				{
				}
			}
		}

		// ============================================================
		// BEFORE SNAPSHOT
		// ============================================================

		private static AssetsFileSnapshot CaptureAssetsFileSnapshot(AssetsManager am,
																	AssetsFileInstance inst,
																	string name)
		{
			var snapshot = new AssetsFileSnapshot { Name = name };

			using (var stream = new MemoryStream()) using (var writer = new AssetsFileWriter(stream))
			{
				inst.file.Write(writer);

				byte[] serializedFile = stream.ToArray();

				snapshot.SerializedLength = serializedFile.Length;

				snapshot.Sha256 = Sha256Hex(serializedFile);
			}

			foreach (var inf in inst.file.AssetInfos)
			{
				var fp = new AssetFingerprint
				{
					PathId = inf.PathId,

					TypeId = inf.TypeId,

					ByteSize = inf.ByteSize,

					MonoScriptIndex = inst.file.GetScriptIndex(inf)
				};

				try
				{
					var bf = am.GetBaseField(inst, inf);

					fp.Name = TryGetName(bf);

					fp.SerializedSha256 = Sha256Hex(bf.WriteToByteArray());
				}
				catch (Exception ex)
				{
					fp.SerializedSha256 = "UNAVAILABLE:" + ex.GetType().Name;

					DebugStr($"[CHECK] Could not fingerprint asset " +
							 $"PID={fp.PathId}: " + $"{ex.Message}");
				}

				snapshot.Assets.Add(fp);
			}

			return snapshot;
		}

		// ============================================================
		// FINAL VALIDATION
		// ============================================================

		private static void ValidateFinalBundle(string bundlePath, string assetfileName,
												long targetPathId, string dumpPath, string fileKind,
												byte[] expectedTargetData,
												AssetsFileSnapshot beforeSnapshot,
												AssetBundleCompressionType originalCompression,
												List<string> originalDirectoryNames,
												int expectedTargetTypeId, bool isTextReplacement)
		{
			AssetsManager am = new AssetsManager();

			try
			{
				// ====================================================
				// NORMALIZE FILE KIND
				// ====================================================

				if (IsVideoClipAsResourceKind(fileKind))
				{
					if (expectedTargetTypeId != VideoClipTypeId)
					{
						throw new InvalidDataException(
							$"[FATAL] VIDEOCLIP_AS_RESOURCE final validation requires " +
							$"VideoClip TypeID={VideoClipTypeId}, " +
							$"but received TypeID={expectedTargetTypeId}.");
					}

					DebugStr($"[CHECK] Final validation kind='VIDEOCLIP_AS_RESOURCE' " +
							 $"for VideoClip TypeID={expectedTargetTypeId}.");
				}
				else if (string.Equals(fileKind, "PNG", StringComparison.OrdinalIgnoreCase))
				{
					DebugStr($"[CHECK] Final validation kind='PNG' " +
							 $"for Texture2D TypeID={expectedTargetTypeId}.");

					if (expectedTargetTypeId != 28)
					{
						throw new InvalidDataException(
							$"[FATAL] PNG final validation requires Texture2D " +
							$"TypeID=28, but received TypeID={expectedTargetTypeId}.");
					}
				}
				else
				{
					fileKind = ResolveFileKindForTarget(fileKind, expectedTargetTypeId);

					DebugStr($"[CHECK] Final validation kind='{fileKind}' " +
							 $"for TypeID={expectedTargetTypeId}.");
				}

				// ====================================================
				// LOAD FINAL BUNDLE
				// ====================================================

				BundleFileInstance bundle = am.LoadBundleFile(bundlePath, true);

				if (bundle == null)
				{
					throw new InvalidDataException("[FATAL] Final bundle cannot be reopened.");
				}

				// ====================================================
				// CONTAINER VALIDATION
				// ====================================================

				AssetBundleCompressionType finalCompression = bundle.file.GetCompressionType();

				var finalDirectoryNames =
					bundle.file.BlockAndDirInfo.DirectoryInfos.Select(d => d.Name).ToList();

				if (finalCompression != originalCompression)
				{
					throw new InvalidDataException(
						$"[FATAL] Compression changed: " + $"original={originalCompression}, " +
						$"final={finalCompression}");
				}

				if (!originalDirectoryNames.SequenceEqual(finalDirectoryNames, StringComparer.Ordinal))
				{
					throw new InvalidDataException("[FATAL] Bundle directory entry names/order " +
												   "changed after repack.");
				}

				DebugStr($"[CHECK] Final compression={finalCompression}; " +
						 $"directory layout identical " + $"({finalDirectoryNames.Count} entries).");

				// ====================================================
				// FIND ASSETS FILE
				// ====================================================

				int fileIndex = bundle.file.GetFileIndex(assetfileName);

				if (fileIndex < 0)
				{
					throw new InvalidDataException("[FATAL] Expected assets file entry is missing: " +
												   assetfileName);
				}

				AssetsFileInstance inst = am.LoadAssetsFileFromBundle(bundle, fileIndex, true);

				if (inst == null)
				{
					throw new InvalidDataException("[FATAL] Expected assets file could not be reopened: " +
												   assetfileName);
				}

				// ====================================================
				// AFTER SNAPSHOT
				// ====================================================

				AssetsFileSnapshot afterSnapshot = CaptureAssetsFileSnapshot(am, inst, assetfileName);

				DebugStr($"[CHECK] AFTER assets '{assetfileName}' " + $"SHA256={afterSnapshot.Sha256} " +
						 $"serializedLength={afterSnapshot.SerializedLength} " +
						 $"assets={afterSnapshot.Assets.Count}");

				// ====================================================
				// ASSET COUNT
				// ====================================================

				if (afterSnapshot.Assets.Count != beforeSnapshot.Assets.Count)
				{
					throw new InvalidDataException(
						$"[FATAL] Asset count changed: " + $"before={beforeSnapshot.Assets.Count} " +
						$"after={afterSnapshot.Assets.Count}");
				}

				// ====================================================
				// VERIFY ALL NON-TARGET ASSETS
				// ====================================================

				foreach (var before in beforeSnapshot.Assets)
				{
					var after = afterSnapshot.Assets.FirstOrDefault(a => a.PathId == before.PathId);

					if (after == null)
					{
						throw new InvalidDataException("[FATAL] PathID disappeared after rewrite: " +
													   before.PathId);
					}

					if (after.TypeId != before.TypeId)
					{
						throw new InvalidDataException($"[FATAL] TypeID changed for PID " +
													   $"{before.PathId}: " + $"{before.TypeId}->" +
													   $"{after.TypeId}");
					}

					if (before.TypeId == 114 && after.MonoScriptIndex != before.MonoScriptIndex)
					{
						throw new InvalidDataException($"[FATAL] MonoScriptIndex changed for PID " +
													   $"{before.PathId}: " + $"{before.MonoScriptIndex}->" +
													   $"{after.MonoScriptIndex}");
					}

					if (before.PathId != targetPathId)
					{
						if (!string.Equals(before.SerializedSha256, after.SerializedSha256,
										   StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException(
								$"[FATAL] UNEXPECTED ASSET CHANGE: " + $"PID={before.PathId} " +
								$"name='{before.Name}' " + $"SHA " + $"{before.SerializedSha256}->" +
								$"{after.SerializedSha256}");
						}
					}
				}

				// ====================================================
				// TARGET BEFORE
				// ====================================================

				var targetBefore = beforeSnapshot.Assets.FirstOrDefault(a => a.PathId == targetPathId);

				if (targetBefore == null)
				{
					throw new InvalidDataException("[FATAL] Target PathID was not present in " +
												   "the original snapshot: " + targetPathId);
				}

				// ====================================================
				// TARGET AFTER
				// ====================================================

				var targetInfo = inst.file.AssetInfos.FirstOrDefault(a => a.PathId == targetPathId);

				if (targetInfo == null)
				{
					throw new InvalidDataException("[FATAL] Target PathID missing after repack: " +
												   targetPathId);
				}

				// ====================================================
				// TARGET TYPE
				// ====================================================

				if (targetInfo.TypeId != expectedTargetTypeId)
				{
					throw new InvalidDataException(
						$"[FATAL] Target TypeID changed: " + $"original={expectedTargetTypeId}, " +
						$"final={targetInfo.TypeId}");
				}

				DebugStr($"[CHECK] Target type preserved: " + $"PID={targetInfo.PathId}, " +
						 $"TypeID={targetInfo.TypeId}");

				// ====================================================
				// MONOSCRIPT INDEX
				// ====================================================

				ushort finalMonoId = inst.file.GetScriptIndex(targetInfo);

				if (expectedTargetTypeId == 114)
				{
					if (finalMonoId == 0xFFFF)
					{
						throw new InvalidDataException("[FATAL] Target MonoBehaviour lost " +
													   "its MonoScript index.");
					}

					DebugStr($"[CHECK] Target MonoScriptIndex=" + $"{finalMonoId} " +
							 $"(0x{finalMonoId:X4})");

					if (finalMonoId != targetBefore.MonoScriptIndex)
					{
						throw new InvalidDataException($"[FATAL] Target MonoScriptIndex changed: " +
													   $"original={targetBefore.MonoScriptIndex}, " +
													   $"final={finalMonoId}");
					}
				}
				else
				{
					DebugStr($"[CHECK] Non-MonoBehaviour target; " + $"MonoScriptIndex={finalMonoId} " +
							 $"(0x{finalMonoId:X4}) accepted.");
				}

				// ====================================================
				// RAW MONOBEHAVIOUR TEXT
				// ====================================================

				bool isPng = string.Equals(fileKind, "PNG", StringComparison.OrdinalIgnoreCase);

				bool rawTextKind = !isPng && IsRawMonoBehaviourTextKind(fileKind);

				// ====================================================
				// TARGET BASEFIELD
				// ====================================================

				AssetsTools.NET.AssetTypeValueField targetField = null;

				bool needsBaseField = !rawTextKind;

				if (needsBaseField)
				{
					try
					{
						targetField = am.GetBaseField(inst, targetInfo);
					}
					catch (Exception ex)
					{
						throw new InvalidDataException($"[FATAL] Could not obtain final target BaseField " +
														   $"for kind '{fileKind}'.",
													   ex);
					}

					if (targetField == null || targetField.IsDummy)
					{
						throw new InvalidDataException($"[FATAL] Final target BaseField is null/dummy " +
													   $"for fileKind='{fileKind}'. " +
													   "This mode requires a usable TypeTree.");
					}
				}

				// ====================================================
				// READ FINAL TARGET PAYLOAD FROM RAW FILE DATA
				// ====================================================
				//
				// IMPORTANT:
				// The authoritative payload check must use the actual bytes
				// stored in the reopened final assets file, not a reserialized
				// BaseField. UABEA/AssetStudio read the serialized asset payload.
				// We still keep targetField above for structural validation.
				//

				byte[] finalTargetData = ReadRawAssetBytes(inst, targetInfo);

				DebugStr($"[CHECK] FINAL RAW TARGET: " + $"PID={targetInfo.PathId}, " +
						 $"TypeID={targetInfo.TypeId}, " + $"ByteSize={targetInfo.ByteSize}, " +
						 $"bytes={finalTargetData.Length}, " + $"SHA256={Sha256Hex(finalTargetData)}");

				DebugStr($"[CHECK] FINAL EXPECTED RAW TARGET: " + $"bytes={expectedTargetData.Length}, " +
						 $"SHA256={Sha256Hex(expectedTargetData)}");

				ComparePayloads("[CHECK] FINAL RAW TARGET", finalTargetData, expectedTargetData);

				if (finalTargetData == null || finalTargetData.Length == 0)
				{
					throw new InvalidDataException("[FATAL] Final target payload is null or empty.");
				}

				if (expectedTargetData == null || expectedTargetData.Length == 0)
				{
					throw new InvalidDataException("[FATAL] Expected target replacement payload " +
												   "is null or empty.");
				}

				string finalTargetSha = Sha256Hex(finalTargetData);

				string expectedTargetSha = Sha256Hex(expectedTargetData);

				bool targetChanged = !string.Equals(targetBefore.SerializedSha256, finalTargetSha,
													StringComparison.OrdinalIgnoreCase);

				DebugStr($"[CHECK] TARGET CHANGE: " + $"changed={targetChanged}, " +
						 $"before={targetBefore.SerializedSha256}, " + $"after={finalTargetSha}");

				DebugStr($"[CHECK] Target payload SHA " + $"expected={expectedTargetSha} " +
						 $"actual={finalTargetSha}, " + $"bytes expected={expectedTargetData.Length} " +
						 $"actual={finalTargetData.Length}");

				// ====================================================
				// EXACT TARGET PAYLOAD VALIDATION
				// ====================================================

				if (finalTargetData.Length != expectedTargetData.Length)
				{
					throw new InvalidDataException($"[FATAL] Final target payload length mismatch: " +
												   $"expected={expectedTargetData.Length}, " +
												   $"actual={finalTargetData.Length}");
				}

				if (!string.Equals(finalTargetSha, expectedTargetSha, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("[FATAL] Final target payload does not match " +
												   "the in-memory replacement payload.");
				}

				DebugStr("[CHECK] Final target payload matches " + "the intended replacement data.");

				// ====================================================
				// TXT VALIDATION
				// ====================================================

				if (isTextReplacement)
				{
					// ====================================================
					// GAMEOBJECT
					// ====================================================

					if (expectedTargetTypeId == 1)
					{
						if (!IsGameObjectFullKind(fileKind))
						{
							throw new InvalidDataException($"[FATAL] TypeID=1 requires a GameObject fileKind, " +
														   $"but received '{fileKind}'.");
						}

						if (targetField == null || targetField.IsDummy)
						{
							throw new InvalidDataException("[FATAL] GAMEOBJECT validation requires " +
														   "a valid BaseField.");
						}

						if (string.Equals(fileKind, "GAMEOBJECT_FULL_CHECKED",
										  StringComparison.OrdinalIgnoreCase))
						{
							ValidateDumpAgainstBaseField(dumpPath, targetField);

							DebugStr("[CHECK] GAMEOBJECT_FULL_CHECKED: " +
									 "full serialized GameObject + scalar " + "validation PASSED.");

							return;
						}

						// GAMEOBJECT_FULL:
						// l'importer ha già applicato il dump e il controllo
						// byte-for-byte sopra ha verificato il payload effettivamente
						// scritto nel bundle.
						DebugStr("[CHECK] GAMEOBJECT_FULL: " + "exact serialized payload validation PASSED.");

						return;
					}

					// ====================================================
					// TEXTASSET
					// ====================================================
					//
					// TextAsset (TypeID=49) is intentionally NOT passed
					// through ValidateDumpAgainstBaseField().
					//
					// A TextAsset dump contains logical fields such as
					// m_Name and m_Script, but the final TextAsset
					// BaseField is not guaranteed to expose those fields
					// through the generic scalar mapping used by Utils.cs.
					//
					// The exact serialized payload SHA/length comparison
					// above is the authoritative validation here.
					//
					if (expectedTargetTypeId == 49)
					{
						DebugStr(
							"[CHECK] TypeID=49 TextAsset: " + "generic BaseField scalar validation skipped. " +
							"Exact serialized payload validation PASSED.");

						return;
					}

					// ====================================================
					// MONOBEHAVIOUR
					// ====================================================

					if (expectedTargetTypeId == 114)
					{
						if (string.Equals(fileKind, "MONOBEHAVIOUR_TEXT", StringComparison.OrdinalIgnoreCase))
						{
							DebugStr("[CHECK] MONOBEHAVIOUR_TEXT: " + "RAW m_text payload validation PASSED. " +
									 "No scalar validation requested.");

							return;
						}

						if (string.Equals(fileKind, "MONOBEHAVIOUR_TEXT_CHECKED",
										  StringComparison.OrdinalIgnoreCase))
						{
							if (targetField == null || targetField.IsDummy)
							{
								throw new InvalidDataException("MONOBEHAVIOUR_TEXT_CHECKED requires " +
															   "a valid BaseField.");
							}

							string checkedMText = ReadDumpMText(dumpPath);

							AssetsTools.NET.AssetTypeValueField checkedTextField;

							try
							{
								checkedTextField = targetField["m_text"];
							}
							catch (Exception ex)
							{
								throw new InvalidDataException(
									"[FATAL] MONOBEHAVIOUR_TEXT_CHECKED could not " + "access m_text.", ex);
							}

							if (checkedTextField == null || checkedTextField.IsDummy)
							{
								throw new InvalidDataException("[FATAL] MONOBEHAVIOUR_TEXT_CHECKED found a " +
															   "null/dummy m_text field.");
							}

							checkedTextField.AsString = checkedMText;

							DebugStr($"[CHECK] MONOBEHAVIOUR_TEXT_CHECKED: " +
									 $"running scalar validation with " + $"m_text length={checkedMText.Length}");

							ValidateDumpAgainstBaseField(dumpPath, targetField);

							DebugStr("[CHECK] MONOBEHAVIOUR_TEXT_CHECKED: " +
									 "m_text + scalar validation PASSED.");

							return;
						}

						if (string.Equals(fileKind, "MONOBEHAVIOUR_FONT", StringComparison.OrdinalIgnoreCase))
						{
							DebugStr("[CHECK] MONOBEHAVIOUR_FONT: " + "full payload validation PASSED. " +
									 "No scalar validation requested.");

							return;
						}

						if (string.Equals(fileKind, "MONOBEHAVIOUR_FONT_CHECKED",
										  StringComparison.OrdinalIgnoreCase))
						{
							if (targetField == null || targetField.IsDummy)
							{
								throw new InvalidDataException("[FATAL] MONOBEHAVIOUR_FONT_CHECKED requires " +
															   "a valid BaseField.");
							}

							ValidateFontDumpAgainstBaseField(dumpPath, targetField);

							DebugStr("[CHECK] MONOBEHAVIOUR_FONT_CHECKED: " +
									 "partial Font payload + structural validation PASSED.");

							return;
						}

						if (string.Equals(fileKind, "MONOBEHAVIOUR_FULL", StringComparison.OrdinalIgnoreCase))
						{
							DebugStr("[CHECK] MONOBEHAVIOUR_FULL: " + "full payload validation PASSED. " +
									 "No scalar validation requested.");

							return;
						}

						if (string.Equals(fileKind, "MONOBEHAVIOUR_FULL_CHECKED",
										  StringComparison.OrdinalIgnoreCase))
						{
							ValidateDumpAgainstBaseField(dumpPath, targetField);

							DebugStr("[CHECK] MONOBEHAVIOUR_FULL_CHECKED: " +
									 "full payload + scalar validation PASSED.");

							return;
						}

						throw new InvalidDataException($"[FATAL] Unknown MonoBehaviour fileKind " +
													   $"'{fileKind}'.");
					}

					// FONT

					// ====================================================
					// FONT
					// ====================================================

					if (expectedTargetTypeId == 128)
					{
						if (string.Equals(fileKind, "FONT", StringComparison.OrdinalIgnoreCase))
						{
							DebugStr("[CHECK] FONT: " + "partial Font payload validation PASSED. " +
									 "No scalar validation requested.");

							return;
						}

						if (string.Equals(fileKind, "FONT_CHECKED", StringComparison.OrdinalIgnoreCase))
						{
							if (targetField == null || targetField.IsDummy)
							{
								throw new InvalidDataException("[FATAL] FONT_CHECKED requires " +
															   "a valid BaseField.");
							}

							ValidateFontDumpAgainstBaseField(dumpPath, targetField);

							DebugStr("[CHECK] FONT_CHECKED: " +
									 "partial Font payload + structural validation PASSED.");

							return;
						}

						throw new InvalidDataException(
							$"[FATAL] TypeID=128 requires a Font fileKind " +
							$"('FONT' or 'FONT_CHECKED'), but received '{fileKind}'.");
					}

					// ====================================================
					// MATERIAL
					// ====================================================

					if (expectedTargetTypeId == 21)
					{
						if (string.Equals(fileKind, "MATERIAL_FULL_CHECKED",
										  StringComparison.OrdinalIgnoreCase))
						{
							if (targetField == null || targetField.IsDummy)
							{
								throw new InvalidDataException("[FATAL] MATERIAL_FULL_CHECKED requires " +
															   "a valid BaseField.");
							}

							ValidateDumpAgainstBaseField(dumpPath, targetField);

							DebugStr("[CHECK] MATERIAL_FULL_CHECKED: " +
									 "full Material payload + scalar validation PASSED.");

							return;
						}

						if (string.Equals(fileKind, "MATERIAL_FULL", StringComparison.OrdinalIgnoreCase))
						{
							DebugStr("[CHECK] MATERIAL_FULL: " +
									 "exact serialized Material payload validation PASSED.");

							return;
						}

						throw new InvalidDataException($"[FATAL] TypeID=21 requires a Material fileKind " +
													   $"('MATERIAL_FULL' or 'MATERIAL_FULL_CHECKED'), " +
													   $"but received '{fileKind}'.");
					}

					// ====================================================
					// RECTTRANSFORM
					// ====================================================

					if (expectedTargetTypeId == 224)
					{
						if (!IsRectTransformKind(fileKind))
						{
							throw new InvalidDataException($"[FATAL] TypeID=224 requires a RectTransform " +
														   $"fileKind, but received '{fileKind}'.");
						}

						if (targetField == null || targetField.IsDummy)
						{
							throw new InvalidDataException("[FATAL] RECTTRANSFORM validation requires " +
														   "a valid BaseField.");
						}

						ValidateDumpAgainstBaseField(dumpPath, targetField);

						DebugStr("[CHECK] RECTTRANSFORM_FULL_CHECKED: " +
								 "full serialized RectTransform validation PASSED.");

						return;
					}

					// ====================================================
					// SPRITE
					// ====================================================

					if (expectedTargetTypeId == 213)
					{
						if (!IsSpriteKind(fileKind))
						{
							throw new InvalidDataException($"[FATAL] TypeID=213 requires a Sprite " +
														   $"fileKind, but received '{fileKind}'.");
						}

						if (targetField == null || targetField.IsDummy)
						{
							throw new InvalidDataException("[FATAL] SPRITE validation requires " +
														   "a valid BaseField.");
						}

						/*
						 * For SPRITE_FULL the actual structural validator
						 * in Utils.cs is responsible for handling arrays
						 * and ByteArrays whose sizes differ between source
						 * dump and target Sprite.
						 *
						 * SPRITE_FULL_CHECKED remains available for
						 * structurally identical Sprite dumps.
						 */
						ValidateDumpAgainstBaseField(dumpPath, targetField);

						DebugStr($"[CHECK] {fileKind}: " + "full serialized Sprite validation PASSED.");

						return;
					}

					throw new InvalidDataException(
						$"[FATAL] TXT replacement requested for unsupported " +
						$"TypeID={expectedTargetTypeId}. " + $"Supported TXT types are " +
						$"Material (21), " + $"TextAsset (49), " + $"MonoBehaviour (114), " +
						$"Font (128), " + $"RectTransform (224), " + $"Sprite (213).");
				}

				// ====================================================
				// VIDEOCLIP AS RESOURCE
				// ====================================================

				if (IsVideoClipAsResourceKind(fileKind))
				{
					ValidateVideoClipAsResourceFinal(am, bundle, inst, targetInfo, targetField, dumpPath);

					DebugStr("[CHECK] VIDEOCLIP_AS_RESOURCE: " +
							 "VideoClip + raw .resource validation PASSED.");

					return;
				}

				// ====================================================
				// NON-TXT
				// ====================================================

				DebugStr("[CHECK] Generic asset replacement payload " + "validation PASSED.");
			}
			finally
			{
				try
				{
					am.UnloadAllAssetsFiles(true);
				}
				catch
				{
				}

				try
				{
					am.UnloadAllBundleFiles();
				}
				catch
				{
				}
			}
		}

		// ============================================================
		// NAME
		// ============================================================

		private static string TryGetName(AssetsTools.NET.AssetTypeValueField field)
		{
			try
			{
				if (field == null || field.IsDummy)
				{
					return "<dummy>";
				}

				var name = field["m_Name"];

				return name?.AsString ?? "";
			}
			catch
			{
				return "";
			}
		}

		// ============================================================
		// SHA256
		// ============================================================

		private static string Sha256File(string path)
		{
			using var sha = SHA256.Create();

			using var stream = File.OpenRead(path);

			return Convert.ToHexString(sha.ComputeHash(stream));
		}

		private static string Sha256Hex(byte[] data)
		{
			using var sha = SHA256.Create();

			return Convert.ToHexString(sha.ComputeHash(data ?? Array.Empty<byte>()));
		}

		private static byte[] ReadBundleDirectoryEntryBytes(BundleFileInstance bundleInst,
															string assetfileName)
		{
			if (bundleInst == null || bundleInst.file == null)
			{
				throw new InvalidOperationException("[FATAL] Bundle instance is null.");
			}

			int dirIndex = bundleInst.file.GetFileIndex(assetfileName);

			if (dirIndex < 0)
			{
				throw new InvalidDataException($"[FATAL] Bundle entry not found: {assetfileName}");
			}

			bundleInst.file.GetFileRange(dirIndex, out long offset, out long length);

			if (offset < 0 || length < 0 || length > int.MaxValue)
			{
				throw new InvalidDataException(
					$"[FATAL] Invalid bundle entry range: " + $"name='{assetfileName}', " +
					$"offset={offset}, " + $"length={length}");
			}

			AssetsFileReader reader = bundleInst.file.DataReader;

			if (reader == null)
			{
				throw new InvalidDataException("[FATAL] Bundle DataReader is null.");
			}

			reader.Position = offset;

			byte[] data = reader.ReadBytes((int)length);

			if (data == null || data.Length != (int)length)
			{
				throw new EndOfStreamException($"[FATAL] Could not read bundle entry '{assetfileName}'. " +
											   $"Expected={length}, " +
											   $"Actual={(data == null ? 0 : data.Length)}");
			}

			return data;
		}

		private static void DebugBundleEntryBytes(BundleFileInstance bundleInst, string assetfileName,
												  string label, byte[] expectedData)
		{
			try
			{
				byte[] actualData = ReadBundleDirectoryEntryBytes(bundleInst, assetfileName);

				DebugStr($"[BUNDLE DEBUG] {label}: " + $"entry='{assetfileName}', " +
						 $"bytes={actualData.Length}, " + $"SHA256={Sha256Hex(actualData)}");

				if (expectedData == null)
				{
					DebugStr($"[BUNDLE DEBUG] {label}: " + "no expected payload supplied.");
					return;
				}

				DebugStr($"[BUNDLE DEBUG] {label}: " + $"expectedBytes={expectedData.Length}, " +
						 $"expectedSHA256={Sha256Hex(expectedData)}");

				int compareLength = Math.Min(actualData.Length, expectedData.Length);

				int firstDifference = -1;

				for (int i = 0; i < compareLength; i++)
				{
					if (actualData[i] != expectedData[i])
					{
						firstDifference = i;
						break;
					}
				}

				if (firstDifference >= 0)
				{
					DebugStr($"[BUNDLE DEBUG] {label}: FIRST DIFFERENCE " + $"offset={firstDifference}, " +
							 $"actual=0x{actualData[firstDifference]:X2}, " +
							 $"expected=0x{expectedData[firstDifference]:X2}");
				}
				else if (actualData.Length != expectedData.Length)
				{
					DebugStr($"[BUNDLE DEBUG] {label}: " + $"common prefix={compareLength}, " +
							 $"but lengths differ.");
				}
				else
				{
					DebugStr($"[BUNDLE DEBUG] {label}: " +
							 "entry bytes are byte-identical to expected payload.");
				}
			}
			catch (Exception ex)
			{
				DebugStr($"[BUNDLE DEBUG] {label}: FAILED: " + $"{ex.GetType().Name}: {ex.Message}");

				DebugStr(ex.ToString());
			}
		}

		private static byte[] ReadRawAssetBytesFromAssetsFile(AssetsFile assetsFile,
															  AssetsFileReader reader,
															  AssetFileInfo info)
		{
			if (assetsFile == null)
			{
				throw new ArgumentNullException(nameof(assetsFile));
			}

			if (reader == null)
			{
				throw new ArgumentNullException(nameof(reader));
			}

			if (info == null)
			{
				throw new ArgumentNullException(nameof(info));
			}

			long absoluteOffset = info.GetAbsoluteByteOffset(assetsFile);

			if (absoluteOffset < 0)
			{
				throw new InvalidDataException($"[FATAL] Invalid absolute asset offset: {absoluteOffset}");
			}

			if (info.ByteSize < 0 || info.ByteSize > int.MaxValue)
			{
				throw new InvalidDataException($"[FATAL] Invalid asset byte size: {info.ByteSize}");
			}

			reader.Position = absoluteOffset;

			byte[] data = reader.ReadBytes((int)info.ByteSize);

			if (data == null || data.Length != (int)info.ByteSize)
			{
				throw new EndOfStreamException($"[FATAL] Could not read raw asset payload. " +
											   $"Expected={info.ByteSize}, " +
											   $"Actual={(data == null ? 0 : data.Length)}");
			}

			return data;
		}

		private static void ComparePayloads(string label, byte[] actual, byte[] expected)
		{
			if (actual == null || expected == null)
			{
				DebugStr($"{label}: comparison skipped because one payload is null.");
				return;
			}

			int compareLength = Math.Min(actual.Length, expected.Length);

			int firstDifference = -1;

			for (int i = 0; i < compareLength; i++)
			{
				if (actual[i] != expected[i])
				{
					firstDifference = i;
					break;
				}
			}

			if (firstDifference >= 0)
			{
				DebugStr($"{label}: FIRST DIFFERENCE " + $"offset={firstDifference}, " +
						 $"actual=0x{actual[firstDifference]:X2}, " +
						 $"expected=0x{expected[firstDifference]:X2}");
			}
			else if (actual.Length != expected.Length)
			{
				DebugStr($"{label}: common prefix={compareLength}, " +
						 $"length mismatch actual={actual.Length}, " + $"expected={expected.Length}");
			}
			else
			{
				DebugStr($"{label}: payloads are byte-identical.");
			}
		}

		private static void DebugReloadedStage1Target(BundleFileInstance bundleInst,
													  string assetfileName, long targetPathId)
		{
			try
			{
				AssetsManager verifyManager = new AssetsManager();

				BundleFileInstance verifyBundle = verifyManager.LoadBundleFile(assetfileName);

				// This overload is intentionally not used here.
				// The actual bundle entry is already available from bundleInst.
				_ = verifyBundle;
				_ = targetPathId;
			}
			catch
			{
			}
		}

		private static void DebugFindFloatPatterns(string label, byte[] payload)
		{
			if (payload == null || payload.Length == 0)
			{
				DebugStr($"[FLOAT DEBUG] {label}: empty payload.");
				return;
			}

			float[] values = { 91.07612f,  164.02675f, 1871.8971f, 170.89714f,
						 1819.8971f, 351.98532f, 0.0f };

			DebugStr($"[FLOAT DEBUG] ===== {label} ===== " + $"bytes={payload.Length}");

			foreach (float value in values)
			{
				byte[] pattern = BitConverter.GetBytes(value);

				List<int> offsets = new List<int>();

				for (int i = 0; i <= payload.Length - pattern.Length; i++)
				{
					bool match = true;

					for (int j = 0; j < pattern.Length; j++)
					{
						if (payload[i + j] != pattern[j])
						{
							match = false;
							break;
						}
					}

					if (match)
					{
						offsets.Add(i);
					}
				}

				string offsetText = offsets.Count == 0 ? "<none>" : string.Join(", ", offsets.Take(20));

				DebugStr($"[FLOAT DEBUG] value={value.ToString(
									System.Globalization.CultureInfo.InvariantCulture)}, " +
						 $"bytes={Convert.ToHexString(pattern)}, " + $"count={offsets.Count}, " +
						 $"offsets={offsetText}");
			}

			DebugStr($"[FLOAT DEBUG] ===== END {label} =====");
		}

		private static void DebugPayloadWindow(string label, byte[] payload, int offset,
											   int radius = 32)
		{
			if (payload == null || payload.Length == 0)
			{
				DebugStr($"[WINDOW] {label}: payload is null/empty.");

				return;
			}

			if (offset < 0 || offset >= payload.Length)
			{
				DebugStr($"[WINDOW] {label}: " + $"offset={offset} is outside payload " +
						 $"(length={payload.Length}).");

				return;
			}

			if (radius < 0)
			{
				radius = 0;
			}

			long startLong = (long)offset - radius;

			long endLong = (long)offset + radius;

			if (startLong < 0)
			{
				startLong = 0;
			}

			if (endLong > payload.Length)
			{
				endLong = payload.Length;
			}

			int start = (int)startLong;

			int end = (int)endLong;

			if (end <= start)
			{
				DebugStr($"[WINDOW] {label}: " + $"invalid window start={start}, end={end}.");

				return;
			}

			int length = end - start;

			byte[] window = new byte[length];

			Buffer.BlockCopy(payload, start, window, 0, length);

			DebugStr($"[WINDOW] {label}: " + $"offset={offset}, " + $"range={start}..{end - 1}");

			DebugStr($"[WINDOW] {Convert.ToHexString(window)}");
		}
	}
}
