using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace OnejsDebugger.Editor
{
    /// <summary>
    /// Editor utility that swaps OneJS's native plugins between the stock
    /// build and the debugger-enabled build via zip backup + extract.
    ///
    /// Why this approach:
    ///   The OnejsDebugger plugin produces a libquickjs_unity with the same
    ///   ABI superset as OneJS's. Unity disallows two native libs with the
    ///   same name across packages, so we never ship Plugins/ in the package
    ///   itself — we keep both sets as zip archives in Hidden (~) folders
    ///   and let the user toggle which one lives at OneJS/Plugins/.
    ///   Every swap snapshots the current Plugins/ to Library/OnejsDebugger/Backup/
    ///   so the operation is reversible even if both source zips are missing.
    /// </summary>
    public static class PluginSwap
    {
        const string PackageName = "com.yten.onejs-debugger";
        const string OneJSPackageName = "com.singtaa.onejs";

        static EmbedRequest s_EmbedRequest;
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
                var pluginsDir = ResolveOneJSPluginsDir();
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
                var pluginsDir = ResolveOneJSPluginsDir();
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

        // Zips Packages/com.yten.onejs-debugger/Plugins/ (including .meta files
        // Unity has generated) into OnejsDebuggerPlugins~/onejs-debugger-plugins.zip.
        // Run this after opening Unity so that .meta files exist for all plugins.
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

        [MenuItem("OneJS/Debugger/Embed OneJS for Debugging", priority = 5)]
        public static void EmbedOneJS()
        {
            if (s_EmbedRequest != null)
            {
                Debug.Log("[OnejsDebugger] Embed already in progress…");
                return;
            }
            Debug.Log("[OnejsDebugger] Embedding OneJS — please wait…");
            s_EmbedRequest = Client.Embed(OneJSPackageName);
            EditorApplication.update += OnEmbedProgress;
        }

        static void OnEmbedProgress()
        {
            if (s_EmbedRequest == null || !s_EmbedRequest.IsCompleted) return;
            EditorApplication.update -= OnEmbedProgress;
            var req = s_EmbedRequest;
            s_EmbedRequest = null;

            if (req.Status == StatusCode.Success)
            {
                Debug.Log($"[OnejsDebugger] OneJS embedded → {req.Result.resolvedPath}");
                bool install = EditorUtility.DisplayDialog("OnejsDebugger",
                    "OneJS has been embedded.\n\nInstall the debugger plugin now?",
                    "Install", "Later");
                if (install) InstallDebugger();
            }
            else
            {
                Debug.LogError($"[OnejsDebugger] Embed failed: {req.Error?.message}");
                EditorUtility.DisplayDialog("OnejsDebugger — Embed Failed",
                    $"Could not embed OneJS:\n{req.Error?.message}\n\n" +
                    "Try manually: Window → Package Manager → OneJS → ⋮ → Embed", "OK");
            }
        }

        [MenuItem("OneJS/Debugger/Embed OneJS for Debugging", validate = true)]
        static bool ValidateEmbed()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(OneJSPackageName);
            if (info == null) return false;
            return info.source == PackageSource.Git
                || info.source == PackageSource.Registry
                || info.source == PackageSource.LocalTarball;
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

        static string ResolveOneJSPluginsDir()
        {
            // Primary: ask UPM for the OneJS package regardless of install method.
            var oneJSInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(OneJSPackageName);
            if (oneJSInfo != null)
            {
                var pluginsPath = Path.GetFullPath(Path.Combine(oneJSInfo.resolvedPath, "Plugins"));
                if (Directory.Exists(pluginsPath))
                {
                    // Immutable packages (git/registry/tarball) live in Library/PackageCache and
                    // cannot be written to. The user must embed the package first.
                    bool isImmutable = oneJSInfo.source == UnityEditor.PackageManager.PackageSource.Git
                                    || oneJSInfo.source == UnityEditor.PackageManager.PackageSource.Registry
                                    || oneJSInfo.source == UnityEditor.PackageManager.PackageSource.LocalTarball;
                    if (isImmutable)
                    {
                        bool embed = EditorUtility.DisplayDialog(
                            "OnejsDebugger — OneJS is read-only",
                            $"OneJS ({oneJSInfo.source}) is installed as an immutable package and cannot be modified.\n\n" +
                            "The debugger plugin swap requires OneJS to be embedded in the project " +
                            "(copied into Packages/com.singtaa.onejs/).\n\n" +
                            "Embed OneJS now? This calls Package Manager Embed and may take a moment.",
                            "Embed & Continue", "Cancel");
                        if (!embed) return null;
                        EmbedOneJS();
                        return null; // caller will retry after embed completes
                    }
                    return pluginsPath;
                }
            }

            // Fallback: filesystem candidates (embedded / manual / submodule layouts).
            var root = Directory.GetCurrentDirectory();
            var candidates = new[] {
                Path.Combine(root, "Packages", OneJSPackageName, "Plugins"),
                Path.Combine(root, "Assets", OneJSPackageName, "Plugins"),
                Path.Combine(root, "Assets", "OneJS", "Plugins"),
                Path.Combine(root, "OneJS", "Plugins"),
            };
            foreach (var p in candidates)
            {
                if (Directory.Exists(p)) return Path.GetFullPath(p);
            }

            EditorUtility.DisplayDialog("OnejsDebugger",
                "Could not locate the OneJS Plugins folder.\n\n" +
                "Make sure OneJS is installed and embedded via the Package Manager\n" +
                "(Window → Package Manager → OneJS → ⋮ → Embed).",
                "OK");
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
