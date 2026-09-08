using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;

namespace UAFGJ
{
	partial class Program
	{
		// ============================================================
		// GAMEOBJECT FULL
		//
		// GAMEOBJECT_FULL
		//   -> importa l'intero dump del GameObject
		//   -> ricostruisce/sincronizza gli Array presenti nel dump
		//   -> applica gli scalari tramite il mapper strutturale comune
		//
		// GAMEOBJECT_FULL_CHECKED
		//   -> stesso comportamento
		//   -> richiede corrispondenza completa degli scalari
		// ============================================================

		private static bool ImportGameObjectFull(
			string inputFile,
			AssetsManager am,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			string assetName,
			out AssetTypeValueField modifiedBaseField,
			out byte[] replacementData,
			out byte[] originalSerializedData)
		{
			return ImportGameObjectFullInternal(
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

		private static bool ImportGameObjectFullChecked(
			string inputFile,
			AssetsManager am,
			AssetFileInfo afie,
			AssetsFileInstance assetInst,
			string assetName,
			out AssetTypeValueField modifiedBaseField,
			out byte[] replacementData,
			out byte[] originalSerializedData)
		{
			return ImportGameObjectFullInternal(
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

		private static bool ImportGameObjectFullInternal(
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

			// --------------------------------------------------------
			// ARGUMENT VALIDATION
			// --------------------------------------------------------

			if (assetInst == null)
			{
				throw new InvalidOperationException(
					"GameObject import: assetInst is null.");
			}

			if (am == null)
			{
				throw new InvalidOperationException(
					"GameObject import: AssetsManager is null.");
			}

			if (afie == null)
			{
				throw new InvalidOperationException(
					"GameObject import: AssetFileInfo is null.");
			}

			if (!File.Exists(inputFile))
			{
				throw new FileNotFoundException(
					"GameObject dump not found.",
					inputFile);
			}

			// --------------------------------------------------------
			// GAMEOBJECT TYPE
			// --------------------------------------------------------

			if (afie.TypeId != 1)
			{
				throw new InvalidDataException(
					$"GameObject importer received TypeID={afie.TypeId}, " +
					"expected TypeID=1.");
			}

			LogPhase(
				$"GAMEOBJECT import starting PID={afie.PathId}, " +
				$"checked={checkedMode}.");

			DebugStr(
				$"[GAMEOBJECT] Loading BaseField " +
				$"PID={afie.PathId}, asset='{assetName}'.");

			// --------------------------------------------------------
			// LOAD BASE FIELD
			// --------------------------------------------------------

			AssetTypeValueField baseField =
				am.GetBaseField(
					assetInst,
					afie);

			if (baseField == null ||
				baseField.IsDummy)
			{
				throw new InvalidDataException(
					$"AssetsTools.NET returned a null/dummy " +
					$"GameObject BaseField for PID={afie.PathId}.");
			}

			// --------------------------------------------------------
			// IMPORTANT:
			//
			// AssetsTools.NET può esporre il root del BaseField
			// come "Base" anche quando TypeID=1 è GameObject.
			//
			// Il dump UABEA mostra invece:
			//
			//     0 GameObject Base
			//
			// Quindi NON bisogna verificare:
			//
			//     TemplateField.Name == "GameObject"
			//
			// Il TypeID=1 già identifica il GameObject.
			// --------------------------------------------------------

			DebugStr(
				$"[GAMEOBJECT] BaseField loaded. " +
				$"TypeID={afie.TypeId}, " +
				$"root='{baseField.TemplateField?.Name ?? "<null>"}'.");

			// --------------------------------------------------------
			// ORIGINAL SERIALIZED DATA
			// --------------------------------------------------------

			originalSerializedData =
				baseField.WriteToByteArray();

			if (originalSerializedData == null ||
				originalSerializedData.Length == 0)
			{
				throw new InvalidDataException(
					$"Original GameObject PID={afie.PathId} " +
					"serialized to zero bytes.");
			}

			DebugStr(
				$"[GAMEOBJECT] Original serialized size=" +
				$"{originalSerializedData.Length} bytes.");

			// --------------------------------------------------------
			// STRUCTURAL ARRAYS
			//
			// In particolare:
			//
			//   vector m_Component
			//     Array Array (N items)
			//
			// Prima riallineiamo la struttura dell'array.
			// Solo dopo applichiamo i valori scalari.
			// --------------------------------------------------------

			List<DumpScalar> dumpScalars =
				ReadDumpScalars(
					inputFile);

			DebugStr(
				$"[GAMEOBJECT] Dump scalars={dumpScalars.Count}. " +
				"Synchronizing arrays.");

			SynchronizeDumpArrayStructure(
				inputFile,
				baseField,
				dumpScalars,
				true);

			// --------------------------------------------------------
			// STRUCTURAL SCALAR MAPPING
			// --------------------------------------------------------

			List<DumpTargetMatch> matches =
				BuildDumpTargetMatches(
					inputFile,
					baseField,
					checkedMode);

			if (matches == null)
			{
				throw new InvalidDataException(
					"GAMEOBJECT mapping returned null.");
			}

			DebugStr(
				$"[GAMEOBJECT] Structural mapping passed: " +
				$"{matches.Count} scalar fields.");

			// --------------------------------------------------------
			// APPLY DUMP VALUES
			// --------------------------------------------------------

			foreach (DumpTargetMatch match in matches)
			{
				if (match == null)
				{
					throw new InvalidDataException(
						"GAMEOBJECT mapping produced a null match.");
				}

				if (match.Dump == null)
				{
					throw new InvalidDataException(
						"GAMEOBJECT mapping produced a match " +
						"with null dump value.");
				}

				if (match.Target == null)
				{
					throw new InvalidDataException(
						"GAMEOBJECT mapping produced a match " +
						"with null target.");
				}

				if (match.Target.Field == null)
				{
					throw new InvalidDataException(
						"GAMEOBJECT mapping produced a target " +
						"with null Field.");
				}

				ApplyDumpValue(
					match.Target.Field,
					match.Dump);
			}

			// --------------------------------------------------------
			// SERIALIZE MODIFIED GAMEOBJECT
			// --------------------------------------------------------

			replacementData =
				baseField.WriteToByteArray();

			if (replacementData == null ||
				replacementData.Length == 0)
			{
				throw new InvalidDataException(
					"Modified GameObject serialized to zero bytes.");
			}

			DebugStr(
				$"[GAMEOBJECT] Serialized replacement: " +
				$"{replacementData.Length} bytes.");

			// --------------------------------------------------------
			// RETURN
			// --------------------------------------------------------

			modifiedBaseField =
				baseField;

			return true;
		}
	}
}
