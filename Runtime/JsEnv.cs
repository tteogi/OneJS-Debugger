using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OnejsDebugger
{
    /// <summary>
    /// Puerts-style debugger lifecycle wrapper around an existing OneJS QuickJSContext.
    /// Owns nothing about JS execution — it only starts / stops the embedded
    /// CDP WebSocket server and exposes attach-wait + breakpoint helpers.
    ///
    /// Typical use:
    /// <code>
    ///   var jsEnv = new JsEnv(jsRunner.Context, port: 9229);
    ///   await jsEnv.WaitDebuggerAsync();
    ///   jsRunner.Run(); // your normal OneJS entry point
    /// </code>
    /// </summary>
    public sealed class JsEnv : IDisposable
    {
        readonly IntPtr _ctx;
        bool _started;
        bool _disposed;

        public int Port { get; }
        public bool IsAttached => !_disposed && _started &&
                                  OnejsDebuggerNative.qjs_debugger_is_attached(_ctx) != 0;

        /// <param name="contextNativePtr">QuickJSContext.NativePtr (the JSContext*).</param>
        /// <param name="port">WebSocket port for VSCode/Chrome DevTools (default 9229).</param>
        /// <param name="autoStart">Start the server immediately (default true).</param>
        public JsEnv(IntPtr contextNativePtr, int port = 9229, bool autoStart = true)
        {
            if (contextNativePtr == IntPtr.Zero)
                throw new ArgumentException("contextNativePtr is null", nameof(contextNativePtr));
            _ctx = contextNativePtr;
            Port = port;
            if (autoStart) Start();
        }

        public void Start()
        {
            if (_started || _disposed) return;
            int rc = OnejsDebuggerNative.qjs_start_debugger(_ctx, Port);
            if (rc != 0)
            {
                throw new InvalidOperationException(
                    $"qjs_start_debugger failed (rc={rc}, port={Port}). " +
                    "Port may be in use, or debugger already started for this context.");
            }
            _started = true;
            Debug.Log($"[OnejsDebugger] Listening on ws://127.0.0.1:{Port}/onejs — attach VSCode now.");
        }

        public void Stop()
        {
            if (!_started || _disposed) return;
            OnejsDebuggerNative.qjs_stop_debugger(_ctx);
            _started = false;
        }

        /// <summary>
        /// Synchronously block the calling thread until VSCode attaches or
        /// <paramref name="timeoutMs"/> elapses (0 = forever). Returns true on attach.
        /// </summary>
        public bool WaitDebugger(int timeoutMs = 0)
        {
            EnsureStarted();
            return OnejsDebuggerNative.qjs_wait_debugger(_ctx, timeoutMs) == 1;
        }

        /// <summary>
        /// Async wait. Polls every 50ms; safe to await from the main thread without
        /// freezing Unity. Cancellation token aborts the wait but does not stop the server.
        /// </summary>
        public async Task<bool> WaitDebuggerAsync(int timeoutMs = 0,
                                                  CancellationToken cancellationToken = default)
        {
            EnsureStarted();
            using var timeoutCts = new CancellationTokenSource();
            if (timeoutMs > 0) timeoutCts.CancelAfter(timeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);
            while (!linked.IsCancellationRequested)
            {
                if (OnejsDebuggerNative.qjs_debugger_is_attached(_ctx) != 0) return true;
                try { await Task.Delay(50, linked.Token); } catch (TaskCanceledException) { break; }
            }
            return false;
        }

        /// <summary>
        /// Programmatic conditional breakpoint. <paramref name="condition"/> may be null/empty.
        /// </summary>
        public int SetBreakpoint(string file, int line, string condition = null)
        {
            EnsureStarted();
            return OnejsDebuggerNative.qjs_set_breakpoint(_ctx, file, line, condition ?? string.Empty);
        }

        /// <summary>
        /// Inform the debugger about a script's source so VSCode can show it and
        /// resolve breakpoint locations. Call after loading each .js/.ts source.
        /// </summary>
        public void RegisterScript(string url, string source)
        {
            EnsureStarted();
            OnejsDebuggerNative.qjs_register_script(_ctx, url, source ?? string.Empty);
        }

        void EnsureStarted()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JsEnv));
            if (!_started) throw new InvalidOperationException("JsEnv has not been started. Call Start().");
        }

        public void Dispose()
        {
            if (_disposed) return;
            try { Stop(); } catch { /* swallow on dispose */ }
            _disposed = true;
        }
    }
}
