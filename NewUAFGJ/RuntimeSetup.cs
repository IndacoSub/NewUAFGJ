using System;
using System.IO;
using System.Linq;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Cpp2IL;

namespace UAFGJ;

internal static class RuntimeSetup
{
    public static void Configure(AssetsManager manager, string sourcePath)
    {
        string? managedDir = FindManagedDirectory(sourcePath);
        if (managedDir == null)
        {
            Console.WriteLine("[MONO] Managed directory not found; MonoCecil generator will be unavailable.");
        }
        else
        {
            try
            {
                manager.MonoTempGenerator = new MonoCecilTempGenerator(managedDir);
                Console.WriteLine($"[MONO] MonoCecil generator configured: {managedDir}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONO] MonoCecil setup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        string? dataDir = FindUnityDataDirectory(sourcePath);
        if (dataDir != null)
        {
            try
            {
                FindCpp2IlFilesResult il2cpp = FindCpp2IlFiles.Find(dataDir);
                if (il2cpp.success)
                {
                    manager.MonoTempGenerator = new Cpp2IlTempGenerator(il2cpp.metaPath, il2cpp.asmPath);
                    Console.WriteLine($"[IL2CPP] Cpp2IL generator configured: {dataDir}");
                    return;
                }

                Console.WriteLine("[IL2CPP] Unity data directory found, but Cpp2IL files were not detected.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IL2CPP] Cpp2IL setup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine("[MONO] No automatic MonoBehaviour template generator could be configured.");
        Console.WriteLine("[MONO] A bundle with an embedded TypeTree can still be processed normally.");
    }

    private static string? FindManagedDirectory(string sourcePath)
    {
        DirectoryInfo? dir = new FileInfo(sourcePath).Directory;
        while (dir != null)
        {
            string directManaged = Path.Combine(dir.FullName, "Managed");
            if (LooksLikeManagedDirectory(directManaged))
                return directManaged;

            foreach (string child in Directory.EnumerateDirectories(dir.FullName, "*_Data", SearchOption.TopDirectoryOnly))
            {
                string managed = Path.Combine(child, "Managed");
                if (LooksLikeManagedDirectory(managed))
                    return managed;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindUnityDataDirectory(string sourcePath)
    {
        DirectoryInfo? dir = new FileInfo(sourcePath).Directory;
        while (dir != null)
        {
            string candidate = dir.FullName;
            if (LooksLikeUnityDataDirectory(candidate))
                return candidate;

            foreach (string child in Directory.EnumerateDirectories(dir.FullName, "*_Data", SearchOption.TopDirectoryOnly))
            {
                if (LooksLikeUnityDataDirectory(child))
                    return child;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool LooksLikeManagedDirectory(string path)
    {
        return Directory.Exists(path) &&
               Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool LooksLikeUnityDataDirectory(string path)
    {
        if (!Directory.Exists(path))
            return false;

        string managed = Path.Combine(path, "Managed");
        string streaming = Path.Combine(path, "StreamingAssets");
        return Directory.Exists(managed) || Directory.Exists(streaming) ||
               File.Exists(Path.Combine(managed, "global-metadata.dat"));
    }
}
