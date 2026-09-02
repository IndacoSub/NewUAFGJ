using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace UAFGJ
{
    partial class Program
    {
        static void Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            try
            {
                DebugStr("[BOOT] ==================================================");
                DebugStr("[BOOT] UAFGJ starting.");
                DebugStr($"[BOOT] PID={Environment.ProcessId}, OS={Environment.OSVersion}, 64bit={Environment.Is64BitProcess}");
                DebugStr($"[BOOT] BaseDirectory='{AppContext.BaseDirectory}'");
                DebugStr($"[BOOT] CurrentDirectory='{Environment.CurrentDirectory}'");
                DebugStr($"[BOOT] Args count={(args == null ? -1 : args.Length)}");

                if (args == null)
                {
                    DisplayStr("Null args!");
                    return;
                }

                if (args.Length < 2)
                {
                    DisplayStr("Usage: UAFGJ.exe <bundle/assets> <input.txt/png> [pathId]");
                    return;
                }

                string assetOrBundle = args[0].Replace('\\', '/');
                string inputFile = args[1].Replace('\\', '/');
                string pathId = args.Length >= 3 ? args[2].Trim() : string.Empty;
                string fileKind = args.Length >= 4 ? args[3].Trim() : string.Empty;

                DebugStr($"[BOOT] Input path='{assetOrBundle}'");
                DebugStr($"[BOOT] Replacement path='{inputFile}'");
                DebugStr($"[BOOT] PathID='{pathId}'");
                DebugStr($"[BOOT] FileKind='{fileKind}'");

                if (!File.Exists(assetOrBundle))
                {
                    DisplayStr("Input bundle/assets file not found: " + assetOrBundle);
                    return;
                }

                if (!File.Exists(inputFile))
                {
                    DisplayStr("Input replacement file not found: " + inputFile);
                    return;
                }

                LogFileState("[BOOT] INPUT", assetOrBundle);
                LogFileState("[BOOT] REPLACEMENT", inputFile);

                DisplayStr("File exists: " + assetOrBundle);
                DisplayStr("Replacement exists: " + inputFile);
                if (!string.IsNullOrWhiteSpace(pathId))
                {
                    DisplayStr("Requested path ID: " + pathId);
                }
                if (!string.IsNullOrWhiteSpace(fileKind))
                {
                    DisplayStr("File kind: " + fileKind);
                }

                DebugStr("[BOOT] Entering DoStuff().");
                DoStuff(assetOrBundle, inputFile, pathId, fileKind);
                DebugStr($"[BOOT] DoStuff() returned. ExitCode={Environment.ExitCode}");
            }
            catch (Exception ex)
            {
                DisplayStr("[FATAL] " + ex.GetType().Name + ": " + ex.Message);
                DebugStr(ex.ToString());
                Environment.ExitCode = 1;
            }
            finally
            {
                DebugStr($"[BOOT] UAFGJ exiting. ExitCode={Environment.ExitCode}");
                DebugStr("[BOOT] ==================================================");
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    DisplayStr("[UNHANDLED] Unhandled exception detected.");
                    DebugStr($"[UNHANDLED] Type={ex.GetType().FullName}");
                    DebugStr($"[UNHANDLED] Message={ex.Message}");
                    DebugStr(ex.ToString());
                }
                else
                {
                    DisplayStr("[UNHANDLED] Unhandled non-Exception object detected: " + (e.ExceptionObject?.GetType().FullName ?? "<null>"));
                    DebugStr($"[UNHANDLED] Object={e.ExceptionObject}");
                }

                DebugStr($"[UNHANDLED] IsTerminating={e.IsTerminating}");
            }
            catch
            {
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            try
            {
                DebugStr($"[BOOT] ProcessExit event. ExitCode={Environment.ExitCode}");
            }
            catch
            {
            }
        }

        static private void DoStuff(string assetOrBundle, string inputFile, string specificPathId, string fileKind)
        {
            DebugStr("[PHASE] DoStuff: starting file detection.");
            DebugStr("Opening file: " + assetOrBundle);

            DetectedFileType fileType;
            try
            {
                fileType = FileTypeDetector.DetectFileType(assetOrBundle);
            }
            catch (Exception ex)
            {
                DisplayStr("[FATAL] File type detection failed: " + ex.GetType().Name + ": " + ex.Message);
                DebugStr(ex.ToString());
                Environment.ExitCode = 1;
                return;
            }

            DebugStr("Detected file type: " + fileType);

            switch (fileType)
            {
                case DetectedFileType.BundleFile:
                    DebugStr("[PHASE] Dispatching to HandleBundle().");
                    HandleBundle(assetOrBundle, inputFile, specificPathId, fileKind);
                    break;

                case DetectedFileType.AssetsFile:
                    DebugStr("[PHASE] Dispatching to HandleAsset().");
                    HandleAsset(assetOrBundle, inputFile, specificPathId, fileKind);
                    break;

                default:
                    DisplayStr("Invalid file type for " + assetOrBundle + ": " + fileType);
                    Environment.ExitCode = 1;
                    break;
            }
        }
    }
}
