using System;
using System.Collections.Generic;
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
    ///   - On ExitingPlayMode: dispose all JsEnv instances.
    ///   - Reloads (live-reload, OnEnable): JSRunner re-runs
    ///     InitializeBridge → BridgeReady fires again → we dispose the
    ///     stale env first, then attach to the fresh context.
    /// </summary>
    [InitializeOnLoad]
    static class DebuggerAutoHook
    {
        const string PortPref = "OnejsDebugger.Port";

        static readonly Dictionary<JSRunner, JsEnv> s_Envs = new();

        static DebuggerAutoHook()
        {
            JSRunner.BridgeReady += OnBridgeReady;
            JSRunner.BeforeEval += OnBeforeEval;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnBeforeEval(JSRunner runner, string code, string filename)
        {
            if (runner == null) return;
            if (!s_Envs.TryGetValue(runner, out var env) || env == null) return;
            // Register the script with the CDP session so VSCode sees it in
            // Loaded Scripts and setBreakpointByUrl can resolve a script id.
            // Safe to call before VSCode attaches: scripts are stored and
            // replayed as scriptParsed events on Debugger.enable
            // (see cdp_handler.cpp: "Send scriptParsed for all known scripts").
            try
            {
                env.RegisterScript(filename, code);
                Debug.Log($"[OnejsDebugger][diag] RegisterScript: '{filename}' (len={code?.Length})");
            }
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
