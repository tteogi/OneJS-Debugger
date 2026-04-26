using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace OnejsDebugger.Editor
{
    /// <summary>
    /// Editor utility that manages the native plugin set inside this package.
    ///
    /// The package ships with its own Plugins/ folder (debugger-enabled build).
    /// Users can roll back to the stock OneJS plugins (DefaultPlugins~/onejs-plugins.zip)
    /// or re-install the debugger build (OnejsDebuggerPlugins~/onejs-debugger-plugins.zip).
    /// Every swap snapshots the current Plugins/ to Library/OnejsDebugger/Backup/
    /// so the operation is reversible even if both source zips are missing.
    /// </summary>
    public static class PluginSwap
    {
        const string PackageName = "com.yten.onejs-debugger";

        const string DebuggerZipRel = "OnejsDebuggerPlugins~/onejs-debugger-plugins.zip";
        const string DefaultZipRel = "DefaultPlugins~/onejs-plugins.zip";
        const string EditorPrefMode = "OnejsDebugger.PluginMode"; // "Default" | "Debugger"
        const int MaxBackups = 5;

        // ------------------------------------------------------------------
        // Menu items
        // ------------------------------------------------------------------
        [MenuItem("OneJS/Debugger/Install Debugger Plugin", priority = 1)]
        public static void InstallDebugger()
        {
            try
            {
                var pluginsDir = ResolveOwnPluginsDir();
                if (pluginsDir == null) return;

                var debuggerZip = ResolvePackageFile(DebuggerZipRel);
                if (debuggerZip == null)
                {
                    EditorUtility.DisplayDialog("OnejsDebugger",
                        $"Debugger plugins zip not found in package:\n  {DebuggerZipRel}\n\n" +
                        "Did you install the package from a GitHub Release tarball " +
                        "(which bundles built plugins)?",
                        "OK");
                    return;
                }

                EditorUtility.DisplayProgressBar("OnejsDebugger",
                    "Backing up current OneJS plugins…", 0.1f);
                BackupPlugins(pluginsDir, "onejs-plugins");

                EditorUtility.DisplayProgressBar("OnejsDebugger",
                    "Extracting debugger plugins…", 0.5f);
                // Overwrite only — do NOT clear first.
                // Platforms absent from the debugger zip (e.g. WebGL) are left
                // untouched so users don't lose those files.
                ExtractZip(debuggerZip, pluginsDir);

                EditorPrefs.SetString(EditorPrefMode, "Debugger");
                EditorUtility.DisplayProgressBar("OnejsDebugger", "Refreshing assets…", 0.9f);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"[OnejsDebugger] Installed debugger plugins to {pluginsDir}");

                // Native libraries are loaded once by the OS and cannot be swapped
                // without restarting the process. Prompt for restart.
                bool restart = EditorUtility.DisplayDialog(
                    "OnejsDebugger — Restart Required",
                    "Debugger plugins installed successfully!\n\n" +
                    "Unity must be restarted to load the new native library.\n\n" +
                    "Restart now?",
                    "Restart Unity", "Later");
                if (restart)
                    EditorApplication.OpenProject(System.IO.Directory.GetCurrentDirectory());
            }
            catch (Exception e)
            {
                Debug.LogError($"[OnejsDebugger] InstallDebugger failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("OneJS/Debugger/Rollback to OneJS", priority = 2)]
        public static void RollbackToOneJS()
        {
            try
            {
                var pluginsDir = ResolveOwnPluginsDir();
                if (pluginsDir == null) return;

                EditorUtility.DisplayProgressBar("OnejsDebugger",
                    "Backing up current debugger plugins…", 0.1f);
                BackupPlugins(pluginsDir, "debugger-plugins");

                // Prefer the most-recent user backup, then fall back to the bundled snapshot.
                var src = LatestBackup("onejs-plugins") ?? ResolvePackageFile(DefaultZipRel);
                if (src == null)
                {
                    EditorUtility.DisplayDialog("OnejsDebugger",
                        "No OneJS plugins backup found and no bundled fallback in:\n  " +
                        DefaultZipRel + "\n\nCannot rollback.", "OK");
                    return;
                }

                EditorUtility.DisplayProgressBar("OnejsDebugger",
                    $"Restoring {Path.GetFileName(src)}…", 0.5f);
                ClearOneJSPluginsContents(pluginsDir);
                ExtractZip(src, pluginsDir);

                EditorPrefs.SetString(EditorPrefMode, "Default");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"[OnejsDebugger] Rolled back OneJS plugins from {src}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[OnejsDebugger] Rollback failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("OneJS/Debugger/Open Backup Folder", priority = 3)]
        public static void OpenBackupFolder()
        {
            var dir = BackupDir();
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        // Zips Packages/com.yten.onejs-debugger/Plugins/ (including any .meta files
        // already on disk) into OnejsDebuggerPlugins~/onejs-debugger-plugins.zip.
        [MenuItem("OneJS/Debugger/Repackage Debugger Plugins", priority = 10)]
        public static void RepackageDebuggerPlugins()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(PluginSwap).Assembly);
                if (info == null)
                {
                    Debug.LogError("[OnejsDebugger] Could not resolve package path.");
                    return;
                }

                var pluginsDir = Path.Combine(info.resolvedPath, "Plugins");
                if (!Directory.Exists(pluginsDir))
                {
                    Debug.LogError($"[OnejsDebugger] Plugins folder not found: {pluginsDir}");
                    return;
                }

                var destZip = Path.Combine(info.resolvedPath,
                    "OnejsDebuggerPlugins~", "onejs-debugger-plugins.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(destZip));

                CreateZipFromDirectoryWithMeta(pluginsDir, destZip);
                AssetDatabase.Refresh();
                Debug.Log($"[OnejsDebugger] Repackaged → {destZip}");
                EditorUtility.RevealInFinder(destZip);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OnejsDebugger] Repackage failed: {e}");
            }
        }

        [MenuItem("OneJS/Debugger/Install Debugger Plugin", validate = true)]
        static bool ValidateInstall()
        {
            return EditorPrefs.GetString(EditorPrefMode, "Default") != "Debugger";
        }

        [MenuItem("OneJS/Debugger/Rollback to OneJS", validate = true)]
        static bool ValidateRollback()
        {
            return EditorPrefs.GetString(EditorPrefMode, "Default") == "Debugger";
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------
        public static string CurrentMode => EditorPrefs.GetString(EditorPrefMode, "Default");

        static string ResolveOwnPluginsDir()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(PluginSwap).Assembly);
            if (info != null)
            {
                var p = Path.Combine(info.resolvedPath, "Plugins");
                if (Directory.Exists(p)) return Path.GetFullPath(p);
            }

            var fallback = Path.Combine(Directory.GetCurrentDirectory(),
                "Packages", PackageName, "Plugins");
            if (Directory.Exists(fallback)) return Path.GetFullPath(fallback);

            EditorUtility.DisplayDialog("OnejsDebugger",
                "Could not locate the Plugins folder in the onejs-debugger package.", "OK");
            return null;
        }

        static string ResolvePackageFile(string relPath)
        {
            // PackageInfo.FindForAssembly resolves the real on-disk path regardless
            // of whether the package is embedded (Packages/), git-cached
            // (Library/PackageCache/), or registry-installed.
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(PluginSwap).Assembly);
            if (info != null)
            {
                var p = Path.Combine(info.resolvedPath, relPath);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }

            // Fallback for embedded packages during local development.
            var fallback = Path.Combine(Directory.GetCurrentDirectory(),
                "Packages", PackageName, relPath);
            return File.Exists(fallback) ? Path.GetFullPath(fallback) : null;
        }

        static string BackupDir()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "Library",
                "OnejsDebugger", "Backup");
        }

        static void BackupPlugins(string pluginsDir, string label)
        {
            var dir = BackupDir();
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dest = Path.Combine(dir, $"{label}-{stamp}.zip");
            CreateZipFromDirectory(pluginsDir, dest);
            PruneOldBackups(dir, label, MaxBackups);
        }

        static string LatestBackup(string label)
        {
            var dir = BackupDir();
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, $"{label}-*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        static void PruneOldBackups(string dir, string label, int keep)
        {
            var files = Directory.EnumerateFiles(dir, $"{label}-*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
        }

        static void CreateZipFromDirectory(string sourceDir, string destZip)
        {
            if (File.Exists(destZip)) File.Delete(destZip);
            ZipFile.CreateFromDirectory(sourceDir, destZip,
                System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        // Like CreateZipFromDirectory but also includes .meta files that sit
        // alongside assets (Unity stores them as <asset>.meta in the same folder).
        // ZipFile.CreateFromDirectory already includes all files in the tree, so
        // .meta files are captured automatically — this method exists to make the
        // intent explicit and to exclude .gitkeep files.
        static void CreateZipFromDirectoryWithMeta(string sourceDir, string destZip)
        {
            if (File.Exists(destZip)) File.Delete(destZip);
            using var archive = ZipFile.Open(destZip, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name == ".gitkeep") continue;

                var entryName = file.Substring(sourceDir.Length + 1).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName,
                    System.IO.Compression.CompressionLevel.Optimal);
            }
        }

        static void ExtractZip(string zip, string destDir)
        {
            // Use ExtractToDirectory with overwriteFiles=true (.NET 4.7.2+ / Unity).
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(destDir), StringComparison.Ordinal))
                {
                    throw new IOException($"Zip entry escapes target dir: {entry.FullName}");
                }
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        static void ClearOneJSPluginsContents(string pluginsDir)
        {
            // Wipe everything *inside* but keep the folder + its .meta.
            foreach (var sub in Directory.EnumerateDirectories(pluginsDir))
            {
                Directory.Delete(sub, recursive: true);
                var meta = sub + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
            foreach (var f in Directory.EnumerateFiles(pluginsDir))
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(f);
                var meta = f + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
        }
    }
}
