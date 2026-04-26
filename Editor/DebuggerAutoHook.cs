using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OnejsDebugger.Editor
{
    /// <summary>
    /// Automatically starts the QuickJS debugger server when Unity enters Play mode,
    /// provided the debugger plugin is installed (PluginSwap mode = "Debugger").
    ///
    /// How it works:
    ///   1. [InitializeOnLoad] subscribes to playModeStateChanged.
    ///   2. On EnteredPlayMode, starts polling EditorApplication.update each frame.
    ///   3. Each frame: finds JSRunner instances whose Bridge.Context.NativePtr is ready,
    ///      creates a JsEnv (calls qjs_start_debugger), then stops tracking that runner.
    ///   4. Polling stops automatically after all active runners are handled, or after
    ///      ~300 frames without finding any new runner (≈5 s at 60 Hz).
    ///   5. On ExitingPlayMode: all JsEnv instances are disposed.
    /// </summary>
    [InitializeOnLoad]
    static class DebuggerAutoHook
    {
        const string PortPref = "OnejsDebugger.Port";

        // JSRunner → JsEnv (null sentinel = tried but failed for this runner)
        static readonly Dictionary<JSRunner, JsEnv> s_Envs = new();
        static int s_PollFrames;
        const int MaxPollFrames = 300; // give up after ~5 s if no runner appears

        static DebuggerAutoHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    if (PluginSwap.CurrentMode == "Debugger")
                    {
                        s_PollFrames = 0;
                        EditorApplication.update += PollForBridges;
                    }
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    EditorApplication.update -= PollForBridges;
                    StopAll();
                    break;
            }
        }

        static void PollForBridges()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= PollForBridges;
                return;
            }

            s_PollFrames++;
            int port = EditorPrefs.GetInt(PortPref, 9229);
            bool anyUnhandled = false;

            foreach (var runner in JSRunner.Instances)
            {
                if (runner == null) continue;
                if (s_Envs.ContainsKey(runner)) continue; // already handled (or failed)

                var bridge = runner.Bridge;
                if (bridge == null || bridge.Context == null || bridge.Context.NativePtr == IntPtr.Zero)
                {
                    anyUnhandled = true; // bridge not ready yet, keep polling
                    continue;
                }

                TryStartDebugger(runner, bridge.Context.NativePtr, port);
            }

            // Stop when every known runner is handled and the timeout hasn't expired yet.
            // Keep polling past the timeout only if there are runners still initializing.
            bool timedOut = s_PollFrames >= MaxPollFrames;
            if (!anyUnhandled && (s_Envs.Count > 0 || timedOut))
                EditorApplication.update -= PollForBridges;
        }

        static void TryStartDebugger(JSRunner runner, IntPtr nativePtr, int port)
        {
            try
            {
                var env = new JsEnv(nativePtr, port);
                s_Envs[runner] = env;
                // JsEnv.Start() already logs the listening message.
            }
            catch (Exception e)
            {
                // Null sentinel so we don't retry this runner.
                s_Envs[runner] = null;
                Debug.LogWarning(
                    $"[OnejsDebugger] Could not start debugger server (port {port}): {e.Message}\n" +
                    "The native library may not have been reloaded yet.\n" +
                    "→ Use OneJS > Debugger > Install Debugger Plugin, then restart Unity.");
            }
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
