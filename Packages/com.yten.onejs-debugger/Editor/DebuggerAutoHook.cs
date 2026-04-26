using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace OnejsDebugger.Editor
{
    /// <summary>
    /// Starts the QuickJS debugger server the moment a JSRunner's
    /// QuickJSUIBridge is constructed — *before* any user script is
    /// evaluated — by subscribing to JSRunner.BridgeReady.
    ///
    /// Why this matters:
    ///   QuickJS only sends Debugger.scriptParsed events for code
    ///   compiled while the debugger is enabled. If we attach after
    ///   InitializeBridge() has already eval'd globals + the entry
    ///   script, VSCode sees zero loaded scripts and every breakpoint
    ///   stays unverified (gray dot). The BridgeReady hook fires after
    ///   the JSContext exists but before any Eval, which is the only
    ///   point that lets breakpoints bind on the first run.
    ///
    /// Lifecycle:
    ///   - On BridgeReady: if PluginSwap.CurrentMode == "Debugger",
    ///     create a JsEnv on the runner's NativePtr.
    ///   - On BridgeDisposing: stop the JsEnv on the OLD ctx BEFORE the
    ///     bridge frees it. Stopping after dispose is a use-after-free
    ///     because qjs_stop_debugger calls JS_SetDebugTraceHandler on
    ///     the freed JSContext.
    ///   - On ExitingPlayMode: dispose all JsEnv instances.
    ///   - Reloads (live-reload, OnEnable): JSRunner fires
    ///     BridgeDisposing (old ctx still valid → stop) → disposes bridge →
    ///     creates new bridge → BridgeReady (fresh ctx → new JsEnv on the
    ///     same port). VSCode auto-reattaches via launch.json `restart: true`.
    /// </summary>
    [InitializeOnLoad]
    static class DebuggerAutoHook
    {
        const string PortPref = "OnejsDebugger.Port";

        static readonly Dictionary<JSRunner, JsEnv> s_Envs = new();

        // esbuild emits `//# sourceMappingURL=app.js.txt.map`, but OneJS's
        // build pipeline renames the .map to `app.js.map.txt` so Unity can
        // import it as a TextAsset. That breaks VSCode's "find the .map next
        // to the bundle" heuristic. We rewrite the comment in-flight on the
        // copy we register with the CDP session — the JS engine still
        // receives the unpatched original via _bridge.Eval, so behavior is
        // identical for runtime.
        static readonly Regex s_SourceMappingUrlFix = new Regex(
            @"sourceMappingURL=([^\s]+?)\.txt\.map(\b|$)",
            RegexOptions.Compiled);

        static DebuggerAutoHook()
        {
            JSRunner.BridgeReady += OnBridgeReady;
            JSRunner.BridgeDisposing += OnBridgeDisposing;
            JSRunner.BeforeEval += OnBeforeEval;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnBridgeDisposing(JSRunner runner)
        {
            if (runner == null) return;
            if (!s_Envs.TryGetValue(runner, out var env)) return;
            // Tear down the debugger on the old ctx WHILE it's still valid.
            // After this returns, JSRunner will free the bridge and the ctx
            // pointer becomes dangling — touching it (e.g. via a delayed
            // qjs_stop_debugger) is a use-after-free.
            try { env?.Dispose(); } catch { }
            s_Envs.Remove(runner);
        }

        static void OnBeforeEval(JSRunner runner, string code, string filename)
        {
            if (runner == null) return;
            if (!s_Envs.TryGetValue(runner, out var env) || env == null) return;

            // Patch the OneJS-renamed sourceMappingURL on the registered copy.
            // See s_SourceMappingUrlFix comment for rationale.
            var registerCode = code;
            if (registerCode != null && registerCode.IndexOf(".txt.map", StringComparison.Ordinal) >= 0)
                registerCode = s_SourceMappingUrlFix.Replace(registerCode, "sourceMappingURL=$1.map.txt$2");

            // Register the script with the CDP session so VSCode sees it in
            // Loaded Scripts and setBreakpointByUrl can resolve a script id.
            // Safe to call before VSCode attaches: scripts are stored and
            // replayed as scriptParsed events on Debugger.enable
            // (see cdp_handler.cpp: "Send scriptParsed for all known scripts").
            try { env.RegisterScript(filename, registerCode); }
            catch (Exception e) { Debug.LogWarning($"[OnejsDebugger] RegisterScript('{filename}') failed: {e.Message}"); }
        }

        static void OnBridgeReady(JSRunner runner)
        {
            if (!EditorApplication.isPlaying) return;
            if (runner == null) return;
            if (PluginSwap.CurrentMode != "Debugger") return;

            // Reload paths fire BridgeReady again on the same runner with a
            // fresh JSContext. Drop the old env so we attach to the new ctx.
            if (s_Envs.TryGetValue(runner, out var existing))
            {
                try { existing?.Dispose(); } catch { }
                s_Envs.Remove(runner);
            }

            var bridge = runner.Bridge;
            if (bridge == null || bridge.Context == null || bridge.Context.NativePtr == IntPtr.Zero)
            {
                Debug.LogWarning("[OnejsDebugger] BridgeReady fired but NativePtr is not set; skipping.");
                return;
            }

            int port = EditorPrefs.GetInt(PortPref, 9229);
            try
            {
                s_Envs[runner] = new JsEnv(bridge.Context.NativePtr, port);
                // JsEnv.Start() already logs the listening message.
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[OnejsDebugger] Could not start debugger server (port {port}): {e.Message}\n" +
                    "→ Use OneJS > Debugger > Install Debugger Plugin, then restart Unity.");
            }
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                StopAll();
        }

        static void StopAll()
        {
            foreach (var env in s_Envs.Values)
            {
                try { env?.Dispose(); } catch { }
            }
            s_Envs.Clear();
        }
    }
}
