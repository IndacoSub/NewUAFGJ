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
			string fileKind)
		{
			LogPhase(
				$"Asset-file start: asset='{asset}', " +
				$"input='{input_file}', " +
				$"pathId='{specific_pathid}', " +
				$"kind='{fileKind}'.");

			AssetsManager am =
				new AssetsManager();

			AssetsFileInstance assetInst =
				null;

			string tempAssetPath =
				asset +
				".uafgj_stage_" +
				Guid.NewGuid().ToString("N") +
				".tmp";

			CleanupStaleAssetStages(asset);
			DeleteFileIfExists(asset + "_temp");
			DeleteFileIfExists(asset + ".uafgj_tmp");

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
                     * FindTXTFile now supports:
                     *
                     * TypeID 49  = TextAsset
                     * TypeID 114 = MonoBehaviour
                     * TypeID 224 = RectTransform
                     * TypeID 213 = Sprite
                     */
					if (!FindTXTFile(
						input_file,
						ref assetInst,
						ref afie,
						ref atvf,
						ref am,
						asset,
						assetInst.name,
						specific_pathid,
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
					if (!FindPNGFile(
						input_file,
						ref afie,
						ref assetInst,
						ref atvf,
						ref am,
						asset,
						assetInst.name,
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
						DisplayStr(
							"Could not import PNG!");

						return;
					}

					rawReplacementData =
						atvf.WriteToByteArray();
				}

				DebugStr(
					"[ASSET] Import phase returned; " +
					"validating replacement state before write.");

				if (afie == null ||
					rawReplacementData == null ||
					rawReplacementData.Length == 0)
				{
					DisplayStr(
						"Invalid replacement state.");

					return;
				}

				ushort monoId =
					assetInst.file.GetScriptIndex(
						afie);

				DebugStr(
					$"[ASSET] Resolved MonoScript index: " +
					$"{monoId} (0x{monoId:X4}) " +
					$"for PID={afie.PathId}");

				// AssetsTools.NET 3.x:
				// attach replacement directly to AssetFileInfo.
				afie.SetNewData(
					rawReplacementData);

				string fakeName =
					tempAssetPath;

				DebugStr(
					$"[ASSET] Writing replacement to staging file " +
					$"'{fakeName}'.");

				using (var stream =
					new FileStream(
						fakeName,
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

				am.UnloadAllAssetsFiles(
					true);

				DebugStr(
					"[ASSET] Handles released; replacing original file.");

				ReplaceFileWithRetry(
					fakeName,
					asset);

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

				DeleteFileIfExists(
					tempAssetPath);
			}
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

				if (string.IsNullOrEmpty(directory) ||
					string.IsNullOrEmpty(fileName) ||
					!Directory.Exists(directory))
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

				// ========================================================
				// FIRST 64 RAW BYTES
				// ========================================================

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

				// ========================================================
				// FIRST 64 BASEFIELD BYTES
				// ========================================================

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