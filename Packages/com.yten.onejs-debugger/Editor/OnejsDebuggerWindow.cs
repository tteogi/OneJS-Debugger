using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace OnejsDebugger.Editor {
    public sealed class OnejsDebuggerWindow : EditorWindow {
        const string PortPref     = "OnejsDebugger.Port";
        const string AutoWaitPref = "OnejsDebugger.AutoWaitOnPlay";

        [MenuItem("OneJS/Debugger/Status Window", priority = 20)]
        public static void Open() {
            var w = GetWindow<OnejsDebuggerWindow>("OneJS Debugger");
            w.minSize = new Vector2(380, 260);
            w.Show();
        }

        void OnGUI() {
            int port = EditorPrefs.GetInt(PortPref, 9229);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Plugin Mode", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.LabelField("Current", PluginSwap.CurrentMode);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Install Debugger"))  PluginSwap.InstallDebugger();
            if (GUILayout.Button("Rollback to OneJS")) PluginSwap.RollbackToOneJS();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Open Backup Folder")) PluginSwap.OpenBackupFolder();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            int newPort = EditorGUILayout.IntField("WebSocket Port", port);
            if (newPort != port) EditorPrefs.SetInt(PortPref, newPort);

            bool autoWait    = EditorPrefs.GetBool(AutoWaitPref, false);
            bool newAutoWait = EditorGUILayout.Toggle(
                new GUIContent("Wait For Debugger On Play",
                    "Block JS execution at startup until VSCode attaches."),
                autoWait);
            if (newAutoWait != autoWait) EditorPrefs.SetBool(AutoWaitPref, newAutoWait);

            EditorGUILayout.HelpBox(
                $"Read port via EditorPrefs key \"{PortPref}\", " +
                $"auto-wait via \"{AutoWaitPref}\".",
                MessageType.Info);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("VSCode Integration", EditorStyles.boldLabel);

            if (GUILayout.Button("Write .vscode/launch.json")) {
                WriteLaunchJson(newPort);
            }

            EditorGUILayout.SelectableLabel(
                BuildLaunchJson(newPort, qjsDebugPath: null),
                EditorStyles.textArea, GUILayout.MinHeight(180));
        }

        // Returns the absolute path to the bundled qjs_debug binary for the
        // current OS, or null if the package path cannot be resolved.
        static string ResolveQjsDebugPath() {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OnejsDebuggerWindow).Assembly);
            if (info == null) return null;
            string root = info.resolvedPath;
#if UNITY_EDITOR_WIN
            return Path.Combine(root, "qjs_debug~", "windows", "qjs_debug.exe").Replace('\\', '/');
#elif UNITY_EDITOR_OSX
            return Path.Combine(root, "qjs_debug~", "macos", "qjs_debug");
#else
            return Path.Combine(root, "qjs_debug~", "linux", "qjs_debug");
#endif
        }

        static string BuildLaunchJson(int port, string qjsDebugPath) {
            string qjsLine = string.IsNullOrEmpty(qjsDebugPath)
                ? "      \"runtimeExecutable\": \"<path-to-qjs_debug>\","
                : $"      \"runtimeExecutable\": \"{qjsDebugPath}\",";

            return
                "{\n" +
                "  \"version\": \"0.2.0\",\n" +
                "  \"configurations\": [\n" +
                "    {\n" +
                "      \"type\": \"node\",\n" +
                "      \"request\": \"attach\",\n" +
                "      \"name\": \"Attach to OneJS (Unity)\",\n" +
                $"      \"port\": {port},\n" +
                "      \"address\": \"127.0.0.1\",\n" +
                "      \"localRoot\": \"${workspaceFolder}\",\n" +
                "      \"sourceMaps\": true,\n" +
                "      \"restart\": true\n" +
                "    },\n" +
                "    {\n" +
                "      \"type\": \"node\",\n" +
                "      \"request\": \"launch\",\n" +
                "      \"name\": \"Launch with qjs_debug\",\n" +
                qjsLine + "\n" +
                "      \"program\": \"${workspaceFolder}/Assets/Scripts/index.js\",\n" +
                "      \"localRoot\": \"${workspaceFolder}\",\n" +
                "      \"sourceMaps\": true\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        static void WriteLaunchJson(int port) {
            string qjsPath = ResolveQjsDebugPath();
            bool binaryExists = qjsPath != null && File.Exists(qjsPath);
            if (!binaryExists) {
                bool proceed = EditorUtility.DisplayDialog(
                    "qjs_debug not found",
                    "The bundled qjs_debug binary was not found at the expected path.\n\n" +
                    "Run build.sh (or package-local.sh) to build it, then commit the result.\n\n" +
                    "Write launch.json with a placeholder path anyway?",
                    "Write anyway", "Cancel");
                if (!proceed) return;
                qjsPath = null;
            }

            string vscodeDir = Path.Combine(Application.dataPath, "..", ".vscode");
            Directory.CreateDirectory(vscodeDir);
            string dest = Path.Combine(vscodeDir, "launch.json");

            // Make qjs_debug executable if it exists
            if (binaryExists) {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                try { System.Diagnostics.Process.Start("chmod", $"+x \"{qjsPath}\""); }
                catch { }
#endif
            }

            File.WriteAllText(dest, BuildLaunchJson(port, qjsPath));
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(dest);
            Debug.Log($"[OnejsDebugger] Written: {dest}");
        }
    }
}
