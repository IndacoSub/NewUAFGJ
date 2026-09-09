using AssetsTools.NET.Extra;
using AssetsTools.NET;
using System;
using System.IO;
using System.Collections.Generic;

namespace UAFGJ
{
	partial class Program
	{
		static private void HandleAsset(
			string asset,
			string input_file,
			string specific_pathid,
			string specific_fileid,
			string fileKind)
		{
			LogPhase(
				$"Asset-file start: asset='{asset}', " +
				$"input='{input_file}', " +
				$"pathId='{specific_pathid}', " +
				$"fileId='{specific_fileid}', " +
				$"kind='{fileKind}'.");

			AssetsManager am =
				new AssetsManager();

			AssetsFileInstance assetInst =
				null;

			string assetfile_name =
				asset;

			string tempAssetPath =
				null;

			CleanupStaleAssetStages(
				asset);

			DeleteFileIfExists(
				asset + "_temp");

			DeleteFileIfExists(
				asset + ".uafgj_tmp");

			try
			{
				DebugStr(
					"[ASSET] Loading AssetsFile.");

				RuntimeSetup.Configure(
					am,
					asset);

				assetInst =
					am.LoadAssetsFile(
						asset,
						true);

				if (assetInst == null)
				{
					DisplayStr(
						"Could not load assets file: " +
						asset);

					return;
				}

				EnsureClassDatabaseIfNeeded(
					am,
					assetInst);

				AssetsTools.NET.AssetTypeValueField atvf =
					null;

				AssetFileInfo afie =
					null;

				byte[] rawReplacementData =
					null;

				byte[] originalSerializedData =
					null;

				bool isPng =
					string.Equals(
						Path.GetExtension(input_file),
						".png",
						StringComparison.OrdinalIgnoreCase);

				DebugStr(
					$"[ASSET] Replacement type: " +
					$"{(isPng ? "PNG" : "TXT")}.");

				if (!isPng)
				{
					/*
					 * FindTXTFile supports:
					 *
					 * TypeID 49  = TextAsset
					 * TypeID 114 = MonoBehaviour
					 * TypeID 224 = RectTransform
					 * TypeID 213 = Sprite
					 *
					 * assetfile_name is updated to the actual
					 * serialized file containing the selected target.
					 */
					if (!FindTXTFile(
						input_file,
						ref assetInst,
						ref afie,
						ref atvf,
						ref am,
						ref asset,
						ref assetfile_name,
						specific_pathid,
						specific_fileid,
						fileKind,
						out rawReplacementData,
						out originalSerializedData))
					{
						DisplayStr(
							"[ASSET] Failed to replace TXT/serialized asset.");

						return;
					}
				}
				else
				{
					/*
					 * FindPNGFile can also resolve an external
					 * serialized file when FileID is specified.
					 */
					if (!FindPNGFile(
						input_file,
						ref afie,
						ref assetInst,
						ref atvf,
						ref am,
						ref asset,
						ref assetfile_name,
						specific_pathid,
						specific_fileid,
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
						DisplayStr(
							"Could not import PNG!");

						return;
					}

					rawReplacementData =
						atvf.WriteToByteArray();
				}

				DebugStr(
					$"[ASSET] Import phase returned; " +
					$"resolved serialized file='{assetfile_name}'.");

				if (assetInst == null)
				{
					DisplayStr(
						"Invalid replacement state: asset instance is null.");

					return;
				}

				if (afie == null)
				{
					DisplayStr(
						"Invalid replacement state: target AssetFileInfo is null.");

					return;
				}

				if (rawReplacementData == null ||
					rawReplacementData.Length == 0)
				{
					DisplayStr(
						"Invalid replacement state: replacement data is empty.");

					return;
				}

				if (string.IsNullOrWhiteSpace(
					assetfile_name))
				{
					DisplayStr(
						"Invalid replacement state: resolved asset file name is empty.");

					return;
				}

				/*
				 * The target file may be different from the file
				 * originally passed on the command line when FileID
				 * resolves an external asset.
				 */
				assetfile_name =
					Path.GetFullPath(
						assetfile_name);

				DebugStr(
					$"[ASSET] Final target serialized file: " +
					$"'{assetfile_name}'.");

				if (!File.Exists(
					assetfile_name))
				{
					DisplayStr(
						"[ASSET] Final target serialized file does not exist:");

					DisplayStr(
						assetfile_name);

					return;
				}

				/*
				 * Clean stale staging files belonging to the actual
				 * resolved target file as well.
				 */
				CleanupStaleAssetStages(
					assetfile_name);

				DeleteFileIfExists(
					assetfile_name + "_temp");

				DeleteFileIfExists(
					assetfile_name + ".uafgj_tmp");

				/*
				 * Staging file MUST belong to the actual serialized
				 * file that contains the selected AssetFileInfo.
				 */
				tempAssetPath =
					assetfile_name +
					".uafgj_stage_" +
					Guid.NewGuid().ToString("N") +
					".tmp";

				ushort monoId =
					assetInst.file.GetScriptIndex(
						afie);

				DebugStr(
					$"[ASSET] Resolved MonoScript index: " +
					$"{monoId} (0x{monoId:X4}) " +
					$"for PID={afie.PathId}");

				/*
				 * AssetsTools.NET 3.x:
				 * attach replacement directly to AssetFileInfo.
				 */
				afie.SetNewData(
					rawReplacementData);

				DebugStr(
					$"[ASSET] Writing replacement to staging file " +
					$"'{tempAssetPath}'.");

				using (var stream =
					new FileStream(
						tempAssetPath,
						FileMode.Create,
						FileAccess.Write,
						FileShare.None))
				using (var writer =
					new AssetsFileWriter(
						stream))
				{
					assetInst.file.Write(
						writer);
				}

				DebugStr(
					"[ASSET] Staging write completed; " +
					"releasing AssetsManager handles.");

				/*
				 * Release all AssetsTools.NET file handles before
				 * touching the original serialized file.
				 */
				am.UnloadAllAssetsFiles(
					true);

				DebugStr(
					"[ASSET] AssetsManager handles released.");

				/*
				 * Give Windows a chance to release any transient
				 * handle before ReplaceFileWithRetry starts.
				 */
				if (!WaitForFileUnlocked(
					assetfile_name))
				{
					throw new IOException(
						$"Destination file remains locked before replace: " +
						$"'{assetfile_name}'.");
				}

				DebugStr(
					"[ASSET] Destination file is unlocked; " +
					"replacing original file.");

				ReplaceFileWithRetry(
					tempAssetPath,
					assetfile_name);

				DisplayStr(
					"Successfully replaced asset!");
			}
			catch (Exception ex)
			{
				Environment.ExitCode =
					1;

				DisplayStr(
					"[FATAL] Assets file handling failed: " +
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
					am.UnloadAllAssetsFiles(
						true);
				}
				catch
				{
				}

				if (!string.IsNullOrEmpty(
					tempAssetPath))
				{
					DeleteFileIfExists(
						tempAssetPath);
				}
			}
		}


		private static bool WaitForFileUnlocked(
			string filePath)
		{
			const int maxAttempts =
				20;

			const int delayMs =
				100;

			if (!File.Exists(
				filePath))
			{
				throw new FileNotFoundException(
					"Destination file does not exist.",
					filePath);
			}

			for (int attempt = 1;
				 attempt <= maxAttempts;
				 attempt++)
			{
				try
				{
					using (FileStream stream =
						new FileStream(
							filePath,
							FileMode.Open,
							FileAccess.ReadWrite,
							FileShare.None))
					{
					}

					return true;
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}

				if (attempt < maxAttempts)
				{
					DebugStr(
						$"[SAVE] Destination still locked; " +
						$"unlock check {attempt}/{maxAttempts - 1}...");

					System.Threading.Thread.Sleep(
						delayMs);
				}
			}

			return false;
		}


		private static void CleanupStaleAssetStages(
			string assetPath)
		{
			try
			{
				string directory =
					Path.GetDirectoryName(
						assetPath);

				string fileName =
					Path.GetFileName(
						assetPath);

				if (string.IsNullOrEmpty(
					directory) ||
					string.IsNullOrEmpty(
					fileName) ||
					!Directory.Exists(
					directory))
				{
					return;
				}

				string pattern =
					fileName +
					".uafgj_stage_*.tmp";

				foreach (string path in
					Directory.GetFiles(
						directory,
						pattern))
				{
					DeleteFileIfExists(
						path);
				}
			}
			catch (Exception ex)
			{
				DebugStr(
					"[CLEANUP] Could not scan for stale asset staging files: " +
					ex.GetType().Name +
					": " +
					ex.Message);
			}
		}


		private static void DebugRawVsBaseFieldSprite(
			AssetsFileInstance assetInst,
			AssetFileInfo afie,
			AssetsTools.NET.AssetTypeValueField baseField)
		{
			try
			{
				if (assetInst == null ||
					afie == null ||
					baseField == null ||
					baseField.IsDummy)
				{
					DebugStr(
						"[SPRITE] RAW/BaseField comparison skipped: " +
						"invalid state.");

					return;
				}

				/*
				 * IMPORTANT:
				 *
				 * ReadRawAssetBytes already exists in MonoBehaviour.cs.
				 * Reuse that implementation instead of defining another one.
				 */
				byte[] rawAsset =
					ReadRawAssetBytes(
						assetInst,
						afie);

				byte[] baseFieldData =
					baseField.WriteToByteArray();

				DebugFindFloatPatterns(
					"SPRITE BASEFIELD",
					baseFieldData);

				DebugPayloadWindow(
					"ORIGINAL textureRect",
					baseFieldData,
					3176);

				DebugStr(
					$"[SPRITE] RAW asset comparison: " +
					$"rawBytes={rawAsset.Length}, " +
					$"rawSHA={Sha256Hex(rawAsset)}, " +
					$"baseFieldBytes={baseFieldData.Length}, " +
					$"baseFieldSHA={Sha256Hex(baseFieldData)}");

				int compareLength =
					Math.Min(
						rawAsset.Length,
						baseFieldData.Length);

				int firstDifference =
					-1;

				for (int i = 0;
					 i < compareLength;
					 i++)
				{
					if (rawAsset[i] != baseFieldData[i])
					{
						firstDifference =
							i;

						break;
					}
				}

				if (firstDifference >= 0)
				{
					DebugStr(
						$"[SPRITE] RAW/BaseField first byte difference: " +
						$"offset={firstDifference}, " +
						$"raw=0x{rawAsset[firstDifference]:X2}, " +
						$"baseField=0x{baseFieldData[firstDifference]:X2}");
				}
				else if (rawAsset.Length !=
						 baseFieldData.Length)
				{
					DebugStr(
						$"[SPRITE] RAW/BaseField contents match for first " +
						$"{compareLength} bytes, but lengths differ.");
				}
				else
				{
					DebugStr(
						"[SPRITE] RAW asset and BaseField serialization are byte-identical.");
				}

				int rawPreviewLength =
					Math.Min(
						64,
						rawAsset.Length);

				if (rawPreviewLength > 0)
				{
					DebugStr(
						$"[SPRITE] RAW asset first {rawPreviewLength} bytes=" +
						Convert.ToHexString(
							rawAsset,
							0,
							rawPreviewLength));
				}

				int basePreviewLength =
					Math.Min(
						64,
						baseFieldData.Length);

				if (basePreviewLength > 0)
				{
					DebugStr(
						$"[SPRITE] BaseField first {basePreviewLength} bytes=" +
						Convert.ToHexString(
							baseFieldData,
							0,
							basePreviewLength));
				}
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[SPRITE] RAW/BaseField comparison failed: " +
					$"{ex.GetType().Name}: {ex.Message}");

				DebugStr(
					ex.ToString());
			}
		}
	}
}