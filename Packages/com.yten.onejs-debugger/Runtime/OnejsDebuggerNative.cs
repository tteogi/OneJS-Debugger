using System;
using System.Runtime.InteropServices;

namespace OnejsDebugger {
    /// <summary>
    /// P/Invoke surface for the OnejsDebugger-extended quickjs_unity native plugin.
    /// Mirrors OneJS's QuickJSNative library-name resolution rules so the same
    /// DLL/dylib/SO/static archive serves both feature sets.
    /// </summary>
    static class OnejsDebuggerNative {
        const string LibName =
#if UNITY_EDITOR
            "quickjs_unity";
#elif UNITY_WEBGL
            "__Internal";
#elif UNITY_IOS
            "__Internal";
#else
            "quickjs_unity";
#endif

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int qjs_start_debugger(IntPtr ctx, int port);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void qjs_stop_debugger(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int qjs_wait_debugger(IntPtr ctx, int timeout_ms);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int qjs_set_breakpoint(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string file, int line,
                                                    [MarshalAs(UnmanagedType.LPStr)] string condition);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int qjs_debugger_is_attached(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int qjs_register_script(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string url,
                                                     [MarshalAs(UnmanagedType.LPStr)] string source);
    }
}
