using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OnejsDebugger.Editor {
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
    public static class PluginSwap {
        const string PackageName = "com.yten.onejs-debugger";
        const string OneJSPackageName = "com.singtaa.onejs";
        const string DebuggerZipRel = "OnejsDebuggerPlugins~/onejs-debugger-plugins.zip";
        const string DefaultZipRel = "DefaultPlugins~/onejs-plugins.zip";
        const string EditorPrefMode = "OnejsDebugger.PluginMode"; // "Default" | "Debugger"
        const int MaxBackups = 5;

        // ------------------------------------------------------------------
        // Menu items
        // ------------------------------------------------------------------
        [MenuItem("OneJS/Debugger/Install Debugger Plugin", priority = 1)]
        public static void InstallDebugger() {
            try {
                var pluginsDir = ResolveOneJSPluginsDir();
                if (pluginsDir == null) return;

                var debuggerZip = ResolvePackageFile(DebuggerZipRel);
                if (debuggerZip == null) {
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
                ClearOneJSPluginsContents(pluginsDir);
                ExtractZip(debuggerZip, pluginsDir);

                EditorPrefs.SetString(EditorPrefMode, "Debugger");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"[OnejsDebugger] Installed debugger plugins to {pluginsDir}");
            } catch (Exception e) {
                Debug.LogError($"[OnejsDebugger] InstallDebugger failed: {e}");
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("OneJS/Debugger/Rollback to OneJS", priority = 2)]
        public static void RollbackToOneJS() {
            try {
                var pluginsDir = ResolveOneJSPluginsDir();
                if (pluginsDir == null) return;

                EditorUtility.DisplayProgressBar("OnejsDebugger",
                    "Backing up current debugger plugins…", 0.1f);
                BackupPlugins(pluginsDir, "debugger-plugins");

                // Prefer the most-recent user backup, then fall back to the bundled snapshot.
                var src = LatestBackup("onejs-plugins") ?? ResolvePackageFile(DefaultZipRel);
                if (src == null) {
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
            } catch (Exception e) {
                Debug.LogError($"[OnejsDebugger] Rollback failed: {e}");
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("OneJS/Debugger/Open Backup Folder", priority = 3)]
        public static void OpenBackupFolder() {
            var dir = BackupDir();
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("OneJS/Debugger/Install Debugger Plugin", validate = true)]
        static bool ValidateInstall() {
            return EditorPrefs.GetString(EditorPrefMode, "Default") != "Debugger";
        }

        [MenuItem("OneJS/Debugger/Rollback to OneJS", validate = true)]
        static bool ValidateRollback() {
            return EditorPrefs.GetString(EditorPrefMode, "Default") == "Debugger";
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------
        public static string CurrentMode => EditorPrefs.GetString(EditorPrefMode, "Default");

        static string ResolveOneJSPluginsDir() {
            // UPM resolves any installed package (registry / git / local / embedded)
            // to a virtual `Packages/<name>/...` path that's always real on disk
            // for editor-side I/O. Try that, then fall back to the in-repo
            // submodule layout used during local development.
            var candidates = new[] {
                Path.Combine(Directory.GetCurrentDirectory(), "Packages", OneJSPackageName, "Plugins"),
                Path.Combine(Directory.GetCurrentDirectory(), "OneJS", "Plugins"),
            };
            foreach (var p in candidates) {
                if (Directory.Exists(p)) return Path.GetFullPath(p);
            }
            EditorUtility.DisplayDialog("OnejsDebugger",
                $"Could not locate the OneJS package's Plugins folder.\n" +
                $"Looked in:\n  {string.Join("\n  ", candidates)}\n\n" +
                $"Make sure {OneJSPackageName} is installed.", "OK");
            return null;
        }

        static string ResolvePackageFile(string relPath) {
            var p = Path.Combine(Directory.GetCurrentDirectory(),
                "Packages", PackageName, relPath);
            return File.Exists(p) ? Path.GetFullPath(p) : null;
        }

        static string BackupDir() {
            return Path.Combine(Directory.GetCurrentDirectory(), "Library",
                "OnejsDebugger", "Backup");
        }

        static void BackupPlugins(string pluginsDir, string label) {
            var dir = BackupDir();
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dest = Path.Combine(dir, $"{label}-{stamp}.zip");
            CreateZipFromDirectory(pluginsDir, dest);
            PruneOldBackups(dir, label, MaxBackups);
        }

        static string LatestBackup(string label) {
            var dir = BackupDir();
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, $"{label}-*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        static void PruneOldBackups(string dir, string label, int keep) {
            var files = Directory.EnumerateFiles(dir, $"{label}-*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in files) {
                try { File.Delete(f); } catch { }
            }
        }

        static void CreateZipFromDirectory(string sourceDir, string destZip) {
            if (File.Exists(destZip)) File.Delete(destZip);
            ZipFile.CreateFromDirectory(sourceDir, destZip,
                CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        static void ExtractZip(string zip, string destDir) {
            // Use ExtractToDirectory with overwriteFiles=true (.NET 4.7.2+ / Unity).
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries) {
                var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(destDir), StringComparison.Ordinal)) {
                    throw new IOException($"Zip entry escapes target dir: {entry.FullName}");
                }
                if (string.IsNullOrEmpty(entry.Name)) {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        static void ClearOneJSPluginsContents(string pluginsDir) {
            // Wipe everything *inside* but keep the folder + its .meta.
            foreach (var sub in Directory.EnumerateDirectories(pluginsDir)) {
                Directory.Delete(sub, recursive: true);
                var meta = sub + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
            foreach (var f in Directory.EnumerateFiles(pluginsDir)) {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(f);
                var meta = f + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
        }
    }
}
