using UnityEditor;
using UnityEngine;

namespace OnejsDebugger.Editor {
    public sealed class OnejsDebuggerWindow : EditorWindow {
        const string PortPref = "OnejsDebugger.Port";
        const string AutoWaitPref = "OnejsDebugger.AutoWaitOnPlay";

        [MenuItem("OneJS/Debugger/Status Window", priority = 20)]
        public static void Open() {
            var w = GetWindow<OnejsDebuggerWindow>("OneJS Debugger");
            w.minSize = new Vector2(360, 200);
            w.Show();
        }

        void OnGUI() {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Plugin Mode", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.LabelField("Current", PluginSwap.CurrentMode);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Install Debugger")) PluginSwap.InstallDebugger();
            if (GUILayout.Button("Rollback to OneJS")) PluginSwap.RollbackToOneJS();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Open Backup Folder")) PluginSwap.OpenBackupFolder();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            int port = EditorPrefs.GetInt(PortPref, 9229);
            int newPort = EditorGUILayout.IntField("WebSocket Port", port);
            if (newPort != port) EditorPrefs.SetInt(PortPref, newPort);

            bool autoWait = EditorPrefs.GetBool(AutoWaitPref, false);
            bool newAutoWait = EditorGUILayout.Toggle(
                new GUIContent("Wait For Debugger On Play",
                    "Block JS execution at startup until VSCode attaches."),
                autoWait);
            if (newAutoWait != autoWait) EditorPrefs.SetBool(AutoWaitPref, newAutoWait);

            EditorGUILayout.HelpBox(
                "Use these EditorPrefs from your JsEnv setup if you want a UI-driven port " +
                "instead of a hard-coded one. Both keys live under " +
                $"\"{PortPref}\" / \"{AutoWaitPref}\".",
                MessageType.Info);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("VSCode launch.json", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                "{\n" +
                "  \"version\": \"0.2.0\",\n" +
                "  \"configurations\": [\n" +
                "    {\n" +
                "      \"type\": \"node\",\n" +
                "      \"request\": \"attach\",\n" +
                "      \"name\": \"Attach to OneJS\",\n" +
                $"      \"port\": {newPort},\n" +
                "      \"address\": \"127.0.0.1\",\n" +
                "      \"localRoot\": \"${workspaceFolder}\",\n" +
                "      \"sourceMaps\": true,\n" +
                "      \"restart\": true\n" +
                "    }\n" +
                "  ]\n" +
                "}",
                EditorStyles.textArea, GUILayout.MinHeight(160));
        }
    }
}
