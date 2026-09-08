using AssetsTools.NET.Extra;
using AssetsTools.NET;
using System.IO;

namespace UAFGJ
{
    partial class Program
    {
        private static AssetsFileInstance GetAssetInst(AssetsManager am, BundleFileInstance bundleInst, string assetfile_name, string ab)
        {
            // Load from index instead of name for now
            AssetsFileInstance assetInst = am.LoadAssetsFileFromBundle(bundleInst, 0, true);
            if (assetInst == null)
            {
                DisplayStr("Could not load asset file for " + assetfile_name + " in " + ab);
            }
            else
            {
                DebugStr("Loaded assetInst for " + assetfile_name);
            }
            return assetInst;
        }

        private static string GetRightAssetFileNameFromBundle(BundleFileInstance bundleInst, string ab)
        {
            string assetfile_name = "";
            int cont = 0;
            foreach (var i in bundleInst.file.BlockAndDirInfo.DirectoryInfos)
            {
                DebugStr("Found asset file: " + i.Name);
                if (i.Name.EndsWith(".resS"))
                {
                    continue;
                }
                if (i.Name.EndsWith(".resource"))
                {
                    continue;
                }
                DebugStr("Found good? asset file: " + i.Name);
                assetfile_name = i.Name;
                cont++;

                if (i.Name.EndsWith(".sharedAssets"))
                {
                    break;
                }
            }

            if (cont >= 2)
            {
                DisplayStr("More than 2 assets file found in " + ab + " (UNIMPLEMENTED)!");
                return string.Empty;
            }
            return assetfile_name;
        }

        private static BundleFileInstance GetBundleInst(AssetsManager am, string ab)
        {
            BundleFileInstance bundleInst = am.LoadBundleFile(ab, true);
            if (bundleInst == null)
            {
                DisplayStr("Could not load bundle file for " + ab);
            }
            return bundleInst;
        }

        private static void DecompressToMemory(BundleFileInstance bundleInst)
        {
            AssetBundleFile bundle = bundleInst.file;

            MemoryStream bundleStream = new MemoryStream();
            bundle.Unpack(new AssetsFileWriter(bundleStream));

            bundleStream.Position = 0;

            AssetBundleFile newBundle = new AssetBundleFile();
            newBundle.Read(new AssetsFileReader(bundleStream));

            bundle.Reader.Close();
            bundleInst.file = newBundle;
        }
        private static void EnsureClassDatabaseIfNeeded(AssetsManager am, AssetsFileInstance assetInst)
        {
            if (assetInst.file.Metadata.TypeTreeEnabled)
            {
                DebugStr("[TYPE] Embedded TypeTree enabled; external classdata.tpk is not required.");
                return;
            }

            string tpk = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
            if (!File.Exists(tpk))
            {
                DebugStr("[TYPE] classdata.tpk not found. Continuing because AssetsTools.NET v3 can use Mono/IL2CPP generators for MonoBehaviours.");
                return;
            }

            try
            {
                am.LoadClassPackage(tpk);
                am.LoadClassDatabaseFromPackage(assetInst.file.Metadata.UnityVersion);
                DebugStr($"[TYPE] Loaded external classdata.tpk for Unity {assetInst.file.Metadata.UnityVersion}.");
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"classdata.tpk is present but could not be loaded by AssetsTools.NET 3.x: {ex.Message}", ex);
            }
        }

    }
}