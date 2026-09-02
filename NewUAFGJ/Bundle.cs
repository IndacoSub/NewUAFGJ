using AssetsTools.NET.Extra;
using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

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
			public List<AssetFingerprint> Assets =
				new List<AssetFingerprint>();
		}


		// ============================================================
		// FILE KIND NORMALIZATION
		// ============================================================

		private static string ResolveFileKindForTarget(
			string requestedKind,
			int typeId)
		{
			/*
             * If the caller explicitly supplied a kind, preserve it.
             */
			if (!string.IsNullOrWhiteSpace(requestedKind))
			{
				return requestedKind.Trim();
			}

			/*
             * Automatic TXT/serialized asset mode.
             *
             * IMPORTANT:
             * TypeID 28 (Texture2D) is intentionally NOT handled here.
             * PNG replacements use the dedicated PNG path instead.
             */
			switch (typeId)
			{
				case 49:
					return "TEXTASSET_FULL_CHECKED";

				case 114:
					return "MONOBEHAVIOUR_FULL_CHECKED";

				case 224:
					return "RECTTRANSFORM_FULL_CHECKED";

				case 213:
					return "SPRITE_FULL";

				default:
					throw new InvalidDataException(
						$"No automatic TXT fileKind is defined for TypeID={typeId}.");
			}
		}


		private static bool IsPngReplacement(
			string inputFile)
		{
			return
				!string.IsNullOrWhiteSpace(inputFile) &&
				string.Equals(
					Path.GetExtension(inputFile),
					".png",
					StringComparison.OrdinalIgnoreCase);
		}


		private static string ResolveEffectiveFileKind(
			string requestedKind,
			int targetTypeId,
			string inputFile)
		{
			/*
             * PNG/Texture2D does NOT use the TXT fileKind resolver.
             *
             * TypeID 28 is Texture2D and is handled by:
             * FindPNGFile -> ImportTexturesCustom -> PNG save path.
             */
			if (IsPngReplacement(inputFile))
			{
				DebugStr(
					$"[CHECK] PNG replacement detected for TypeID={targetTypeId}; " +
					"skipping TXT fileKind resolution.");

				if (targetTypeId != 28)
				{
					throw new InvalidDataException(
						$"PNG replacement requires Texture2D TypeID=28, " +
						$"but target TypeID={targetTypeId}.");
				}

				return "PNG";
			}

			return ResolveFileKindForTarget(
				requestedKind,
				targetTypeId);
		}


		private static bool IsRawMonoBehaviourTextKind(
			string fileKind)
		{
			return
				string.Equals(
					fileKind,
					"MONOBEHAVIOUR_TEXT",
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					fileKind,
					"MONOBEHAVIOUR_TEXT_CHECKED",
					StringComparison.OrdinalIgnoreCase);
		}


		private static bool IsRectTransformKind(
			string fileKind)
		{
			return
				string.Equals(
					fileKind,
					"RECTTRANSFORM_FULL",
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					fileKind,
					"RECTTRANSFORM_FULL_CHECKED",
					StringComparison.OrdinalIgnoreCase);
		}


		private static bool IsSpriteKind(
			string fileKind)
		{
			return
				string.Equals(
					fileKind,
					"SPRITE_FULL",
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					fileKind,
					"SPRITE_FULL_CHECKED",
					StringComparison.OrdinalIgnoreCase);
		}


		private static bool IsMonoBehaviourFullKind(
			string fileKind)
		{
			return
				string.Equals(
					fileKind,
					"MONOBEHAVIOUR_FULL",
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					fileKind,
					"MONOBEHAVIOUR_FULL_CHECKED",
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					fileKind,
					"MONOBEHAVIOUR_FONT",
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					fileKind,
					"MONOBEHAVIOUR_FONT_CHECKED",
					StringComparison.OrdinalIgnoreCase);
		}


		// ============================================================
		// HANDLE BUNDLE
		// ============================================================

		private static void HandleBundle(
			string ab,
			string input_file,
			string specific_pathid,
			string fileKind)
		{
			LogPhase(
				$"Bundle start: bundle='{ab}', " +
				$"input='{input_file}', " +
				$"pathId='{specific_pathid}', " +
				$"kind='{fileKind}'.");

			string originalBundleSha =
				Sha256File(ab);

			long originalBundleLength =
				new FileInfo(ab).Length;

			DebugStr(
				$"[CHECK] INPUT bundle length={originalBundleLength} " +
				$"SHA256={originalBundleSha}");

			DebugStr(
				$"[CHECK] INPUT replacement SHA256={Sha256File(input_file)} " +
				$"length={new FileInfo(input_file).Length}");

			DebugStr(
				$"[CHECK] classdata.tpk SHA256=" +
				$"{(File.Exists("classdata.tpk")
					? Sha256File("classdata.tpk")
					: "MISSING")}");

			// ========================================================
			// UNIQUE STAGING FILES
			// ========================================================

			string tempBundlePath =
				ab +
				".uafgj_stage1_" +
				Guid.NewGuid().ToString("N") +
				".tmp";

			string finalTempPath =
				ab +
				".uafgj_stage2_" +
				Guid.NewGuid().ToString("N") +
				".tmp";

			DebugStr(
				$"[TEMP] stage1='{tempBundlePath}'");

			DebugStr(
				$"[TEMP] stage2='{finalTempPath}'");

			CleanupStaleBundleStages(ab);

			DeleteFileIfExists(
				ab + "_temp");

			DeleteFileIfExists(
				ab + ".new");

			DeleteFileIfExists(
				ab + ".uafgj_tmp");

			DebugStr(
				"[TEMP] Stale temporary cleanup completed.");

			AssetsManager am =
				new AssetsManager();

			RuntimeSetup.Configure(
				am,
				ab);

			try
			{
				// ====================================================
				// LOAD BUNDLE
				// ====================================================

				LogPhase(
					"Loading bundle into AssetsManager.");

				BundleFileInstance bundleInst =
					GetBundleInst(
						am,
						ab);

				if (bundleInst == null)
					return;

				string assetfile_name =
					GetRightAssetFileNameFromBundle(
						bundleInst,
						ab);

				if (string.IsNullOrEmpty(
					assetfile_name))
				{
					return;
				}

				LogPhase(
					$"Loading contained assets file '{assetfile_name}'.");

				AssetsFileInstance assetInst =
					GetAssetInst(
						am,
						bundleInst,
						assetfile_name,
						ab);

				if (assetInst == null)
					return;

				EnsureClassDatabaseIfNeeded(
					am,
					assetInst);

				AssetBundleCompressionType originalCompression =
					bundleInst.file.GetCompressionType();

				var originalDirectoryNames =
					bundleInst.file.BlockAndDirInfo.DirectoryInfos
						.Select(d => d.Name)
						.ToList();

				DebugStr(
					$"[CHECK] INPUT bundle compression=" +
					$"{originalCompression}; " +
					$"directory entries={originalDirectoryNames.Count}");

				// ====================================================
				// BEFORE SNAPSHOT
				// ====================================================

				LogPhase(
					"Capturing pre-import assets snapshot.");

				AssetsFileSnapshot beforeSnapshot =
					CaptureAssetsFileSnapshot(
						am,
						assetInst,
						assetfile_name);

				DebugStr(
					$"[CHECK] BEFORE assets '{assetfile_name}' " +
					$"SHA256={beforeSnapshot.Sha256} " +
					$"serializedLength={beforeSnapshot.SerializedLength} " +
					$"assets={beforeSnapshot.Assets.Count}");

				// ====================================================
				// IMPORT STATE
				// ====================================================

				AssetsTools.NET.AssetTypeValueField atvf =
					null;

				AssetFileInfo afie =
					null;

				byte[] rawReplacementData =
					null;

				byte[] originalTargetData =
					null;

				bool isTextReplacement =
					!IsPngReplacement(input_file);

				// ====================================================
				// IMPORT
				// ====================================================

				LogPhase(
					$"Beginning import for kind='{fileKind}', " +
					$"input='{input_file}'.");

				if (isTextReplacement)
				{
					if (!FindTXTFile(
						input_file,
						ref assetInst,
						ref afie,
						ref atvf,
						ref am,
						ab,
						assetfile_name,
						specific_pathid,
						fileKind,
						out rawReplacementData,
						out originalTargetData))
					{
						DisplayStr(
							"Failed to replace TXT/serialized asset.");

						return;
					}
				}
				else
				{
					if (!FindPNGFile(
						input_file,
						ref afie,
						ref assetInst,
						ref atvf,
						ref am,
						ab,
						assetfile_name,
						specific_pathid,
						fileKind))
					{
						return;
					}

					if (atvf == null ||
						atvf.IsDummy)
					{
						DisplayStr(
							"[PNG] Replacement target BaseField " +
							"is null/dummy.");

						return;
					}

					int format =
						atvf["m_TextureFormat"].AsInt;

					if (!ImportTexturesCustom(
						ref atvf,
						input_file,
						format,
						fileKind))
					{
						return;
					}

					rawReplacementData =
						atvf.WriteToByteArray();

					originalTargetData =
						null;
				}

				// ====================================================
				// RESOLVE EFFECTIVE FILE KIND
				// ====================================================

				if (afie == null)
				{
					DisplayStr(
						"[CHECK] Replacement target is missing; " +
						"refusing to write.");

					return;
				}

				string effectiveFileKind =
					ResolveEffectiveFileKind(
						fileKind,
						afie.TypeId,
						input_file);

				DebugStr(
					$"[CHECK] Effective fileKind='{effectiveFileKind}' " +
					$"for target TypeID={afie.TypeId}.");

				// ====================================================
				// VALIDATE IMPORT RESULT
				// ====================================================

				bool isPngReplacement =
					string.Equals(
						effectiveFileKind,
						"PNG",
						StringComparison.OrdinalIgnoreCase);

				bool rawMonoTextKind =
					!isPngReplacement &&
					IsRawMonoBehaviourTextKind(
						effectiveFileKind);

				if (rawReplacementData == null ||
					rawReplacementData.Length == 0)
				{
					DisplayStr(
						"[CHECK] Replacement data is missing; " +
						"refusing to write.");

					return;
				}

				if (!rawMonoTextKind &&
					(atvf == null ||
					 atvf.IsDummy))
				{
					DisplayStr(
						"[CHECK] Replacement BaseField is missing for " +
						$"fileKind='{effectiveFileKind}'; " +
						"refusing to write.");

					return;
				}

				DebugStr(
					$"[CHECK] Replacement target accepted: " +
					$"PID={afie.PathId}, " +
					$"TypeID={afie.TypeId}, " +
					$"kind='{effectiveFileKind}', " +
					$"raw={rawMonoTextKind}");

				int expectedTargetTypeId =
					afie.TypeId;

				DebugStr(
					$"[CHECK] Replacement mode=" +
					$"{(isTextReplacement
						? "TXT"
						: "PNG/GenericAsset")}, " +
					$"PID={afie.PathId}, " +
					$"TypeID={expectedTargetTypeId}");

				// ====================================================
				// VERIFY TARGET IN BEFORE SNAPSHOT
				// ====================================================

				AssetFingerprint targetBefore =
					beforeSnapshot.Assets.FirstOrDefault(
						a =>
							a.PathId ==
							afie.PathId);

				if (targetBefore == null)
				{
					throw new InvalidDataException(
						"Target PathID disappeared " +
						"from pre-write snapshot.");
				}

				if (targetBefore.TypeId !=
					expectedTargetTypeId)
				{
					throw new InvalidDataException(
						$"Replacement target TypeID changed " +
						$"before save: " +
						$"{targetBefore.TypeId}->" +
						$"{expectedTargetTypeId}");
				}

				DebugStr(
					$"[CHECK] TARGET BEFORE " +
					$"PID={targetBefore.PathId} " +
					$"TypeID={targetBefore.TypeId} " +
					$"ByteSize={targetBefore.ByteSize} " +
					$"ScriptIndex={targetBefore.MonoScriptIndex} " +
					$"Name='{targetBefore.Name}' " +
					$"SHA256={targetBefore.SerializedSha256}");

				DebugStr(
					$"[CHECK] TARGET AFTER " +
					$"PID={afie.PathId} " +
					$"TypeID={afie.TypeId} " +
					$"bytes={rawReplacementData.Length} " +
					$"SHA256={Sha256Hex(rawReplacementData)}");

				LogPhase(
					"Replacement data accepted; " +
					"beginning save pipeline.");

				// ====================================================
				// SAVE
				// ====================================================

				if (rawMonoTextKind)
				{
					DebugStr(
						$"[SAVE] Using RAW MonoBehaviour saver " +
						$"for fileKind='{effectiveFileKind}'.");

					SaveAssetBundleRaw(
						rawReplacementData,
						afie,
						assetInst,
						bundleInst,
						assetfile_name,
						tempBundlePath);
				}
				else
				{
					SaveAssetBundle(
						atvf,
						afie,
						assetInst,
						bundleInst,
						assetfile_name,
						tempBundlePath,
						Path.GetFileNameWithoutExtension(
							input_file));
				}

				// ====================================================
				// RELEASE SOURCE HANDLES
				// ====================================================

				LogPhase(
					"Releasing source bundle handles before final pack.");

				if (!am.UnloadAllAssetsFiles(true))
				{
					DisplayStr(
						"Could not unload all asset files!");
				}

				if (!am.UnloadAllBundleFiles())
				{
					DisplayStr(
						"Could not unload all bundle files!");
				}

				// ====================================================
				// FINAL PACK
				// ====================================================

				LogPhase(
					"Beginning final pack and validation.");

				PackBundlePreservingFormat(
					ab,
					assetfile_name,
					afie.PathId,
					specific_pathid,
					input_file,
					effectiveFileKind,
					rawReplacementData,
					beforeSnapshot,
					originalBundleSha,
					originalBundleLength,
					originalCompression,
					originalDirectoryNames,
					expectedTargetTypeId,
					isTextReplacement,
					tempBundlePath,
					finalTempPath);

				DisplayStr(
					"Done!");
			}
			catch (Exception ex)
			{
				Environment.ExitCode =
					1;

				DisplayStr(
					"[FATAL] Bundle handling failed: " +
					ex.GetType().Name +
					": " +
					ex.Message);

				DebugStr(
					ex.ToString());
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

				DeleteFileIfExists(
					tempBundlePath);

				DeleteFileIfExists(
					finalTempPath);
			}
		}


		// ============================================================
		// SAVE ASSET BUNDLE
		// ============================================================

		private static void SaveAssetBundle(
			AssetsTools.NET.AssetTypeValueField modifiedBaseField,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			BundleFileInstance bundleInst,
			string assetfile_name,
			string tempBundle,
			string input_noext)
		{
			if (modifiedBaseField == null)
				throw new InvalidOperationException(
					"Modified base field is null.");

			if (afie == null)
				throw new InvalidOperationException(
					"Asset info is null.");

			afie.SetNewData(
				modifiedBaseField);

			byte[] newAssetData;

			using (var stream =
				new MemoryStream())
			using (var writer =
				new AssetsFileWriter(stream))
			{
				assetInst.file.Write(
					writer);

				newAssetData =
					stream.ToArray();
			}

			DebugStr(
				$"[SAVE] Inner assets file size=" +
				$"{newAssetData.Length} " +
				$"SHA256={Sha256Hex(newAssetData)}");

			int dirIndex =
				bundleInst.file.GetFileIndex(
					assetfile_name);

			if (dirIndex < 0)
			{
				throw new InvalidDataException(
					$"Bundle entry not found: {assetfile_name}");
			}

			bundleInst.file
				.BlockAndDirInfo
				.DirectoryInfos[dirIndex]
				.SetNewData(
					newAssetData);

			using (var fileStream =
				new FileStream(
					tempBundle,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None))
			using (var bunWriter =
				new AssetsFileWriter(
					fileStream))
			{
				bundleInst.file.Write(
					bunWriter);
			}

			DebugStr(
				$"[SAVE] Temporary bundle written: {tempBundle}");
		}


		// ============================================================
		// SAVE ASSET BUNDLE - RAW
		// ============================================================

		private static void SaveAssetBundleRaw(
			byte[] replacementData,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			BundleFileInstance bundleInst,
			string assetfile_name,
			string tempBundle)
		{
			if (replacementData == null ||
				replacementData.Length == 0)
			{
				throw new InvalidDataException(
					"Raw replacement data is empty.");
			}

			if (afie == null ||
				assetInst == null ||
				bundleInst == null)
			{
				throw new InvalidDataException(
					"Asset/bundle state is null.");
			}

			afie.SetNewData(
				replacementData);

			byte[] newAssetData;

			using (var stream =
				new MemoryStream())
			using (var writer =
				new AssetsFileWriter(stream))
			{
				assetInst.file.Write(
					writer);

				newAssetData =
					stream.ToArray();
			}

			DebugStr(
				$"[SAVE] RAW inner assets file size=" +
				$"{newAssetData.Length} " +
				$"SHA256={Sha256Hex(newAssetData)}");

			int dirIndex =
				bundleInst.file.GetFileIndex(
					assetfile_name);

			if (dirIndex < 0)
			{
				throw new InvalidDataException(
					$"Bundle entry not found: {assetfile_name}");
			}

			bundleInst.file
				.BlockAndDirInfo
				.DirectoryInfos[dirIndex]
				.SetNewData(
					newAssetData);

			using (var fileStream =
				new FileStream(
					tempBundle,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None))
			using (var bunWriter =
				new AssetsFileWriter(
					fileStream))
			{
				bundleInst.file.Write(
					bunWriter);
			}

			DebugStr(
				$"[SAVE] Temporary bundle written: {tempBundle}");
		}


		// ============================================================
		// PACK + VALIDATION
		// ============================================================

		private static void PackBundlePreservingFormat(
			string realName,
			string assetfileName,
			long targetPathId,
			string specificPathId,
			string dumpPath,
			string fileKind,
			byte[] expectedTargetData,
			AssetsFileSnapshot beforeSnapshot,
			string originalBundleSha,
			long originalBundleLength,
			AssetBundleCompressionType originalCompression,
			List<string> originalDirectoryNames,
			int expectedTargetTypeId,
			bool isTextReplacement,
			string fakeName,
			string finalTemp)
		{
			if (!File.Exists(
				fakeName))
			{
				throw new FileNotFoundException(
					"Temporary bundle missing.",
					fakeName);
			}

			DebugStr(
				"[CHECK] ===== PRE-PACK CONTAINER VALIDATION =====");

			ValidateBundleContainer(
				fakeName);

			AssetsManager am =
				new AssetsManager();

			DeleteFileIfExists(
				finalTemp);

			try
			{
				BundleFileInstance bun =
					am.LoadBundleFile(
						fakeName);

				if (bun == null)
				{
					throw new InvalidDataException(
						"Could not reopen temporary bundle.");
				}

				using (var stream =
					new FileStream(
						finalTemp,
						FileMode.CreateNew,
						FileAccess.Write,
						FileShare.None))
				using (var writer =
					new AssetsFileWriter(
						stream))
				{
					DebugStr(
						$"[PACK] Packing with original " +
						$"compression={originalCompression}.");

					DebugStr(
						$"[PACK] Source staging bundle length=" +
						$"{new FileInfo(fakeName).Length}");

					bun.file.Pack(
						writer,
						originalCompression);

					DebugStr(
						$"[PACK] Finished pack write to " +
						$"'{finalTemp}'.");
				}

				if (!am.UnloadAllBundleFiles())
				{
					DisplayStr(
						"[PACK] Could not unload temporary bundle handles.");
				}

				string finalSha =
					Sha256File(
						finalTemp);

				DebugStr(
					$"[CHECK] PACKED staging bundle length=" +
					$"{new FileInfo(finalTemp).Length} " +
					$"SHA256={finalSha}");

				DebugStr(
					"[CHECK] ===== FINAL CONTAINER VALIDATION =====");

				ValidateBundleContainer(
					finalTemp);

				ValidateFinalBundle(
					finalTemp,
					assetfileName,
					targetPathId,
					dumpPath,
					fileKind,
					expectedTargetData,
					beforeSnapshot,
					originalCompression,
					originalDirectoryNames,
					expectedTargetTypeId,
					isTextReplacement);

				if (string.Equals(
					finalSha,
					originalBundleSha,
					StringComparison.OrdinalIgnoreCase))
				{
					DebugStr(
						"[CHECK] Whole-bundle SHA matches input. " +
						"This is not treated as an import failure; " +
						"the validated target payload is authoritative.");
				}
				else
				{
					DebugStr(
						"[CHECK] Whole-bundle SHA differs from input. " +
						"Binary change detected.");
				}

				DebugStr(
					"[CHECK] ===== ALL PRE-REPLACE CHECKS PASSED =====");

				DebugStr(
					$"[CHECK] INPUT  SHA256={originalBundleSha} " +
					$"length={originalBundleLength}");

				DebugStr(
					$"[CHECK] OUTPUT SHA256={finalSha} " +
					$"length={new FileInfo(finalTemp).Length}");

				LogFileState(
					"[SAVE] FINAL STAGING BEFORE COMMIT",
					finalTemp);

				DebugStr(
					$"[SAVE] Committing validated staging file " +
					$"to '{realName}'.");

				ReplaceFileWithRetry(
					finalTemp,
					realName);

				DebugStr(
					"[SAVE] Commit operation returned successfully.");

				string committedSha =
					Sha256File(
						realName);

				DebugStr(
					$"[CHECK] COMMITTED bundle SHA256={committedSha} " +
					$"length={new FileInfo(realName).Length}");

				if (!string.Equals(
					committedSha,
					finalSha,
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						"Committed bundle SHA256 differs from " +
						"the validated staging file.");
				}
			}
			finally
			{
				try
				{
					am.UnloadAllAssetsFiles(
						true);
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

				DeleteFileIfExists(
					fakeName);

				DeleteFileIfExists(
					finalTemp);
			}
		}


		// ============================================================
		// CLEANUP
		// ============================================================

		private static void CleanupStaleBundleStages(
			string bundlePath)
		{
			try
			{
				string directory =
					Path.GetDirectoryName(
						bundlePath);

				string fileName =
					Path.GetFileName(
						bundlePath);

				if (string.IsNullOrEmpty(directory) ||
					string.IsNullOrEmpty(fileName) ||
					!Directory.Exists(directory))
				{
					return;
				}

				string[] patterns =
				{
					fileName +
					".uafgj_stage1_*.tmp",

					fileName +
					".uafgj_stage2_*.tmp"
				};

				foreach (string pattern in patterns)
				{
					foreach (string path in
						Directory.GetFiles(
							directory,
							pattern))
					{
						DeleteFileIfExists(
							path);
					}
				}
			}
			catch (Exception ex)
			{
				DebugStr(
					"[CLEANUP] Could not scan for stale staging files: " +
					ex.GetType().Name +
					": " +
					ex.Message);
			}
		}


		private static void DeleteFileIfExists(
			string path)
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
				DebugStr(
					$"[CLEANUP] Could not delete temporary file " +
					$"'{path}': " +
					$"{ex.GetType().Name}: {ex.Message}");
			}
		}


		// ============================================================
		// FILE REPLACEMENT
		// ============================================================

		private static void ReplaceFileWithRetry(
			string sourcePath,
			string destinationPath)
		{
			const int maxAttempts =
				10;

			const int delayMs =
				250;

			Exception lastError =
				null;

			if (!File.Exists(
				sourcePath))
			{
				throw new FileNotFoundException(
					"Replacement file does not exist.",
					sourcePath);
			}

			for (int attempt = 1;
				 attempt <= maxAttempts;
				 attempt++)
			{
				try
				{
					if (File.Exists(
						destinationPath))
					{
						try
						{
							File.Replace(
								sourcePath,
								destinationPath,
								null,
								true);
						}
						catch (PlatformNotSupportedException)
						{
							File.Move(
								sourcePath,
								destinationPath,
								true);
						}
						catch (NotSupportedException)
						{
							File.Move(
								sourcePath,
								destinationPath,
								true);
						}
						catch (IOException)
						{
							File.Move(
								sourcePath,
								destinationPath,
								true);
						}
					}
					else
					{
						File.Move(
							sourcePath,
							destinationPath);
					}

					return;
				}
				catch (IOException ex)
				{
					lastError =
						ex;
				}
				catch (UnauthorizedAccessException ex)
				{
					lastError =
						ex;
				}

				if (attempt < maxAttempts)
				{
					DebugStr(
						$"[SAVE] Destination temporarily unavailable; " +
						$"retry {attempt}/{maxAttempts - 1}...");

					Thread.Sleep(
						delayMs);
				}
			}

			throw new IOException(
				$"Could not replace '{destinationPath}' " +
				$"after {maxAttempts} attempts.",
				lastError);
		}


		// ============================================================
		// CONTAINER VALIDATION
		// ============================================================

		private static void ValidateBundleContainer(
			string bundlePath)
		{
			AssetsManager validator =
				new AssetsManager();

			try
			{
				BundleFileInstance bundle =
					validator.LoadBundleFile(
						bundlePath,
						true);

				if (bundle == null)
				{
					throw new InvalidDataException(
						"Bundle could not be reopened: " +
						bundlePath);
				}

				AssetBundleCompressionType compression =
					bundle.file.GetCompressionType();

				int dirCount =
					bundle.file
						.BlockAndDirInfo
						.DirectoryInfos
						.Count;

				DebugStr(
					$"[CHECK] Container OK: " +
					$"path={bundlePath}");

				DebugStr(
					$"[CHECK] Signature={bundle.file.Header.Signature}, " +
					$"UnityVersion={bundle.file.Header.EngineVersion}, " +
					$"compression={compression}, " +
					$"dirs={dirCount}, " +
					$"blocks={bundle.file.BlockAndDirInfo.BlockInfos.Length}");

				int assetsCount =
					0;

				for (int i = 0;
					 i < dirCount;
					 i++)
				{
					var dir =
						bundle.file
							.BlockAndDirInfo
							.DirectoryInfos[i];

					int fileIndex =
						bundle.file.GetFileIndex(
							dir.Name);

					if (fileIndex < 0 ||
						!bundle.file.IsAssetsFile(
							fileIndex))
					{
						continue;
					}

					AssetsFileInstance inst =
						validator.LoadAssetsFileFromBundle(
							bundle,
							fileIndex,
							true);

					if (inst == null)
					{
						throw new InvalidDataException(
							"Could not load assets entry: " +
							dir.Name);
					}

					assetsCount++;

					DebugStr(
						$"[CHECK]   assets file '{dir.Name}' loaded; " +
						$"asset count={inst.file.AssetInfos.Count}, " +
						$"unity={inst.file.Metadata.UnityVersion}");
				}

				DebugStr(
					$"[CHECK] Serialized asset files successfully loaded: " +
					$"{assetsCount}");
			}
			finally
			{
				try
				{
					validator.UnloadAllAssetsFiles(
						true);
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

		private static AssetsFileSnapshot CaptureAssetsFileSnapshot(
			AssetsManager am,
			AssetsFileInstance inst,
			string name)
		{
			var snapshot =
				new AssetsFileSnapshot
				{
					Name = name
				};

			using (var stream =
				new MemoryStream())
			using (var writer =
				new AssetsFileWriter(
					stream))
			{
				inst.file.Write(
					writer);

				byte[] serializedFile =
					stream.ToArray();

				snapshot.SerializedLength =
					serializedFile.Length;

				snapshot.Sha256 =
					Sha256Hex(
						serializedFile);
			}

			foreach (var inf
				in inst.file.AssetInfos)
			{
				var fp =
					new AssetFingerprint
					{
						PathId =
							inf.PathId,

						TypeId =
							inf.TypeId,

						ByteSize =
							inf.ByteSize,

						MonoScriptIndex =
							inst.file.GetScriptIndex(
								inf)
					};

				try
				{
					var bf =
						am.GetBaseField(
							inst,
							inf);

					fp.Name =
						TryGetName(
							bf);

					fp.SerializedSha256 =
						Sha256Hex(
							bf.WriteToByteArray());
				}
				catch (Exception ex)
				{
					fp.SerializedSha256 =
						"UNAVAILABLE:" +
						ex.GetType().Name;

					DebugStr(
						$"[CHECK] Could not fingerprint asset " +
						$"PID={fp.PathId}: " +
						$"{ex.Message}");
				}

				snapshot.Assets.Add(
					fp);
			}

			return snapshot;
		}


		// ============================================================
		// FINAL VALIDATION
		// ============================================================

		private static void ValidateFinalBundle(
			string bundlePath,
			string assetfileName,
			long targetPathId,
			string dumpPath,
			string fileKind,
			byte[] expectedTargetData,
			AssetsFileSnapshot beforeSnapshot,
			AssetBundleCompressionType originalCompression,
			List<string> originalDirectoryNames,
			int expectedTargetTypeId,
			bool isTextReplacement)
		{
			AssetsManager am =
				new AssetsManager();

			try
			{
				// ====================================================
				// NORMALIZE FILE KIND
				// ====================================================

				if (string.Equals(
					fileKind,
					"PNG",
					StringComparison.OrdinalIgnoreCase))
				{
					DebugStr(
						$"[CHECK] Final validation kind='PNG' " +
						$"for Texture2D TypeID={expectedTargetTypeId}.");

					if (expectedTargetTypeId != 28)
					{
						throw new InvalidDataException(
							$"PNG final validation requires Texture2D " +
							$"TypeID=28, but received TypeID={expectedTargetTypeId}.");
					}
				}
				else
				{
					fileKind =
						ResolveFileKindForTarget(
							fileKind,
							expectedTargetTypeId);

					DebugStr(
						$"[CHECK] Final validation kind='{fileKind}' " +
						$"for TypeID={expectedTargetTypeId}.");
				}

				// ====================================================
				// LOAD FINAL BUNDLE
				// ====================================================

				BundleFileInstance bundle =
					am.LoadBundleFile(
						bundlePath,
						true);

				if (bundle == null)
				{
					throw new InvalidDataException(
						"Final bundle cannot be reopened.");
				}

				// ====================================================
				// CONTAINER VALIDATION
				// ====================================================

				AssetBundleCompressionType finalCompression =
					bundle.file.GetCompressionType();

				var finalDirectoryNames =
					bundle.file
						.BlockAndDirInfo
						.DirectoryInfos
						.Select(d => d.Name)
						.ToList();

				if (finalCompression !=
					originalCompression)
				{
					throw new InvalidDataException(
						$"Compression changed: " +
						$"original={originalCompression}, " +
						$"final={finalCompression}");
				}

				if (!originalDirectoryNames.SequenceEqual(
						finalDirectoryNames,
						StringComparer.Ordinal))
				{
					throw new InvalidDataException(
						"Bundle directory entry names/order " +
						"changed after repack.");
				}

				DebugStr(
					$"[CHECK] Final compression={finalCompression}; " +
					$"directory layout identical " +
					$"({finalDirectoryNames.Count} entries).");

				// ====================================================
				// FIND ASSETS FILE
				// ====================================================

				int fileIndex =
					bundle.file.GetFileIndex(
						assetfileName);

				if (fileIndex < 0)
				{
					throw new InvalidDataException(
						"Expected assets file entry is missing: " +
						assetfileName);
				}

				AssetsFileInstance inst =
					am.LoadAssetsFileFromBundle(
						bundle,
						fileIndex,
						true);

				if (inst == null)
				{
					throw new InvalidDataException(
						"Expected assets file could not be reopened: " +
						assetfileName);
				}

				// ====================================================
				// AFTER SNAPSHOT
				// ====================================================

				AssetsFileSnapshot afterSnapshot =
					CaptureAssetsFileSnapshot(
						am,
						inst,
						assetfileName);

				DebugStr(
					$"[CHECK] AFTER assets '{assetfileName}' " +
					$"SHA256={afterSnapshot.Sha256} " +
					$"serializedLength={afterSnapshot.SerializedLength} " +
					$"assets={afterSnapshot.Assets.Count}");

				// ====================================================
				// ASSET COUNT
				// ====================================================

				if (afterSnapshot.Assets.Count !=
					beforeSnapshot.Assets.Count)
				{
					throw new InvalidDataException(
						$"Asset count changed: " +
						$"before={beforeSnapshot.Assets.Count} " +
						$"after={afterSnapshot.Assets.Count}");
				}

				// ====================================================
				// VERIFY ALL NON-TARGET ASSETS
				// ====================================================

				foreach (var before
					in beforeSnapshot.Assets)
				{
					var after =
						afterSnapshot.Assets.FirstOrDefault(
							a =>
								a.PathId ==
								before.PathId);

					if (after == null)
					{
						throw new InvalidDataException(
							"PathID disappeared after rewrite: " +
							before.PathId);
					}

					if (after.TypeId !=
						before.TypeId)
					{
						throw new InvalidDataException(
							$"TypeID changed for PID " +
							$"{before.PathId}: " +
							$"{before.TypeId}->" +
							$"{after.TypeId}");
					}

					if (before.TypeId == 114 &&
						after.MonoScriptIndex !=
							before.MonoScriptIndex)
					{
						throw new InvalidDataException(
							$"MonoScriptIndex changed for PID " +
							$"{before.PathId}: " +
							$"{before.MonoScriptIndex}->" +
							$"{after.MonoScriptIndex}");
					}

					if (before.PathId !=
						targetPathId)
					{
						if (!string.Equals(
							before.SerializedSha256,
							after.SerializedSha256,
							StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException(
								$"UNEXPECTED ASSET CHANGE: " +
								$"PID={before.PathId} " +
								$"name='{before.Name}' " +
								$"SHA " +
								$"{before.SerializedSha256}->" +
								$"{after.SerializedSha256}");
						}
					}
				}

				// ====================================================
				// TARGET BEFORE
				// ====================================================

				var targetBefore =
					beforeSnapshot.Assets.FirstOrDefault(
						a =>
							a.PathId ==
							targetPathId);

				if (targetBefore == null)
				{
					throw new InvalidDataException(
						"Target PathID was not present in " +
						"the original snapshot: " +
						targetPathId);
				}

				// ====================================================
				// TARGET AFTER
				// ====================================================

				var targetInfo =
					inst.file.AssetInfos.FirstOrDefault(
						a =>
							a.PathId ==
							targetPathId);

				if (targetInfo == null)
				{
					throw new InvalidDataException(
						"Target PathID missing after repack: " +
						targetPathId);
				}

				// ====================================================
				// TARGET TYPE
				// ====================================================

				if (targetInfo.TypeId !=
					expectedTargetTypeId)
				{
					throw new InvalidDataException(
						$"Target TypeID changed: " +
						$"original={expectedTargetTypeId}, " +
						$"final={targetInfo.TypeId}");
				}

				DebugStr(
					$"[CHECK] Target type preserved: " +
					$"PID={targetInfo.PathId}, " +
					$"TypeID={targetInfo.TypeId}");

				// ====================================================
				// MONOSCRIPT INDEX
				// ====================================================

				ushort finalMonoId =
					inst.file.GetScriptIndex(
						targetInfo);

				if (expectedTargetTypeId == 114)
				{
					if (finalMonoId ==
						0xFFFF)
					{
						throw new InvalidDataException(
							"Target MonoBehaviour lost " +
							"its MonoScript index.");
					}

					DebugStr(
						$"[CHECK] Target MonoScriptIndex=" +
						$"{finalMonoId} " +
						$"(0x{finalMonoId:X4})");

					if (finalMonoId !=
						targetBefore.MonoScriptIndex)
					{
						throw new InvalidDataException(
							$"Target MonoScriptIndex changed: " +
							$"original={targetBefore.MonoScriptIndex}, " +
							$"final={finalMonoId}");
					}
				}
				else
				{
					DebugStr(
						$"[CHECK] Non-MonoBehaviour target; " +
						$"MonoScriptIndex={finalMonoId} " +
						$"(0x{finalMonoId:X4}) accepted.");
				}

				// ====================================================
				// RAW MONOBEHAVIOUR TEXT
				// ====================================================

				bool isPng =
					string.Equals(
						fileKind,
						"PNG",
						StringComparison.OrdinalIgnoreCase);

				bool rawTextKind =
					!isPng &&
					IsRawMonoBehaviourTextKind(
						fileKind);

				// ====================================================
				// TARGET BASEFIELD
				// ====================================================

				AssetsTools.NET.AssetTypeValueField targetField =
					null;

				bool needsBaseField =
					!rawTextKind;

				if (needsBaseField)
				{
					try
					{
						targetField =
							am.GetBaseField(
								inst,
								targetInfo);
					}
					catch (Exception ex)
					{
						throw new InvalidDataException(
							$"Could not obtain final target BaseField " +
							$"for kind '{fileKind}'.",
							ex);
					}

					if (targetField == null ||
						targetField.IsDummy)
					{
						throw new InvalidDataException(
							$"Final target BaseField is null/dummy " +
							$"for fileKind='{fileKind}'. " +
							"This mode requires a usable TypeTree.");
					}
				}

				// ====================================================
				// READ FINAL TARGET PAYLOAD
				// ====================================================

				byte[] finalTargetData;

				if (rawTextKind)
				{
					finalTargetData =
						ReadRawAssetBytes(
							inst,
							targetInfo);
				}
				else
				{
					finalTargetData =
						targetField.WriteToByteArray();
				}

				if (finalTargetData == null ||
					finalTargetData.Length == 0)
				{
					throw new InvalidDataException(
						"Final target payload is null or empty.");
				}

				if (expectedTargetData == null ||
					expectedTargetData.Length == 0)
				{
					throw new InvalidDataException(
						"Expected target replacement payload " +
						"is null or empty.");
				}

				string finalTargetSha =
					Sha256Hex(
						finalTargetData);

				string expectedTargetSha =
					Sha256Hex(
						expectedTargetData);

				bool targetChanged =
					!string.Equals(
						targetBefore.SerializedSha256,
						finalTargetSha,
						StringComparison.OrdinalIgnoreCase);

				DebugStr(
					$"[CHECK] TARGET CHANGE: " +
					$"changed={targetChanged}, " +
					$"before={targetBefore.SerializedSha256}, " +
					$"after={finalTargetSha}");

				DebugStr(
					$"[CHECK] Target payload SHA " +
					$"expected={expectedTargetSha} " +
					$"actual={finalTargetSha}, " +
					$"bytes expected={expectedTargetData.Length} " +
					$"actual={finalTargetData.Length}");

				// ====================================================
				// EXACT TARGET PAYLOAD VALIDATION
				// ====================================================

				if (finalTargetData.Length !=
					expectedTargetData.Length)
				{
					throw new InvalidDataException(
						$"Final target payload length mismatch: " +
						$"expected={expectedTargetData.Length}, " +
						$"actual={finalTargetData.Length}");
				}

				if (!string.Equals(
					finalTargetSha,
					expectedTargetSha,
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						"Final target payload does not match " +
						"the in-memory replacement payload.");
				}

				DebugStr(
					"[CHECK] Final target payload matches " +
					"the intended replacement data.");

				// ====================================================
				// TXT VALIDATION
				// ====================================================

				if (isTextReplacement)
				{
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
							"[CHECK] TypeID=49 TextAsset: " +
							"generic BaseField scalar validation skipped. " +
							"Exact serialized payload validation PASSED.");

						return;
					}

					// ====================================================
					// MONOBEHAVIOUR
					// ====================================================

					if (expectedTargetTypeId == 114)
					{
						if (string.Equals(
							fileKind,
							"MONOBEHAVIOUR_TEXT",
							StringComparison.OrdinalIgnoreCase))
						{
							DebugStr(
								"[CHECK] MONOBEHAVIOUR_TEXT: " +
								"RAW m_text payload validation PASSED. " +
								"No scalar validation requested.");

							return;
						}

						if (string.Equals(
							fileKind,
							"MONOBEHAVIOUR_TEXT_CHECKED",
							StringComparison.OrdinalIgnoreCase))
						{
							if (targetField == null ||
								targetField.IsDummy)
							{
								throw new InvalidDataException(
									"MONOBEHAVIOUR_TEXT_CHECKED requires " +
									"a valid BaseField.");
							}

							string checkedMText =
								ReadDumpMText(
									dumpPath);

							AssetsTools.NET.AssetTypeValueField checkedTextField;

							try
							{
								checkedTextField =
									targetField["m_text"];
							}
							catch (Exception ex)
							{
								throw new InvalidDataException(
									"MONOBEHAVIOUR_TEXT_CHECKED could not " +
									"access m_text.",
									ex);
							}

							if (checkedTextField == null ||
								checkedTextField.IsDummy)
							{
								throw new InvalidDataException(
									"MONOBEHAVIOUR_TEXT_CHECKED found a " +
									"null/dummy m_text field.");
							}

							checkedTextField.AsString =
								checkedMText;

							DebugStr(
								$"[CHECK] MONOBEHAVIOUR_TEXT_CHECKED: " +
								$"running scalar validation with " +
								$"m_text length={checkedMText.Length}");

							ValidateDumpAgainstBaseField(
								dumpPath,
								targetField);

							DebugStr(
								"[CHECK] MONOBEHAVIOUR_TEXT_CHECKED: " +
								"m_text + scalar validation PASSED.");

							return;
						}

						if (string.Equals(
							fileKind,
							"MONOBEHAVIOUR_FONT",
							StringComparison.OrdinalIgnoreCase))
						{
							DebugStr(
								"[CHECK] MONOBEHAVIOUR_FONT: " +
								"full payload validation PASSED. " +
								"No scalar validation requested.");

							return;
						}

						if (string.Equals(
							fileKind,
							"MONOBEHAVIOUR_FONT_CHECKED",
							StringComparison.OrdinalIgnoreCase))
						{
							ValidateDumpAgainstBaseField(
								dumpPath,
								targetField);

							DebugStr(
								"[CHECK] MONOBEHAVIOUR_FONT_CHECKED: " +
								"full payload + scalar validation PASSED.");

							return;
						}

						if (string.Equals(
							fileKind,
							"MONOBEHAVIOUR_FULL",
							StringComparison.OrdinalIgnoreCase))
						{
							DebugStr(
								"[CHECK] MONOBEHAVIOUR_FULL: " +
								"full payload validation PASSED. " +
								"No scalar validation requested.");

							return;
						}

						if (string.Equals(
							fileKind,
							"MONOBEHAVIOUR_FULL_CHECKED",
							StringComparison.OrdinalIgnoreCase))
						{
							ValidateDumpAgainstBaseField(
								dumpPath,
								targetField);

							DebugStr(
								"[CHECK] MONOBEHAVIOUR_FULL_CHECKED: " +
								"full payload + scalar validation PASSED.");

							return;
						}

						throw new InvalidDataException(
							$"Unknown MonoBehaviour fileKind " +
							$"'{fileKind}'.");
					}

					// ====================================================
					// RECTTRANSFORM
					// ====================================================

					if (expectedTargetTypeId == 224)
					{
						if (!IsRectTransformKind(
							fileKind))
						{
							throw new InvalidDataException(
								$"TypeID=224 requires a RectTransform " +
								$"fileKind, but received '{fileKind}'.");
						}

						if (targetField == null ||
							targetField.IsDummy)
						{
							throw new InvalidDataException(
								"RECTTRANSFORM validation requires " +
								"a valid BaseField.");
						}

						ValidateDumpAgainstBaseField(
							dumpPath,
							targetField);

						DebugStr(
							"[CHECK] RECTTRANSFORM_FULL_CHECKED: " +
							"full serialized RectTransform validation PASSED.");

						return;
					}

					// ====================================================
					// SPRITE
					// ====================================================

					if (expectedTargetTypeId == 213)
					{
						if (!IsSpriteKind(
							fileKind))
						{
							throw new InvalidDataException(
								$"TypeID=213 requires a Sprite " +
								$"fileKind, but received '{fileKind}'.");
						}

						if (targetField == null ||
							targetField.IsDummy)
						{
							throw new InvalidDataException(
								"SPRITE validation requires " +
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
						ValidateDumpAgainstBaseField(
							dumpPath,
							targetField);

						DebugStr(
							$"[CHECK] {fileKind}: " +
							"full serialized Sprite validation PASSED.");

						return;
					}

					throw new InvalidDataException(
						$"TXT replacement requested for unsupported " +
						$"TypeID={expectedTargetTypeId}. " +
						$"Supported TXT types are " +
						$"TextAsset (49), " +
						$"MonoBehaviour (114), " +
						$"RectTransform (224), " +
						$"Sprite (213).");
				}

				// ====================================================
				// NON-TXT
				// ====================================================

				DebugStr(
					"[CHECK] Generic asset replacement payload " +
					"validation PASSED.");
			}
			finally
			{
				try
				{
					am.UnloadAllAssetsFiles(
						true);
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

		private static string TryGetName(
			AssetsTools.NET.AssetTypeValueField field)
		{
			try
			{
				if (field == null ||
					field.IsDummy)
				{
					return "<dummy>";
				}

				var name =
					field["m_Name"];

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

		private static string Sha256File(
			string path)
		{
			using var sha =
				SHA256.Create();

			using var stream =
				File.OpenRead(
					path);

			return Convert.ToHexString(
				sha.ComputeHash(
					stream));
		}

		private static string Sha256Hex(
			byte[] data)
		{
			using var sha =
				SHA256.Create();

			return Convert.ToHexString(
				sha.ComputeHash(
					data ??
					Array.Empty<byte>()));
		}
	}
}