using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace UAFGJ
{
	partial class Program
	{
		// ============================================================
		// LOGGING
		// ============================================================

		private static void LogPhase(string message)
		{
			DebugStr("[PHASE] " + message);
		}

		private static void LogException(
			string context,
			Exception ex)
		{
			DebugStr(
				$"[ERROR] {context}: {ex.GetType().FullName}: {ex.Message}");

			DebugStr(ex.ToString());
		}

		private static void LogFileState(
			string label,
			string path)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(path))
				{
					DebugStr($"{label}: path=<empty>");
					return;
				}

				if (!File.Exists(path))
				{
					DebugStr(
						$"{label}: MISSING path='{path}'");

					return;
				}

				var info =
					new FileInfo(path);

				DebugStr(
					$"{label}: path='{path}', " +
					$"length={info.Length}, " +
					$"lastWriteUtc={info.LastWriteTimeUtc:O}, " +
					$"readOnly={info.IsReadOnly}");

				DebugStr(
					$"{label}: SHA256={Sha256File(path)}");
			}
			catch (Exception ex)
			{
				DebugStr(
					$"[ERROR] Could not log file state '{path}': " +
					$"{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static bool StartsWithSpace(
			string str,
			string value)
		{
			return str.StartsWith(
				value + " ",
				StringComparison.Ordinal);
		}


		// ============================================================
		// DUMP STRING PARSING
		// ============================================================

		private static string UnescapeDumpString(
			string str)
		{
			StringBuilder sb =
				new StringBuilder(str.Length);

			bool escaping = false;

			foreach (char c in str)
			{
				if (!escaping && c == '\\')
				{
					escaping = true;
					continue;
				}

				if (escaping)
				{
					switch (c)
					{
						case '\\':
							sb.Append('\\');
							break;

						case 'r':
							sb.Append('\r');
							break;

						case 'n':
							sb.Append('\n');
							break;

						case 't':
							sb.Append('\t');
							break;

						case '"':
							sb.Append('"');
							break;

						default:
							sb.Append(c);
							break;
					}

					escaping = false;
				}
				else
				{
					sb.Append(c);
				}
			}

			if (escaping)
				sb.Append('\\');

			return sb.ToString();
		}

		private static int LeadingSpaces(
			string line)
		{
			int depth = 0;

			while (depth < line.Length &&
				   line[depth] == ' ')
			{
				depth++;
			}

			return depth;
		}

		private static string ParseDumpString(
			string valueStr)
		{
			int firstQuote =
				valueStr.IndexOf('"');

			int lastQuote =
				valueStr.LastIndexOf('"');

			if (firstQuote < 0 ||
				lastQuote <= firstQuote)
			{
				throw new FormatException(
					"String field does not contain a valid quoted value: " +
					valueStr);
			}

			return UnescapeDumpString(
				valueStr.Substring(
					firstQuote + 1,
					lastQuote - firstQuote - 1));
		}


		// ============================================================
		// NUMERIC PARSING
		// ============================================================

		private static int ParseInt32(
			string s)
		{
			return int.Parse(
				s,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture);
		}

		private static long ParseInt64(
			string s)
		{
			return long.Parse(
				s,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture);
		}

		private static uint ParseUInt32(
			string s)
		{
			return uint.Parse(
				s,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture);
		}

		private static ulong ParseUInt64(
			string s)
		{
			return ulong.Parse(
				s,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture);
		}

		private static string NormalizeNumericLiteral(
			string value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));

			value =
				value.Trim();

			if (value.Length == 0)
			{
				throw new FormatException(
					"Numeric literal is empty.");
			}

			bool hasComma =
				value.Contains(',');

			bool hasDot =
				value.Contains('.');

			if (hasComma && !hasDot)
			{
				return value.Replace(
					',',
					'.');
			}

			if (!hasComma && hasDot)
			{
				return value;
			}

			if (!hasComma && !hasDot)
			{
				return value;
			}

			int commaIndex =
				value.LastIndexOf(',');

			int dotIndex =
				value.LastIndexOf('.');

			if (commaIndex > dotIndex)
			{
				value =
					value.Replace(
						".",
						"");

				value =
					value.Replace(
						',',
						'.');

				return value;
			}

			value =
				value.Replace(
					",",
					"");

			return value;
		}

		private static float ParseSingle(
			string s)
		{
			string normalized =
				NormalizeNumericLiteral(s);

			return float.Parse(
				normalized,
				NumberStyles.Float,
				CultureInfo.InvariantCulture);
		}

		private static double ParseDouble(
			string s)
		{
			string normalized =
				NormalizeNumericLiteral(s);

			return double.Parse(
				normalized,
				NumberStyles.Float,
				CultureInfo.InvariantCulture);
		}


		// ============================================================
		// DUMP REPRESENTATION
		// ============================================================

		private sealed class DumpScalar
		{
			public int LineNumber;

			public string Type =
				"";

			public string FieldName =
				"";

			public string Value =
				"";

			public string Path =
				"";

			/*
			 * Explicit raw-buffer metadata.
			 *
			 * Used for Unity TypelessData / ByteArray dumps.
			 */
			public string RawBufferPath =
				"";

			public int RawBufferIndex =
				-1;
		}

		private sealed class ScalarFieldEntry
		{
			public string Path =
				"";

			public string CanonicalPath =
				"";

			public string Type =
				"";

			public string FieldName =
				"";

			public AssetTypeValueField Field =
				null;
		}

		private sealed class DumpTargetMatch
		{
			public DumpScalar Dump =
				null;

			public ScalarFieldEntry Target =
				null;
		}

		private sealed class DumpNode
		{
			public int Depth;

			public string Name =
				"";

			public string Type =
				"";

			public bool IsArray;

			public bool IsTypelessData;

			public int? ArrayIndex;
		}

		private sealed class DumpArrayInfo
		{
			public int LineNumber;

			public string Path =
				"";

			public int Count;
		}


		// ============================================================
		// PATH CANONICALIZATION
		// ============================================================

		private static string CanonicalizeStructuralPath(
			string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return "";

			path =
				path.Trim();

			path =
				path.Replace(
					"/Array[",
					"[");

			path =
				path.Replace(
					"/Array/",
					"/");

			while (path.Contains("//"))
			{
				path =
					path.Replace(
						"//",
						"/");
			}

			if (path.StartsWith("/",
				StringComparison.Ordinal))
			{
				path =
					path.Substring(1);
			}

			if (path.EndsWith("/",
				StringComparison.Ordinal))
			{
				path =
					path.Substring(
						0,
						path.Length - 1);
			}

			string[] rawSegments =
				path.Split(
					'/',
					StringSplitOptions.RemoveEmptyEntries);

			if (rawSegments.Length == 0)
				return "";

			var normalizedSegments =
				new List<string>(
					rawSegments.Length);

			for (int i = 0;
					 i < rawSegments.Length;
				 i++)
			{
				string segment =
					rawSegments[i];

				if (string.Equals(
					segment,
					"data",
					StringComparison.Ordinal) &&
					normalizedSegments.Count > 0 &&
					normalizedSegments[
						normalizedSegments.Count - 1]
						.EndsWith(
							"]",
							StringComparison.Ordinal))
				{
					continue;
				}

				if (segment.StartsWith(
					"data[",
					StringComparison.Ordinal) &&
					segment.EndsWith(
						"]",
						StringComparison.Ordinal) &&
					normalizedSegments.Count > 0 &&
					normalizedSegments[
						normalizedSegments.Count - 1]
						.EndsWith(
							"]",
							StringComparison.Ordinal))
				{
					string indexSuffix =
						segment.Substring(
							4);

					string previous =
						normalizedSegments[
							normalizedSegments.Count - 1];

					normalizedSegments[
						normalizedSegments.Count - 1] =
						previous +
						indexSuffix;

					continue;
				}

				normalizedSegments.Add(
					segment);
			}

			path =
				string.Join(
					"/",
					normalizedSegments);

			if (string.Equals(
				path,
				"Array",
				StringComparison.Ordinal))
			{
				return "";
			}

			return path;
		}


		// ============================================================
		// DUMP NODE PARSER
		// ============================================================

		private static string ParseDumpNodeName(
			string left,
			out string type)
		{
			left =
				left.Trim();

			if (string.IsNullOrEmpty(left))
			{
				type = "";
				return "";
			}

			/*
			 * Remove metadata suffix:
			 *
			 *   TypelessData m_DataSize (8232 items)
			 */
			int metadataParen =
				left.LastIndexOf(
					" (",
					StringComparison.Ordinal);

			if (metadataParen >= 0 &&
				left.EndsWith(
					" items)",
					StringComparison.OrdinalIgnoreCase))
			{
				left =
					left.Substring(
						0,
						metadataParen).Trim();
			}

			if (left.StartsWith(
				"Array Array",
				StringComparison.Ordinal))
			{
				type = "Array";
				return "Array";
			}

			string[] multiWordTypes =
			{
				"unsigned int",
				"signed int",
				"unsigned long long",
				"signed long long",
				"unsigned short",
				"signed short"
			};

			foreach (string knownType in multiWordTypes)
			{
				if (left.StartsWith(
					knownType + " ",
					StringComparison.Ordinal))
				{
					type = knownType;

					return left.Substring(
						knownType.Length).Trim();
				}
			}

			int split =
				left.LastIndexOf(' ');

			if (split <= 0 ||
				split >= left.Length - 1)
			{
				type = "";
				return left;
			}

			type =
				left.Substring(
					0,
					split).Trim();

			return left.Substring(
				split + 1).Trim();
		}

		private static bool TryParseDumpArrayCount(
			string left,
			out int count)
		{
			count = 0;

			if (string.IsNullOrWhiteSpace(left))
				return false;

			if (!left.StartsWith(
				"Array Array",
				StringComparison.Ordinal))
			{
				return false;
			}

			int openParen =
				left.IndexOf('(');

			int itemsIndex =
				left.IndexOf(
					" items",
					StringComparison.OrdinalIgnoreCase);

			if (openParen < 0 ||
				itemsIndex <= openParen)
			{
				return false;
			}

			string countText =
				left.Substring(
					openParen + 1,
					itemsIndex - openParen - 1).Trim();

			return int.TryParse(
				countText,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out count);
		}

		private static bool TryParseDumpTypelessDataCount(
			string left,
			out int count)
		{
			count = 0;

			if (string.IsNullOrWhiteSpace(left))
				return false;

			if (!left.StartsWith(
				"TypelessData ",
				StringComparison.Ordinal))
			{
				return false;
			}

			int openParen =
				left.LastIndexOf(
					" (",
					StringComparison.Ordinal);

			int itemsIndex =
				left.LastIndexOf(
					" items)",
					StringComparison.OrdinalIgnoreCase);

			if (openParen < 0 ||
				itemsIndex <= openParen)
			{
				return false;
			}

			string countText =
				left.Substring(
					openParen + 2,
					itemsIndex - (openParen + 2));

			return int.TryParse(
				countText,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out count);
		}


		// ============================================================
		// BUILD DUMP LOGICAL PATH
		// ============================================================

		private static string BuildDumpLogicalPath(
			List<DumpNode> stack,
			string scalarName)
		{
			var parts =
				new List<string>();

			for (int i = 0;
				 i < stack.Count;
				 i++)
			{
				DumpNode node =
					stack[i];

				if (node == null ||
					string.IsNullOrEmpty(node.Name))
				{
					continue;
				}

				if (string.Equals(
					node.Name,
					"<anonymous>",
					StringComparison.Ordinal))
				{
					continue;
				}

				if (node.IsArray ||
					node.IsTypelessData)
				{
					if (parts.Count > 0)
					{
						string previous =
							parts[parts.Count - 1];

						int index =
							node.ArrayIndex ?? 0;

						parts[parts.Count - 1] =
							previous +
							"[" +
							index +
							"]";
					}

					continue;
				}

				parts.Add(
					node.Name);
			}

			if (!string.IsNullOrEmpty(scalarName))
			{
				parts.Add(
					scalarName);
			}

			return CanonicalizeStructuralPath(
				string.Join(
					"/",
					parts));
		}


		// ============================================================
		// RAW TYPELESSDATA CONTEXT
		// ============================================================

		private static bool TryGetActiveRawBufferContext(
			List<DumpNode> stack,
			out string bufferPath,
			out int index)
		{
			bufferPath = "";
			index = -1;

			if (stack == null ||
				stack.Count == 0)
			{
				return false;
			}

			/*
			 * Find the nearest active TypelessData node.
			 *
			 * IMPORTANT:
			 *
			 * Do NOT treat TypelessData itself like a normal array while
			 * constructing the buffer path.
			 *
			 * We explicitly want:
			 *
			 *   m_RD/m_VertexData/m_DataSize
			 *
			 * NOT:
			 *
			 *   m_RD/m_VertexData[0]
			 *
			 * or any other indexed form.
			 */
			int typelessIndex =
				-1;

			DumpNode typelessNode =
				null;

			for (int i =
				stack.Count - 1;
				i >= 0;
				i--)
			{
				DumpNode node =
					stack[i];

				if (node == null ||
					!node.IsTypelessData)
				{
					continue;
				}

				if (!node.ArrayIndex.HasValue)
				{
					continue;
				}

				typelessIndex =
					i;

				typelessNode =
					node;

				break;
			}

			if (typelessNode == null)
				return false;

			/*
			 * Build the path from the parent fields plus the actual
			 * TypelessData field name.
			 */
			var parts =
				new List<string>();

			for (int i = 0;
				 i < typelessIndex;
				 i++)
			{
				DumpNode node =
					stack[i];

				if (node == null ||
					string.IsNullOrEmpty(node.Name))
				{
					continue;
				}

				if (string.Equals(
					node.Name,
					"<anonymous>",
					StringComparison.Ordinal))
				{
					continue;
				}

				/*
				 * Normal arrays occurring above the TypelessData are
				 * still represented with their active indices.
				 */
				if (node.IsArray)
				{
					if (parts.Count > 0)
					{
						string previous =
							parts[parts.Count - 1];

						int parentIndex =
							node.ArrayIndex ?? 0;

						parts[parts.Count - 1] =
							previous +
							"[" +
							parentIndex +
							"]";
					}

					continue;
				}

				/*
				 * A TypelessData nested above another TypelessData is
				 * unusual, but support it without corrupting the path.
				 */
				if (node.IsTypelessData)
				{
					parts.Add(
						node.Name);

					continue;
				}

				parts.Add(
					node.Name);
			}

			/*
			 * Finally append the actual TypelessData field name.
			 */
			parts.Add(
				typelessNode.Name);

			bufferPath =
				CanonicalizeStructuralPath(
					string.Join(
						"/",
						parts));

			index =
				typelessNode.ArrayIndex.Value;

			return !string.IsNullOrEmpty(
				bufferPath) &&
				index >= 0;
		}


		// ============================================================
		// READ DUMP SCALARS
		// ============================================================

		private static List<DumpScalar> ReadDumpScalars(
			string inputFile)
		{
			var result =
				new List<DumpScalar>();

			var stack =
				new List<DumpNode>();

			using (var reader =
				new StreamReader(
					inputFile,
					Encoding.UTF8,
					true))
			{
				int lineNumber = 0;

				while (true)
				{
					string line =
						reader.ReadLine();

					if (line == null)
						break;

					lineNumber++;

					if (string.IsNullOrWhiteSpace(line))
						continue;

					int depth =
						LeadingSpaces(line);

					if (depth >= line.Length)
						continue;

					string trimmed =
						line.Substring(depth);

					if (string.IsNullOrWhiteSpace(trimmed))
						continue;

					/*
					 * Indexed array element.
					 *
					 * Supported forms:
					 *
					 *   [0]
					 *
					 * and:
					 *
					 *   [0] UInt8 data = 113
					 */
					if (trimmed[0] == '[')
					{
						int close =
							trimmed.IndexOf(']');

						if (close > 1)
						{
							string indexText =
								trimmed.Substring(
									1,
									close - 1);

							if (int.TryParse(
								indexText,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture,
								out int arrayIndex))
							{
								for (int i =
									stack.Count - 1;
									i >= 0;
									i--)
								{
									DumpNode candidate =
										stack[i];

									if (candidate == null ||
										(!candidate.IsArray &&
										 !candidate.IsTypelessData))
									{
										continue;
									}

									if (candidate.Depth > depth)
										continue;

									candidate.ArrayIndex =
										arrayIndex;

									if (candidate.IsTypelessData &&
										(arrayIndex < 3 ||
										 arrayIndex >= 8230))
									{
										DebugStr(
											$"[TXT] TypelessData index assigned: " +
											$"node='{candidate.Name}', " +
											$"index={arrayIndex}, " +
											$"depth={depth}");
									}

									while (stack.Count > i + 1)
									{
										stack.RemoveAt(
											stack.Count - 1);
									}

									break;
								}
							}
						}

						/*
						 * If nothing follows ], this is an index-only
						 * structural line:
						 *
						 *   [123]
						 */
						if (close < 0 ||
							close + 1 >= trimmed.Length)
						{
							continue;
						}

						string remainder =
							trimmed.Substring(
								close + 1).TrimStart();

						if (remainder.Length == 0)
						{
							continue;
						}

						/*
						 * Inline payload:
						 *
						 *   [123] UInt8 data = 77
						 */
						trimmed =
							remainder;
					}

					int firstSpace =
						trimmed.IndexOf(' ');

					if (firstSpace <= 0)
						continue;

					string payload =
						trimmed.Substring(
							firstSpace + 1).TrimStart();

					if (payload.Length == 0)
						continue;

					int eq =
						payload.IndexOf('=');

					string left =
						eq >= 0
							? payload.Substring(
								0,
								eq).Trim()
							: payload;

					if (left.Length == 0)
						continue;

					string type;

					string fieldName =
						ParseDumpNodeName(
							left,
							out type);

					if (string.IsNullOrEmpty(fieldName))
						continue;

					while (stack.Count > 0 &&
						   stack[stack.Count - 1].Depth >= depth)
					{
						stack.RemoveAt(
							stack.Count - 1);
					}

					bool isScalar =
						eq >= 0;

					if (!isScalar &&
						depth == 0)
					{
						stack.Clear();
						continue;
					}

					if (!isScalar)
					{
						bool isArray =
							string.Equals(
								type,
								"Array",
								StringComparison.Ordinal);

						bool isTypelessData =
							string.Equals(
								type,
								"TypelessData",
								StringComparison.Ordinal);

						stack.Add(
							new DumpNode
							{
								Depth = depth,
								Name = fieldName,
								Type = type,
								IsArray = isArray,
								IsTypelessData = isTypelessData
							});

						continue;
					}

					string value =
						payload.Substring(
							eq + 1).Trim();

					if (string.Equals(
						fieldName,
						"size",
						StringComparison.Ordinal))
					{
						continue;
					}

					string scalarPath =
						BuildDumpLogicalPath(
							stack,
							fieldName);

					string rawBufferPath = "";
					int rawBufferIndex = -1;

					bool hasRawBufferContext =
						TryGetActiveRawBufferContext(
							stack,
							out rawBufferPath,
							out rawBufferIndex);

					if (hasRawBufferContext &&
						string.Equals(
							fieldName,
							"data",
							StringComparison.Ordinal) &&
						string.Equals(
							NormalizeDumpType(type),
							"UInt8",
							StringComparison.OrdinalIgnoreCase))
					{
						if (rawBufferIndex < 3 ||
							rawBufferIndex >= 8230)
						{
							DebugStr(
								$"[TXT] Raw TypelessData scalar: " +
								$"buffer='{rawBufferPath}', " +
								$"index={rawBufferIndex}, " +
								$"value={value}, " +
								$"line={lineNumber}");
						}
					}

					result.Add(
						new DumpScalar
						{
							LineNumber = lineNumber,
							Type = type,
							FieldName = fieldName,
							Value = value,
							Path = scalarPath,
							RawBufferPath = rawBufferPath,
							RawBufferIndex = rawBufferIndex
						});
				}
			}

			/*
			 * Compact TypelessData summary.
			 */
			var rawBufferGroups =
				result
					.Where(
						x =>
							x != null &&
							!string.IsNullOrEmpty(
								x.RawBufferPath) &&
							x.RawBufferIndex >= 0)
					.GroupBy(
						x => x.RawBufferPath,
						StringComparer.Ordinal)
					.ToList();

			foreach (var group in rawBufferGroups)
			{
				List<DumpScalar> ordered =
					group
						.OrderBy(
							x => x.RawBufferIndex)
						.ToList();

				DebugStr(
					$"[TXT] Parsed TypelessData buffer: " +
					$"path='{group.Key}', " +
					$"scalars={ordered.Count}, " +
					$"firstIndex={ordered.First().RawBufferIndex}, " +
					$"lastIndex={ordered.Last().RawBufferIndex}");

				int previewCount =
					Math.Min(
						3,
						ordered.Count);

				for (int i = 0;
					 i < previewCount;
					 i++)
				{
					DumpScalar scalar =
						ordered[i];

					DebugStr(
						$"[TXT]   raw[{scalar.RawBufferIndex}]=" +
						$"{scalar.Value}");
				}

				int tailStart =
					Math.Max(
						previewCount,
						ordered.Count - 2);

				for (int i = tailStart;
					 i < ordered.Count;
					 i++)
				{
					DumpScalar scalar =
						ordered[i];

					DebugStr(
						$"[TXT]   raw[{scalar.RawBufferIndex}]=" +
						$"{scalar.Value}");
				}
			}

			return result;
		}


		// ============================================================
		// READ DUMP ARRAY INFORMATION
		// ============================================================

		private static List<DumpArrayInfo> ReadDumpArrayInfos(
			string inputFile)
		{
			var result =
				new List<DumpArrayInfo>();

			var stack =
				new List<DumpNode>();

			using (var reader =
				new StreamReader(
					inputFile,
					Encoding.UTF8,
					true))
			{
				int lineNumber = 0;

				while (true)
				{
					string line =
						reader.ReadLine();

					if (line == null)
						break;

					lineNumber++;

					if (string.IsNullOrWhiteSpace(line))
						continue;

					int depth =
						LeadingSpaces(line);

					if (depth >= line.Length)
						continue;

					string trimmed =
						line.Substring(depth);

					if (string.IsNullOrWhiteSpace(trimmed))
						continue;

					if (trimmed[0] == '[')
					{
						int close =
							trimmed.IndexOf(']');

						if (close > 1)
						{
							string indexText =
								trimmed.Substring(
									1,
									close - 1);

							if (int.TryParse(
								indexText,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture,
								out int arrayIndex))
							{
								for (int i =
									stack.Count - 1;
									i >= 0;
									i--)
								{
									DumpNode candidate =
										stack[i];

									if (candidate == null ||
										(!candidate.IsArray &&
										 !candidate.IsTypelessData))
									{
										continue;
									}

									if (candidate.Depth > depth)
										continue;

									candidate.ArrayIndex =
										arrayIndex;

									if (candidate.IsTypelessData &&
										(arrayIndex < 3 ||
										 arrayIndex >= 8230))
									{
										DebugStr(
											$"[TXT] TypelessData index assigned: " +
											$"node='{candidate.Name}', " +
											$"index={arrayIndex}, " +
											$"depth={depth}");
									}

									while (stack.Count > i + 1)
									{
										stack.RemoveAt(
											stack.Count - 1);
									}

									break;
								}
							}
						}

						continue;
					}

					int firstSpace =
						trimmed.IndexOf(' ');

					if (firstSpace <= 0)
						continue;

					string payload =
						trimmed.Substring(
							firstSpace + 1).TrimStart();

					if (payload.Length == 0)
						continue;

					int eq =
						payload.IndexOf('=');

					string left =
						eq >= 0
							? payload.Substring(
								0,
								eq).Trim()
							: payload;

					if (left.Length == 0)
						continue;

					string type;

					string fieldName =
						ParseDumpNodeName(
							left,
							out type);

					if (string.IsNullOrEmpty(fieldName))
						continue;

					while (stack.Count > 0 &&
						   stack[stack.Count - 1].Depth >= depth)
					{
						stack.RemoveAt(
							stack.Count - 1);
					}

					bool isScalar =
						eq >= 0;

					if (!isScalar &&
						depth == 0)
					{
						stack.Clear();
						continue;
					}

					if (!isScalar)
					{
						bool isArray =
							string.Equals(
								type,
								"Array",
								StringComparison.Ordinal);

						bool isTypelessData =
							string.Equals(
								type,
								"TypelessData",
								StringComparison.Ordinal);

						if (isArray &&
							TryParseDumpArrayCount(
								left,
								out int count))
						{
							string parentPath =
								BuildDumpLogicalPath(
									stack,
									"");

							parentPath =
								CanonicalizeStructuralPath(
									parentPath);

							if (!string.IsNullOrEmpty(parentPath))
							{
								result.Add(
									new DumpArrayInfo
									{
										LineNumber = lineNumber,
										Path = parentPath,
										Count = count
									});
							}
						}
						else if (isTypelessData &&
								 TryParseDumpTypelessDataCount(
									 left,
									 out int typelessCount))
						{
							string parentPath =
								BuildDumpLogicalPath(
									stack,
									"");

							parentPath =
								CanonicalizeStructuralPath(
									parentPath);

							string typelessPath =
								string.IsNullOrEmpty(parentPath)
									? fieldName
									: parentPath + "/" + fieldName;

							typelessPath =
								CanonicalizeStructuralPath(
									typelessPath);

							if (!string.IsNullOrEmpty(
								typelessPath))
							{
								DebugStr(
									$"[TXT] Structural TypelessData detected: " +
									$"path='{typelessPath}', " +
									$"count={typelessCount}");

								result.Add(
									new DumpArrayInfo
									{
										LineNumber = lineNumber,
										Path = typelessPath,
										Count = typelessCount
									});
							}
						}

						stack.Add(
							new DumpNode
							{
								Depth = depth,
								Name = fieldName,
								Type = type,
								IsArray = isArray,
								IsTypelessData = isTypelessData
							});

						continue;
					}
				}
			}

			return result;
		}


		// ============================================================
		// TARGET BASEFIELD SCALAR COLLECTION
		// ============================================================

		private static void CollectScalarFieldEntriesRecursive(
			AssetTypeValueField field,
			string parentPath,
			List<ScalarFieldEntry> result)
		{
			if (field == null ||
				field.IsDummy)
			{
				return;
			}

			string fieldName =
				field.TemplateField?.Name ??
				"<unnamed>";

			string currentPath =
				string.IsNullOrEmpty(parentPath)
					? fieldName
					: parentPath + "/" + fieldName;

			if (field.Value != null &&
				field.Value.ValueType ==
					AssetValueType.Array)
			{
				if (field.Children != null)
				{
					for (int i = 0;
						 i < field.Children.Count;
						 i++)
					{
						CollectScalarFieldEntriesRecursive(
							field.Children[i],
							currentPath + "[" + i + "]",
							result);
					}
				}

				return;
			}

			if (field.TemplateField != null &&
				field.TemplateField.IsArray &&
				!IsTemplateByteArray(field))
			{
				if (field.Children != null)
				{
					for (int i = 0;
						 i < field.Children.Count;
						 i++)
					{
						CollectScalarFieldEntriesRecursive(
							field.Children[i],
							currentPath + "[" + i + "]",
							result);
					}
				}

				return;
			}

			if (field.Value == null &&
				field.Children != null &&
				field.Children.Count > 0)
			{
				AssetTypeValueField explicitArray =
					FindDirectChildByName(
						field,
						"Array");

				if (explicitArray != null &&
					GetFieldValueType(
						explicitArray) ==
						AssetValueType.Array)
				{
					if (explicitArray.Children != null)
					{
						for (int i = 0;
							 i < explicitArray.Children.Count;
							 i++)
						{
							CollectScalarFieldEntriesRecursive(
								explicitArray.Children[i],
								currentPath + "[" + i + "]",
								result);
						}
					}

					return;
				}
			}

			if (field.Children != null &&
				field.Children.Count > 0)
			{
				foreach (var child in field.Children)
				{
					CollectScalarFieldEntriesRecursive(
						child,
						currentPath,
						result);
				}

				return;
			}

			AssetValueType? effectiveType =
				GetNullableFieldValueType(
					field);

			if (!effectiveType.HasValue)
				return;

			if (effectiveType.Value ==
					AssetValueType.ByteArray ||
				effectiveType.Value ==
					AssetValueType.ManagedReferencesRegistry)
			{
				return;
			}

			string canonicalPath =
				CanonicalizeStructuralPath(
					currentPath);

			result.Add(
				new ScalarFieldEntry
				{
					Path = currentPath,
					CanonicalPath = canonicalPath,
					Type =
						RuntimeTypeToDumpType(
							effectiveType.Value),
					FieldName = fieldName,
					Field = field
				});
		}

		private static List<ScalarFieldEntry> CollectScalarFieldEntries(
			AssetTypeValueField baseField)
		{
			var result =
				new List<ScalarFieldEntry>();

			if (baseField?.Children != null)
			{
				foreach (var child in baseField.Children)
				{
					CollectScalarFieldEntriesRecursive(
						child,
						"",
						result);
				}
			}

			return result;
		}


		// ============================================================
		// TYPE NORMALIZATION
		// ============================================================

		private static string NormalizeDumpType(
			string type)
		{
			if (string.IsNullOrWhiteSpace(type))
				return "";

			type =
				type.Trim();

			switch (type)
			{
				case "unsigned int":
					return "UInt32";

				case "signed int":
					return "SInt32";

				case "unsigned short":
					return "UInt16";

				case "signed short":
					return "SInt16";

				case "unsigned long long":
					return "UInt64";

				case "signed long long":
					return "SInt64";

				case "UInt8":
				case "SInt8":
				case "UInt16":
				case "SInt16":
				case "UInt32":
				case "SInt32":
				case "UInt64":
				case "SInt64":
				case "bool":
				case "float":
				case "double":
				case "string":
					return type;

				case "int":
					return "SInt32";

				default:
					return type;
			}
		}

		private static string RuntimeTypeToDumpType(
			AssetValueType t)
		{
			switch (t)
			{
				case AssetValueType.Bool:
					return "bool";

				case AssetValueType.UInt8:
					return "UInt8";

				case AssetValueType.Int8:
					return "SInt8";

				case AssetValueType.UInt16:
					return "UInt16";

				case AssetValueType.Int16:
					return "SInt16";

				case AssetValueType.UInt32:
					return "UInt32";

				case AssetValueType.Int32:
					return "SInt32";

				case AssetValueType.UInt64:
					return "UInt64";

				case AssetValueType.Int64:
					return "SInt64";

				case AssetValueType.Float:
					return "float";

				case AssetValueType.Double:
					return "double";

				case AssetValueType.String:
					return "string";

				default:
					return t.ToString();
			}
		}


		// ============================================================
		// BASEFIELD HELPERS
		// ============================================================

		private static AssetTypeValueField FindDirectChildByName(
			AssetTypeValueField field,
			string name)
		{
			if (field == null ||
				field.IsDummy ||
				string.IsNullOrEmpty(name))
			{
				return null;
			}

			if (field.Children != null)
			{
				foreach (AssetTypeValueField child in field.Children)
				{
					if (child == null ||
						child.IsDummy)
					{
						continue;
					}

					string childName =
						child.TemplateField?.Name ??
						"";

					if (string.Equals(
						childName,
						name,
						StringComparison.Ordinal))
					{
						return child;
					}
				}
			}

			return null;
		}

		private static AssetTypeValueField FindChildThroughArrayWrapper(
			AssetTypeValueField field,
			string name)
		{
			AssetTypeValueField direct =
				FindDirectChildByName(
					field,
					name);

			if (direct != null)
				return direct;

			AssetTypeValueField arrayWrapper =
				FindDirectChildByName(
					field,
					"Array");

			if (arrayWrapper == null ||
				arrayWrapper.IsDummy)
			{
				return null;
			}

			return FindDirectChildByName(
				arrayWrapper,
				name);
		}

		private static bool IsTemplateByteArray(
			AssetTypeValueField field)
		{
			if (field == null ||
				field.IsDummy ||
				field.TemplateField == null)
			{
				return false;
			}

			return
				field.TemplateField.IsArray &&
				field.TemplateField.ValueType ==
					AssetValueType.ByteArray;
		}

		private static AssetValueType GetFieldValueType(
			AssetTypeValueField field)
		{
			if (field == null)
			{
				throw new ArgumentNullException(nameof(field));
			}

			if (IsTemplateByteArray(field))
			{
				return AssetValueType.ByteArray;
			}

			if (field.Value != null)
				return field.Value.ValueType;

			if (field.TemplateField != null)
				return field.TemplateField.ValueType;

			throw new InvalidDataException(
				$"Field '{field.TemplateField?.Name ?? "<unnamed>"}' " +
				"has neither a runtime value nor a template value type.");
		}

		private static AssetValueType? GetNullableFieldValueType(
			AssetTypeValueField field)
		{
			if (field == null)
				return null;

			if (IsTemplateByteArray(field))
			{
				return AssetValueType.ByteArray;
			}

			if (field.Value != null)
				return field.Value.ValueType;

			if (field.TemplateField != null)
				return field.TemplateField.ValueType;

			return null;
		}

		private static bool IsSpriteBaseField(
			AssetTypeValueField baseField)
		{
			if (baseField == null ||
				baseField.IsDummy)
			{
				return false;
			}

			string rootName =
				baseField.TemplateField?.Name ??
				"";

			if (string.Equals(
				rootName,
				"Sprite",
				StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			AssetTypeValueField mRD =
				FindDirectChildByName(
					baseField,
					"m_RD");

			AssetTypeValueField physicsShape =
				FindDirectChildByName(
					baseField,
					"m_PhysicsShape");

			return
				mRD != null &&
				physicsShape != null;
		}

		private static int CountArrayIndices(
			string path)
		{
			if (string.IsNullOrEmpty(path))
				return 0;

			int count = 0;

			for (int i = 0;
				 i < path.Length;
				 i++)
			{
				if (path[i] == '[')
					count++;
			}

			return count;
		}

		private static bool TrySplitPathSegment(
			string segment,
			out string fieldName,
			out int arrayIndex)
		{
			fieldName =
				segment ?? "";

			arrayIndex =
				-1;

			if (string.IsNullOrEmpty(segment))
				return false;

			int openBracket =
				segment.LastIndexOf('[');

			if (openBracket < 0 ||
				!segment.EndsWith(
					"]",
					StringComparison.Ordinal))
			{
				return true;
			}

			int closeBracket =
				segment.Length - 1;

			string indexText =
				segment.Substring(
					openBracket + 1,
					closeBracket - openBracket - 1);

			if (!int.TryParse(
				indexText,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out arrayIndex))
			{
				fieldName = segment;
				arrayIndex = -1;
				return true;
			}

			fieldName =
				segment.Substring(
					0,
					openBracket);

			return true;
		}

		private static bool TryResolveLogicalPath(
			AssetTypeValueField baseField,
			string path,
			out AssetTypeValueField resolved)
		{
			resolved = null;

			if (baseField == null ||
				baseField.IsDummy)
			{
				return false;
			}

			string canonical =
				CanonicalizeStructuralPath(path);

			if (string.IsNullOrEmpty(canonical))
			{
				return false;
			}

			string[] segments =
				canonical.Split(
					'/',
					StringSplitOptions.RemoveEmptyEntries);

			AssetTypeValueField current =
				baseField;

			for (int i = 0;
				 i < segments.Length;
				 i++)
			{
				string rawSegment =
					segments[i];

				string fieldName;
				int arrayIndex;

				TrySplitPathSegment(
					rawSegment,
					out fieldName,
					out arrayIndex);

				if (!string.IsNullOrEmpty(fieldName))
				{
					if (current != null &&
						current.TemplateField != null &&
						string.Equals(
							current.TemplateField.Name,
							fieldName,
							StringComparison.Ordinal) &&
						GetFieldValueType(current) ==
							AssetValueType.Array)
					{
					}
					else
					{
						AssetTypeValueField child =
							FindChildThroughArrayWrapper(
								current,
								fieldName);

						if (child == null)
						{
							return false;
						}

						current =
							child;
					}
				}

				if (arrayIndex >= 0)
				{
					if (current == null ||
						current.IsDummy)
					{
						return false;
					}

					if (IsTemplateByteArray(current))
					{
						return false;
					}

					AssetValueType currentType =
						GetFieldValueType(
							current);

					if (currentType !=
						AssetValueType.Array)
					{
						AssetTypeValueField arrayChild =
							FindDirectChildByName(
								current,
								"Array");

						if (arrayChild == null ||
							arrayChild.IsDummy)
						{
							return false;
						}

						current =
							arrayChild;

						if (IsTemplateByteArray(current))
						{
							return false;
						}

						currentType =
							GetFieldValueType(
								current);
					}

					if (currentType !=
						AssetValueType.Array)
					{
						return false;
					}

					if (current.Children == null ||
						arrayIndex < 0 ||
						arrayIndex >= current.Children.Count)
					{
						return false;
					}

					current =
						current.Children[arrayIndex];
				}
			}

			if (current == null ||
				current.IsDummy)
			{
				return false;
			}

			if (IsTemplateByteArray(current))
			{
				resolved =
					current;

				return true;
			}

			AssetValueType finalType =
				GetFieldValueType(
					current);

			if (finalType ==
				AssetValueType.ByteArray)
			{
				resolved =
					current;

				return true;
			}

			if (finalType ==
				AssetValueType.Array)
			{
				resolved =
					current;

				return true;
			}

			AssetTypeValueField finalArray =
				FindDirectChildByName(
					current,
					"Array");

			if (finalArray != null &&
				!finalArray.IsDummy)
			{
				if (IsTemplateByteArray(finalArray))
				{
					resolved =
						finalArray;

					return true;
				}

				AssetValueType finalArrayType =
					GetFieldValueType(
						finalArray);

				if (finalArrayType ==
					AssetValueType.Array ||
					finalArrayType ==
					AssetValueType.ByteArray)
				{
					resolved =
						finalArray;

					return true;
				}
			}

			return false;
		}

		private static bool TryGetIndexedByteArrayScalarInfo(
			DumpScalar scalar,
			out string byteArrayPath,
			out int index)
		{
			byteArrayPath = "";
			index = -1;

			if (scalar == null)
				return false;

			/*
			 * Preferred path:
			 * explicit raw TypelessData metadata.
			 */
			if (!string.IsNullOrEmpty(
				scalar.RawBufferPath) &&
				scalar.RawBufferIndex >= 0)
			{
				byteArrayPath =
					CanonicalizeStructuralPath(
						scalar.RawBufferPath);

				index =
					scalar.RawBufferIndex;

				return !string.IsNullOrEmpty(
					byteArrayPath);
			}

			/*
			 * Fallback for ordinary ByteArrays:
			 *
			 *   m_RD/m_IndexBuffer[0]
			 *   m_RD/m_IndexBuffer[0]/data
			 */
			string path =
				scalar.Path?.Trim() ?? "";

			if (path.Length == 0)
				return false;

			string core =
				path;

			const string dataSuffix = "/data";

			if (core.EndsWith(
				dataSuffix,
				StringComparison.Ordinal))
			{
				core =
					core.Substring(
						0,
						core.Length -
						dataSuffix.Length);
			}

			int openBracket =
				core.LastIndexOf('[');

			if (openBracket < 0 ||
				!core.EndsWith(
					"]",
					StringComparison.Ordinal))
			{
				return false;
			}

			int closeBracket =
				core.Length - 1;

			string indexText =
				core.Substring(
					openBracket + 1,
					closeBracket -
					openBracket -
					1);

			if (!int.TryParse(
				indexText,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out index))
			{
				return false;
			}

			if (index < 0)
				return false;

			byteArrayPath =
				CanonicalizeStructuralPath(
					core.Substring(
						0,
						openBracket));

			return !string.IsNullOrEmpty(
				byteArrayPath);
		}


		// ============================================================
		// STRUCTURAL SPRITE ARRAY SYNCHRONIZATION
		// ============================================================

		private static Dictionary<string, DumpArrayInfo>
			BuildUniqueDumpArrayInfoMap(
				List<DumpArrayInfo> arrayInfos)
		{
			var result =
				new Dictionary<string, DumpArrayInfo>(
					StringComparer.Ordinal);

			foreach (var info in arrayInfos)
			{
				if (info == null ||
					string.IsNullOrEmpty(info.Path))
				{
					continue;
				}

				string path =
					CanonicalizeStructuralPath(
						info.Path);

				if (result.TryGetValue(
					path,
					out DumpArrayInfo existing))
				{
					if (existing.Count != info.Count)
					{
						throw new InvalidDataException(
							$"Dump contains conflicting array sizes for " +
							$"'{path}': " +
							$"line {existing.LineNumber}={existing.Count}, " +
							$"line {info.LineNumber}={info.Count}.");
					}

					continue;
				}

				result.Add(
					path,
					new DumpArrayInfo
					{
						LineNumber = info.LineNumber,
						Path = path,
						Count = info.Count
					});
			}

			return result;
		}

		private static void ResizeTargetArray(
			AssetTypeValueField arrayField,
			int expectedCount)
		{
			if (arrayField == null)
			{
				throw new InvalidDataException(
					"Attempted to resize a null array field.");
			}

			if (IsTemplateByteArray(arrayField))
			{
				throw new InvalidDataException(
					"Attempted to resize a ByteArray as a normal array.");
			}

			AssetValueType fieldType =
				GetFieldValueType(
					arrayField);

			if (fieldType !=
				AssetValueType.Array)
			{
				AssetTypeValueField explicitArray =
					FindDirectChildByName(
						arrayField,
						"Array");

				if (explicitArray == null ||
					explicitArray.IsDummy ||
					IsTemplateByteArray(explicitArray) ||
					GetFieldValueType(explicitArray) !=
						AssetValueType.Array)
				{
					throw new InvalidDataException(
						"Attempted to resize a field that is not a normal array.");
				}

				arrayField =
					explicitArray;

				fieldType =
					GetFieldValueType(
						arrayField);
			}

			if (expectedCount < 0)
			{
				throw new InvalidDataException(
					$"Invalid negative array size {expectedCount}.");
			}

			const int maxReasonableArrayItems =
				5000000;

			if (expectedCount >
				maxReasonableArrayItems)
			{
				throw new InvalidDataException(
					$"Array size {expectedCount} exceeds the safety limit " +
					$"{maxReasonableArrayItems}.");
			}

			if (arrayField.Children == null)
			{
				arrayField.Children =
					new List<AssetTypeValueField>();
			}

			if (arrayField.Children.Count ==
				expectedCount)
			{
				return;
			}

			var newChildren =
				new List<AssetTypeValueField>(
					expectedCount);

			for (int i = 0;
				 i < expectedCount;
				 i++)
			{
				AssetTypeValueField newItem =
					ValueBuilder.DefaultValueFieldFromArrayTemplate(
						arrayField);

				if (newItem == null)
				{
					throw new InvalidDataException(
						"ValueBuilder returned null while creating " +
						$"array element {i}.");
				}

				newChildren.Add(
					newItem);
			}

			arrayField.Children.Clear();
			arrayField.Children.AddRange(
				newChildren);
		}

		private static void SynchronizeDumpArrayStructure(
			string inputFile,
			AssetTypeValueField baseField,
			List<DumpScalar> dumpScalars,
			bool applyChanges)
		{
			List<DumpArrayInfo> rawArrayInfos =
				ReadDumpArrayInfos(inputFile);

			Dictionary<string, DumpArrayInfo> arrayInfoMap =
				BuildUniqueDumpArrayInfoMap(
					rawArrayInfos);

			DebugStr(
				$"[TXT] Structural arrays found in dump: " +
				$"{arrayInfoMap.Count}");

			List<DumpArrayInfo> orderedArrays =
				arrayInfoMap.Values
					.OrderBy(
						x => CountArrayIndices(x.Path))
					.ThenBy(
						x => x.Path.Length)
					.ToList();

			var targetByteArrayPaths =
				new HashSet<string>(
					StringComparer.Ordinal);

			foreach (DumpArrayInfo info in orderedArrays)
			{
				AssetTypeValueField targetField;

				if (!TryResolveLogicalPath(
					baseField,
					info.Path,
					out targetField))
				{
					throw new InvalidDataException(
						$"Structural array mapping failed at dump line " +
						$"{info.LineNumber}: array path '{info.Path}' " +
						$"does not exist in target.");
				}

				AssetValueType targetType =
					GetFieldValueType(
						targetField);

				if (targetType ==
					AssetValueType.ByteArray)
				{
					targetByteArrayPaths.Add(
						info.Path);

					DebugStr(
						$"[TXT] Structural ByteArray detected: " +
						$"path='{info.Path}', count={info.Count}");

					continue;
				}

				if (targetType !=
					AssetValueType.Array)
				{
					throw new InvalidDataException(
						$"Structural array mapping failed at dump line " +
						$"{info.LineNumber}: path='{info.Path}' " +
						$"is dump array size={info.Count}, but target " +
						$"type is '{targetType}'.");
				}

				int currentCount =
					targetField.Children?.Count ?? 0;

				if (applyChanges)
				{
					if (currentCount != info.Count)
					{
						DebugStr(
							$"[TXT] Resizing array '{info.Path}': " +
							$"target={currentCount}, dump={info.Count}");

						ResizeTargetArray(
							targetField,
							info.Count);
					}
					else
					{
						DebugStr(
							$"[TXT] Array '{info.Path}' already has " +
							$"correct size {info.Count}.");
					}
				}
				else
				{
					if (currentCount != info.Count)
					{
						throw new InvalidDataException(
							$"FINAL CHECK: array size mismatch at " +
							$"'{info.Path}': " +
							$"dump={info.Count}, " +
							$"target={currentCount}.");
					}
				}
			}

			foreach (string byteArrayPath in
				targetByteArrayPaths)
			{
				DumpArrayInfo info =
					arrayInfoMap[byteArrayPath];

				if (!TryResolveLogicalPath(
					baseField,
					byteArrayPath,
					out AssetTypeValueField targetField))
				{
					throw new InvalidDataException(
						$"ByteArray path '{byteArrayPath}' " +
						$"cannot be resolved in target.");
				}

				if (GetFieldValueType(
					targetField) !=
					AssetValueType.ByteArray)
				{
					throw new InvalidDataException(
						$"Resolved ByteArray path '{byteArrayPath}' " +
						$"is actually '{GetFieldValueType(targetField)}'.");
				}

				List<DumpScalar> byteScalars =
					new List<DumpScalar>();

				foreach (DumpScalar scalar in dumpScalars)
				{
					if (scalar == null)
						continue;

					if (!string.Equals(
						NormalizeDumpType(scalar.Type),
						"UInt8",
						StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (!string.Equals(
						scalar.FieldName,
						"data",
						StringComparison.Ordinal))
					{
						continue;
					}

					if (!TryGetIndexedByteArrayScalarInfo(
						scalar,
						out string scalarParent,
						out int index))
					{
						continue;
					}

					if (!string.Equals(
						scalarParent,
						byteArrayPath,
						StringComparison.Ordinal))
					{
						continue;
					}

					byteScalars.Add(
						scalar);
				}

				DebugStr(
					$"[TXT] ByteArray scalar collection: " +
					$"path='{byteArrayPath}', " +
					$"dumpSize={info.Count}, " +
					$"parsedEntries={byteScalars.Count}");

				if (byteScalars.Count != info.Count)
				{
					throw new InvalidDataException(
						$"ByteArray '{byteArrayPath}' has dump size " +
						$"{info.Count}, but {byteScalars.Count} " +
						$"UInt8 data entries were found.");
				}

				byteScalars =
					byteScalars
						.OrderBy(
							x =>
							{
								TryGetIndexedByteArrayScalarInfo(
									x,
									out _,
									out int idx);

								return idx;
							})
						.ToList();

				byte[] bytes =
					new byte[info.Count];

				var seen =
					new bool[info.Count];

				foreach (DumpScalar scalar in byteScalars)
				{
					TryGetIndexedByteArrayScalarInfo(
						scalar,
						out _,
						out int index);

					if (index < 0 ||
						index >= info.Count)
					{
						throw new InvalidDataException(
							$"ByteArray '{byteArrayPath}' contains " +
							$"out-of-range index {index}, " +
							$"expected 0..{info.Count - 1}.");
					}

					if (seen[index])
					{
						throw new InvalidDataException(
							$"ByteArray '{byteArrayPath}' contains " +
							$"duplicate element index {index}.");
					}

					byte value =
						byte.Parse(
							scalar.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);

					bytes[index] =
						value;

					seen[index] =
						true;
				}

				for (int i = 0;
					 i < seen.Length;
					 i++)
				{
					if (!seen[i])
					{
						throw new InvalidDataException(
							$"ByteArray '{byteArrayPath}' is missing " +
							$"dump element {i}.");
					}
				}

				if (applyChanges)
				{
					targetField.AsByteArray =
						bytes;

					DebugStr(
						$"[TXT] Applied ByteArray '{byteArrayPath}': " +
						$"{bytes.Length} bytes.");
				}
				else
				{
					byte[] actual =
						targetField.AsByteArray ??
						Array.Empty<byte>();

					if (!actual.SequenceEqual(bytes))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: ByteArray mismatch at " +
							$"'{byteArrayPath}': " +
							$"expectedLength={bytes.Length}, " +
							$"actualLength={actual.Length}.");
					}

					DebugStr(
						$"[CHECK] ByteArray validation PASSED: " +
						$"'{byteArrayPath}' ({bytes.Length} bytes).");
				}
			}
		}


		// ============================================================
		// STRUCTURAL SPRITE MATCHING
		// ============================================================

		private static bool IsDumpScalarPartOfByteArray(
			DumpScalar dump,
			HashSet<string> byteArrayPaths)
		{
			if (dump == null ||
				byteArrayPaths == null ||
				byteArrayPaths.Count == 0)
			{
				return false;
			}

			if (!string.Equals(
				NormalizeDumpType(dump.Type),
				"UInt8",
				StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			if (!string.Equals(
				dump.FieldName,
				"data",
				StringComparison.Ordinal))
			{
				return false;
			}

			if (!TryGetIndexedByteArrayScalarInfo(
				dump,
				out string parentPath,
				out _))
			{
				return false;
			}

			return byteArrayPaths.Contains(
				parentPath);
		}

		private static List<DumpTargetMatch>
			BuildStructuralDumpTargetMatches(
				List<DumpScalar> dumpScalars,
				AssetTypeValueField baseField,
				bool requireExactTargetCount)
		{
			if (dumpScalars == null)
			{
				dumpScalars =
					new List<DumpScalar>();
			}

			var targetEntries =
				CollectScalarFieldEntries(
					baseField);

			DebugStr(
				$"[TXT] Sprite scalar mapping: " +
				$"dump scalar count={dumpScalars.Count}; " +
				$"target scalar count={targetEntries.Count}");

			var targetByCanonicalPath =
				new Dictionary<string, ScalarFieldEntry>(
					StringComparer.Ordinal);

			foreach (ScalarFieldEntry target in targetEntries)
			{
				string canonical =
					CanonicalizeStructuralPath(
						target.Path);

				target.CanonicalPath =
					canonical;

				if (!targetByCanonicalPath.TryAdd(
					canonical,
					target))
				{
					throw new InvalidDataException(
						$"Duplicate target canonical scalar path " +
						$"'{canonical}'. " +
						$"Existing='{targetByCanonicalPath[canonical].Path}', " +
						$"duplicate='{target.Path}'.");
				}
			}

			var matches =
				new List<DumpTargetMatch>();

			var usedCanonicalPaths =
				new HashSet<string>(
					StringComparer.Ordinal);

			int effectiveDumpScalarCount = 0;

			foreach (DumpScalar dump in dumpScalars)
			{
				effectiveDumpScalarCount++;

				string canonicalDumpPath =
					CanonicalizeStructuralPath(
						dump.Path);

				if (!targetByCanonicalPath.TryGetValue(
					canonicalDumpPath,
					out ScalarFieldEntry target))
				{
					throw new InvalidDataException(
						$"SPRITE structural mapping failed at dump line " +
						$"{dump.LineNumber}: " +
						$"'{dump.Type} {dump.FieldName}' " +
						$"path='{dump.Path}' " +
						$"canonical='{canonicalDumpPath}' " +
						$"has no matching target field.");
				}

				string dumpType =
					NormalizeDumpType(
						dump.Type);

				string targetType =
					NormalizeDumpType(
						target.Type);

				if (!string.Equals(
					dumpType,
					targetType,
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						$"SPRITE type mismatch at dump line " +
						$"{dump.LineNumber}: " +
						$"path='{dump.Path}', " +
						$"dump='{dump.Type}', " +
						$"target='{target.Type}', " +
						$"targetPath='{target.Path}'.");
				}

				if (!usedCanonicalPaths.Add(
					canonicalDumpPath))
				{
					throw new InvalidDataException(
						$"Duplicate dump scalar canonical path " +
						$"'{canonicalDumpPath}' " +
						$"at line {dump.LineNumber}.");
				}

				matches.Add(
					new DumpTargetMatch
					{
						Dump = dump,
						Target = target
					});
			}

			if (requireExactTargetCount &&
				effectiveDumpScalarCount !=
					targetEntries.Count)
			{
				throw new InvalidDataException(
					$"SPRITE structural mapping count mismatch: " +
					$"dumpScalars={effectiveDumpScalarCount}, " +
					$"targetScalars={targetEntries.Count}.");
			}

			return matches;
		}


		// ============================================================
		// BUILD DUMP LOGICAL BYTEARRAY PATH SET
		// ============================================================

		private static HashSet<string>
			ResolveByteArrayPaths(
				string inputFile,
				AssetTypeValueField baseField)
		{
			var result =
				new HashSet<string>(
					StringComparer.Ordinal);

			Dictionary<string, DumpArrayInfo> arrayInfoMap =
				BuildUniqueDumpArrayInfoMap(
					ReadDumpArrayInfos(inputFile));

			foreach (DumpArrayInfo info in
				arrayInfoMap.Values)
			{
				if (!TryResolveLogicalPath(
					baseField,
					info.Path,
					out AssetTypeValueField targetField))
				{
					continue;
				}

				if (GetFieldValueType(
					targetField) ==
					AssetValueType.ByteArray)
				{
					result.Add(
						info.Path);
				}
			}

			return result;
		}


		// ============================================================
		// APPLY DUMP VALUES
		// ============================================================

		private static void ApplyDumpValue(
			AssetsTools.NET.AssetTypeValueField field,
			DumpScalar dump)
		{
			if (field == null ||
				field.Value == null)
			{
				throw new InvalidOperationException(
					$"Target field '{dump.FieldName}' " +
					"has no scalar value.");
			}

			switch (field.Value.ValueType)
			{
				case AssetValueType.Bool:
					field.AsBool =
						bool.Parse(
							dump.Value);
					break;

				case AssetValueType.UInt8:
					field.AsUInt =
						byte.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.Int8:
					field.AsInt =
						sbyte.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.UInt16:
					field.AsUInt =
						ushort.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.Int16:
					field.AsInt =
						short.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.UInt32:
					field.AsUInt =
						uint.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.Int32:
					field.AsInt =
						int.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.UInt64:
					field.AsULong =
						ulong.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.Int64:
					field.AsLong =
						long.Parse(
							dump.Value,
							NumberStyles.Integer,
							CultureInfo.InvariantCulture);
					break;

				case AssetValueType.Float:
					field.AsFloat =
						ParseSingle(
							dump.Value);
					break;

				case AssetValueType.Double:
					field.AsDouble =
						ParseDouble(
							dump.Value);
					break;

				case AssetValueType.String:
					field.AsString =
						ParseDumpString(
							dump.Value);
					break;

				default:
					throw new NotSupportedException(
						$"Unsupported runtime scalar type " +
						$"'{field.Value.ValueType}' " +
						$"for dump line {dump.LineNumber} " +
						$"({dump.Type} {dump.FieldName}).");
			}
		}


		// ============================================================
		// APPLY FULL SPRITE DUMP
		// ============================================================

		private static byte[] ApplySpriteStructuralDumpToBaseField(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField)
		{
			DebugStr(
				"[SPRITE] Applying structural Sprite dump " +
				"with array reconstruction.");

			List<DumpScalar> dumpScalars =
				ReadDumpScalars(
					inputFile);

			SynchronizeDumpArrayStructure(
				inputFile,
				baseField,
				dumpScalars,
				true);

			HashSet<string> byteArrayPaths =
				ResolveByteArrayPaths(
					inputFile,
					baseField);

			DebugStr(
				$"[SPRITE] Resolved structural ByteArrays: " +
				$"{byteArrayPaths.Count}");

			foreach (string path in byteArrayPaths)
			{
				DebugStr(
					$"[SPRITE]   ByteArray='{path}'");
			}

			List<DumpTargetMatch> matches =
				BuildStructuralDumpTargetMatchesWithByteArrays(
					dumpScalars,
					baseField,
					byteArrayPaths,
					true);

			DebugStr(
				$"[SPRITE] Structural mapping passed for " +
				$"{matches.Count} scalar fields. Applying values.");

			foreach (DumpTargetMatch match in matches)
			{
				try
				{
					ApplyDumpValue(
						match.Target.Field,
						match.Dump);
				}
				catch (Exception ex)
				{
					throw new InvalidDataException(
						$"Unable to apply Sprite field " +
						$"path='{match.Dump.Path}', " +
						$"line={match.Dump.LineNumber}.",
						ex);
				}
			}

			byte[] data =
				baseField.WriteToByteArray();

			DebugStr(
				$"[SPRITE] BaseField reserialized: " +
				$"{data.Length} bytes " +
				$"SHA256={Sha256Hex(data)}");

			return data;
		}

		private static List<DumpTargetMatch>
			BuildStructuralDumpTargetMatchesWithByteArrays(
				List<DumpScalar> dumpScalars,
				AssetTypeValueField baseField,
				HashSet<string> byteArrayPaths,
				bool requireExactTargetCount)
		{
			List<ScalarFieldEntry> targetEntries =
				CollectScalarFieldEntries(
					baseField);

			DebugStr(
				$"[TXT] Sprite scalar mapping after structural sync: " +
				$"dump={dumpScalars.Count}, " +
				$"target={targetEntries.Count}, " +
				$"byteArrays={byteArrayPaths.Count}");

			var targetByCanonicalPath =
				new Dictionary<string, ScalarFieldEntry>(
					StringComparer.Ordinal);

			foreach (ScalarFieldEntry target in targetEntries)
			{
				string canonical =
					CanonicalizeStructuralPath(
						target.Path);

				target.CanonicalPath =
					canonical;

				if (!targetByCanonicalPath.TryAdd(
					canonical,
					target))
				{
					throw new InvalidDataException(
						$"Duplicate target canonical scalar path " +
						$"'{canonical}'. " +
						$"Existing='{targetByCanonicalPath[canonical].Path}', " +
						$"duplicate='{target.Path}'.");
				}
			}

			var matches =
				new List<DumpTargetMatch>();

			var usedCanonicalPaths =
				new HashSet<string>(
					StringComparer.Ordinal);

			int effectiveDumpCount = 0;

			foreach (DumpScalar dump in dumpScalars)
			{
				if (IsDumpScalarPartOfByteArray(
					dump,
					byteArrayPaths))
				{
					continue;
				}

				effectiveDumpCount++;

				string canonicalDumpPath =
					CanonicalizeStructuralPath(
						dump.Path);

				if (!targetByCanonicalPath.TryGetValue(
					canonicalDumpPath,
					out ScalarFieldEntry target))
				{
					throw new InvalidDataException(
						$"SPRITE structural mapping failed at dump line " +
						$"{dump.LineNumber}: " +
						$"'{dump.Type} {dump.FieldName}' " +
						$"path='{dump.Path}' " +
						$"canonical='{canonicalDumpPath}' " +
						$"has no matching target field.");
				}

				string dumpType =
					NormalizeDumpType(
						dump.Type);

				string targetType =
					NormalizeDumpType(
						target.Type);

				if (!string.Equals(
					dumpType,
					targetType,
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						$"SPRITE type mismatch at dump line " +
						$"{dump.LineNumber}: " +
						$"path='{dump.Path}', " +
						$"dump='{dump.Type}', " +
						$"target='{target.Type}', " +
						$"targetPath='{target.Path}'.");
				}

				if (!usedCanonicalPaths.Add(
					canonicalDumpPath))
				{
					throw new InvalidDataException(
						$"Duplicate dump scalar canonical path " +
						$"'{canonicalDumpPath}' " +
						$"at line {dump.LineNumber}.");
				}

				matches.Add(
					new DumpTargetMatch
					{
						Dump = dump,
						Target = target
					});
			}

			if (requireExactTargetCount &&
				effectiveDumpCount !=
					targetEntries.Count)
			{
				throw new InvalidDataException(
					$"SPRITE structural scalar count mismatch: " +
					$"effectiveDump={effectiveDumpCount}, " +
					$"target={targetEntries.Count}, " +
					$"byteArrayPaths={byteArrayPaths.Count}.");
			}

			return matches;
		}


		// ============================================================
		// APPLY FULL DUMP
		// ============================================================

		private static byte[] ApplyTextDumpToBaseField(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField)
		{
			if (IsSpriteBaseField(
				baseField))
			{
				return ApplySpriteStructuralDumpToBaseField(
					inputFile,
					baseField);
			}

			DebugStr(
				"[TXT] Applying FULL checked dump " +
				"with structural preflight.");

			var matches =
				BuildDumpTargetMatches(
					inputFile,
					baseField,
					true);

			DebugStr(
				$"[TXT] FULL checked structural preflight passed " +
				$"for {matches.Count} fields. Applying values now.");

			foreach (var match in matches)
			{
				try
				{
					ApplyDumpValue(
						match.Target.Field,
						match.Dump);
				}
				catch (Exception ex)
				{
					throw new InvalidDataException(
						$"Unable to apply checked FULL field " +
						$"path='{match.Dump.Path}', " +
						$"line={match.Dump.LineNumber}.",
						ex);
				}
			}

			byte[] data =
				baseField.WriteToByteArray();

			DebugStr(
				$"[TXT] BaseField reserialized: " +
				$"{data.Length} bytes " +
				$"SHA256={Sha256Hex(data)}");

			return data;
		}


		// ============================================================
		// STRUCTURAL MATCHING (LEGACY / CHECKED)
		// ============================================================

		private static List<DumpTargetMatch> BuildDumpTargetMatches(
			string inputFile,
			AssetTypeValueField baseField,
			bool requireExactTargetCount)
		{
			var dumpScalars =
				ReadDumpScalars(inputFile);

			var targetEntries =
				CollectScalarFieldEntries(baseField);

			DebugStr(
				$"[TXT] Structural mapping: " +
				$"dump scalar count={dumpScalars.Count}; " +
				$"target scalar count={targetEntries.Count}");

			var targetByCanonicalPath =
				new Dictionary<string, ScalarFieldEntry>(
					StringComparer.Ordinal);

			foreach (var target in targetEntries)
			{
				string canonical =
					CanonicalizeStructuralPath(
						target.Path);

				target.CanonicalPath =
					canonical;

				if (!targetByCanonicalPath.TryAdd(
					canonical,
					target))
				{
					throw new InvalidDataException(
						$"Duplicate target canonical scalar path '{canonical}'. " +
						$"Existing='{targetByCanonicalPath[canonical].Path}', " +
						$"duplicate='{target.Path}'.");
				}
			}

			if (dumpScalars.Count > 0)
			{
				DebugStr(
					"[TXT] Dump logical scalar path sample:");

				int sampleCount =
					Math.Min(
						12,
						dumpScalars.Count);

				for (int i = 0;
					 i < sampleCount;
					 i++)
				{
					var dump =
						dumpScalars[i];

					DebugStr(
						$"[TXT]   L{dump.LineNumber}: " +
						$"{dump.Type} {dump.FieldName} -> " +
						$"{dump.Path}");
				}
			}

			if (targetEntries.Count > 0)
			{
				DebugStr(
					"[TXT] Target canonical scalar path sample:");

				int sampleCount =
					Math.Min(
						12,
						targetEntries.Count);

				for (int i = 0;
					 i < sampleCount;
					 i++)
				{
					var target =
						targetEntries[i];

					DebugStr(
						$"[TXT]   {target.Type} " +
						$"{target.FieldName} -> " +
						$"{target.Path} " +
						$"[canonical={target.CanonicalPath}]");
				}
			}

			var matches =
				new List<DumpTargetMatch>(
					dumpScalars.Count);

			var usedCanonicalPaths =
				new HashSet<string>(
					StringComparer.Ordinal);

			int equivalentArrayPaths =
				0;

			foreach (var dump in dumpScalars)
			{
				string canonicalDumpPath =
					CanonicalizeStructuralPath(
						dump.Path);

				if (!targetByCanonicalPath.TryGetValue(
					canonicalDumpPath,
					out ScalarFieldEntry target))
				{
					throw new InvalidDataException(
						$"FULL structural mapping failed at dump line " +
						$"{dump.LineNumber}: " +
						$"'{dump.Type} {dump.FieldName}' " +
						$"path='{dump.Path}' " +
						$"canonical='{canonicalDumpPath}' " +
						$"has no matching target field.");
				}

				if (!string.Equals(
					target.Path,
					dump.Path,
					StringComparison.Ordinal))
				{
					equivalentArrayPaths++;
				}

				string dumpType =
					NormalizeDumpType(
						dump.Type);

				string targetType =
					NormalizeDumpType(
						target.Type);

				if (!string.Equals(
					dumpType,
					targetType,
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						$"FULL type mismatch at dump line " +
						$"{dump.LineNumber}: " +
						$"path='{dump.Path}', " +
						$"canonical='{canonicalDumpPath}', " +
						$"dump='{dump.Type}', " +
						$"target='{target.Type}', " +
						$"targetPath='{target.Path}'.");
				}

				if (!usedCanonicalPaths.Add(
					canonicalDumpPath))
				{
					throw new InvalidDataException(
						$"Duplicate dump scalar canonical path '{canonicalDumpPath}' " +
						$"at line {dump.LineNumber}.");
				}

				matches.Add(
					new DumpTargetMatch
					{
						Dump = dump,
						Target = target
					});
			}

			if (equivalentArrayPaths > 0)
			{
				DebugStr(
					$"[TXT] Structural mapping normalized " +
					$"{equivalentArrayPaths} path(s) containing explicit Array nodes.");
			}

			if (requireExactTargetCount &&
				dumpScalars.Count !=
					targetEntries.Count)
			{
				throw new InvalidDataException(
					$"FULL checked mapping count mismatch: " +
					$"dump={dumpScalars.Count}, " +
					$"target={targetEntries.Count}.");
			}

			if (!requireExactTargetCount)
			{
				int extras =
					targetEntries.Count -
					usedCanonicalPaths.Count;

				if (extras > 0)
				{
					DebugStr(
						$"[TXT] FULL structural mapping: " +
						$"{extras} target scalar(s) are not present " +
						$"in the dump; they will remain unchanged.");
				}
			}

			return matches;
		}


		// ============================================================
		// FLOAT COMPARISON
		// ============================================================

		private static bool AreFloatsEqual(
			float expected,
			float actual)
		{
			if (float.IsNaN(expected) &&
				float.IsNaN(actual))
			{
				return true;
			}

			if (float.IsPositiveInfinity(expected) &&
				float.IsPositiveInfinity(actual))
			{
				return true;
			}

			if (float.IsNegativeInfinity(expected) &&
				float.IsNegativeInfinity(actual))
			{
				return true;
			}

			if (float.IsNaN(expected) ||
				float.IsNaN(actual))
			{
				return false;
			}

			float tolerance =
				Math.Max(
					1e-6f,
					Math.Abs(expected) * 1e-6f);

			return Math.Abs(
				expected - actual) <= tolerance;
		}

		private static bool AreDoublesEqual(
			double expected,
			double actual)
		{
			if (double.IsNaN(expected) &&
				double.IsNaN(actual))
			{
				return true;
			}

			if (double.IsPositiveInfinity(expected) &&
				double.IsPositiveInfinity(actual))
			{
				return true;
			}

			if (double.IsNegativeInfinity(expected) &&
				double.IsNegativeInfinity(actual))
			{
				return true;
			}

			if (double.IsNaN(expected) ||
				double.IsNaN(actual))
			{
				return false;
			}

			double tolerance =
				Math.Max(
					1e-12,
					Math.Abs(expected) * 1e-12);

			return Math.Abs(
				expected - actual) <= tolerance;
		}


		// ============================================================
		// FINAL SPRITE VALIDATION
		// ============================================================

		private static void ValidateSpriteStructuralDumpAgainstBaseField(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField)
		{
			List<DumpScalar> dumpScalars =
				ReadDumpScalars(
					inputFile);

			SynchronizeDumpArrayStructure(
				inputFile,
				baseField,
				dumpScalars,
				false);

			HashSet<string> byteArrayPaths =
				ResolveByteArrayPaths(
					inputFile,
					baseField);

			List<DumpTargetMatch> matches =
				BuildStructuralDumpTargetMatchesWithByteArrays(
					dumpScalars,
					baseField,
					byteArrayPaths,
					true);

			DebugStr(
				$"[CHECK] FINAL Sprite scalar structural mapping PASSED: " +
				$"{matches.Count} fields.");

			foreach (DumpTargetMatch match in matches)
			{
				DumpScalar dump =
					match.Dump;

				AssetTypeValueField target =
					match.Target.Field;

				if (target == null ||
					target.Value == null)
				{
					throw new InvalidDataException(
						$"FINAL CHECK: target field is null " +
						$"at '{dump.Path}'.");
				}

				if (target.Value.ValueType ==
					AssetValueType.String)
				{
					string expectedString =
						ParseDumpString(
							dump.Value);

					string actualString =
						target.AsString ?? "";

					if (!string.Equals(
						expectedString,
						actualString,
						StringComparison.Ordinal))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: string mismatch at " +
							$"'{dump.Path}': " +
							$"expectedLength={expectedString.Length}, " +
							$"actualLength={actualString.Length}.");
					}

					continue;
				}

				if (target.Value.ValueType ==
					AssetValueType.Float)
				{
					float expectedFloat =
						ParseSingle(
							dump.Value);

					if (!AreFloatsEqual(
						expectedFloat,
						target.AsFloat))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: float mismatch at " +
							$"'{dump.Path}': " +
							$"dump='{dump.Value}' " +
							$"actual='" +
							target.AsFloat.ToString(
								"R",
								CultureInfo.InvariantCulture) +
							"'.");
					}

					continue;
				}

				if (target.Value.ValueType ==
					AssetValueType.Double)
				{
					double expectedDouble =
						ParseDouble(
							dump.Value);

					if (!AreDoublesEqual(
						expectedDouble,
						target.AsDouble))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: double mismatch at " +
							$"'{dump.Path}': " +
							$"dump='{dump.Value}' " +
							$"actual='" +
							target.AsDouble.ToString(
								"R",
								CultureInfo.InvariantCulture) +
							"'.");
					}

					continue;
				}

				string actualValue =
					ReadFieldAsDumpValue(
						target);

				if (target.Value.ValueType ==
						AssetValueType.UInt8 ||
					target.Value.ValueType ==
						AssetValueType.Int8 ||
					target.Value.ValueType ==
						AssetValueType.UInt16 ||
					target.Value.ValueType ==
						AssetValueType.Int16 ||
					target.Value.ValueType ==
						AssetValueType.UInt32 ||
					target.Value.ValueType ==
						AssetValueType.Int32 ||
					target.Value.ValueType ==
						AssetValueType.UInt64 ||
					target.Value.ValueType ==
						AssetValueType.Int64)
				{
					string normalizedDump =
						NormalizeNumericLiteral(
							dump.Value);

					string normalizedActual =
						NormalizeNumericLiteral(
							actualValue);

					if (!NumericStringsEqual(
						normalizedDump,
						normalizedActual,
						target.Value.ValueType))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: numeric mismatch at " +
							$"'{dump.Path}': " +
							$"dump='{dump.Value}' " +
							$"actual='{actualValue}'.");
					}
				}
				else if (!string.Equals(
					actualValue,
					dump.Value,
					StringComparison.Ordinal))
				{
					throw new InvalidDataException(
						$"FINAL CHECK: value mismatch at " +
						$"'{dump.Path}': " +
						$"dump='{dump.Value}' " +
						$"actual='{actualValue}'.");
				}
			}
		}


		// ============================================================
		// FINAL DUMP VALIDATION
		// ============================================================

		private static void ValidateDumpAgainstBaseField(
			string inputFile,
			AssetsTools.NET.AssetTypeValueField baseField)
		{
			if (IsSpriteBaseField(
				baseField))
			{
				ValidateSpriteStructuralDumpAgainstBaseField(
					inputFile,
					baseField);

				return;
			}

			var matches =
				BuildDumpTargetMatches(
					inputFile,
					baseField,
					true);

			DebugStr(
				$"[CHECK] FINAL dump structural mapping passed: " +
				$"{matches.Count} fields.");

			foreach (var match in matches)
			{
				DumpScalar dump =
					match.Dump;

				AssetTypeValueField target =
					match.Target.Field;

				if (target == null ||
					target.Value == null)
				{
					throw new InvalidDataException(
						$"FINAL CHECK: target field is null " +
						$"at '{dump.Path}'.");
				}

				if (target.Value.ValueType ==
					AssetValueType.String)
				{
					string expectedString =
						ParseDumpString(
							dump.Value);

					string actualString =
						target.AsString ?? "";

					if (!string.Equals(
						expectedString,
						actualString,
						StringComparison.Ordinal))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: string mismatch at " +
							$"'{dump.Path}': " +
							$"expectedLength={expectedString.Length}, " +
							$"actualLength={actualString.Length}.");
					}

					continue;
				}

				if (target.Value.ValueType ==
					AssetValueType.Float)
				{
					float expectedFloat =
						ParseSingle(
							dump.Value);

					if (!AreFloatsEqual(
						expectedFloat,
						target.AsFloat))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: float mismatch at " +
							$"'{dump.Path}': " +
							$"dump='{dump.Value}' " +
							$"actual='" +
							target.AsFloat.ToString(
								"R",
								CultureInfo.InvariantCulture) +
							"'.");
					}

					continue;
				}

				if (target.Value.ValueType ==
					AssetValueType.Double)
				{
					double expectedDouble =
						ParseDouble(
							dump.Value);

					if (!AreDoublesEqual(
						expectedDouble,
						target.AsDouble))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: double mismatch at " +
							$"'{dump.Path}': " +
							$"dump='{dump.Value}' " +
							$"actual='" +
							target.AsDouble.ToString(
								"R",
								CultureInfo.InvariantCulture) +
							"'.");
					}

					continue;
				}

				string actualValue =
					ReadFieldAsDumpValue(
						target);

				if (target.Value.ValueType ==
						AssetValueType.UInt8 ||
					target.Value.ValueType ==
						AssetValueType.Int8 ||
					target.Value.ValueType ==
						AssetValueType.UInt16 ||
					target.Value.ValueType ==
						AssetValueType.Int16 ||
					target.Value.ValueType ==
						AssetValueType.UInt32 ||
					target.Value.ValueType ==
						AssetValueType.Int32 ||
					target.Value.ValueType ==
						AssetValueType.UInt64 ||
					target.Value.ValueType ==
						AssetValueType.Int64)
				{
					string normalizedDump =
						NormalizeNumericLiteral(
							dump.Value);

					string normalizedActual =
						NormalizeNumericLiteral(
							actualValue);

					if (!NumericStringsEqual(
						normalizedDump,
						normalizedActual,
						target.Value.ValueType))
					{
						throw new InvalidDataException(
							$"FINAL CHECK: numeric mismatch at " +
							$"'{dump.Path}': " +
							$"dump='{dump.Value}' " +
							$"actual='{actualValue}'.");
					}
				}
				else if (!string.Equals(
					actualValue,
					dump.Value,
					StringComparison.Ordinal))
				{
					throw new InvalidDataException(
						$"FINAL CHECK: value mismatch at " +
						$"'{dump.Path}': " +
						$"dump='{dump.Value}' " +
						$"actual='{actualValue}'.");
				}
			}
		}


		// ============================================================
		// NUMERIC COMPARISON
		// ============================================================

		private static bool NumericStringsEqual(
			string expected,
			string actual,
			AssetValueType type)
		{
			try
			{
				switch (type)
				{
					case AssetValueType.UInt8:
					case AssetValueType.UInt16:
					case AssetValueType.UInt32:
						return
							uint.Parse(
								expected,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture)
							==
							uint.Parse(
								actual,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture);

					case AssetValueType.Int8:
					case AssetValueType.Int16:
					case AssetValueType.Int32:
						return
							int.Parse(
								expected,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture)
							==
							int.Parse(
								actual,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture);

					case AssetValueType.UInt64:
						return
							ulong.Parse(
								expected,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture)
							==
							ulong.Parse(
								actual,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture);

					case AssetValueType.Int64:
						return
							long.Parse(
								expected,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture)
							==
							long.Parse(
								actual,
								NumberStyles.Integer,
								CultureInfo.InvariantCulture);

					default:
						return string.Equals(
							expected,
							actual,
							StringComparison.Ordinal);
				}
			}
			catch
			{
				return string.Equals(
					expected,
					actual,
					StringComparison.Ordinal);
			}
		}


		// ============================================================
		// READ FIELD VALUE
		// ============================================================

		private static string ReadFieldAsDumpValue(
			AssetsTools.NET.AssetTypeValueField field)
		{
			if (field == null ||
				field.Value == null)
			{
				return "";
			}

			switch (field.Value.ValueType)
			{
				case AssetValueType.Bool:
					return field.AsBool
						? "true"
						: "false";

				case AssetValueType.UInt8:
					return field.AsUInt.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.Int8:
					return field.AsInt.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.UInt16:
					return field.AsUInt.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.Int16:
					return field.AsInt.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.UInt32:
					return field.AsUInt.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.Int32:
					return field.AsInt.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.UInt64:
					return field.AsULong.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.Int64:
					return field.AsLong.ToString(
						CultureInfo.InvariantCulture);

				case AssetValueType.Float:
					return field.AsFloat.ToString(
						"R",
						CultureInfo.InvariantCulture);

				case AssetValueType.Double:
					return field.AsDouble.ToString(
						"R",
						CultureInfo.InvariantCulture);

				case AssetValueType.String:
					return field.AsString ?? "";

				default:
					throw new NotSupportedException(
						"Unsupported field type in final validation: " +
						field.Value.ValueType);
			}
		}
	}
}