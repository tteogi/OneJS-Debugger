# onejs-debugger native plugin

Drop-in replacement for OneJS's `quickjs_unity` native plugin that adds an
embedded Chrome DevTools Protocol (CDP) server. The exported ABI is a
**superset** of OneJS's: existing OneJS C# code (`QuickJSContext`,
`QuickJSUIBridge`, …) keeps working unchanged; the additional debugger
exports are consumed by the `com.yten.onejs-debugger` UPM package.

## Layout

```
Auxiliary~/onejs-debugger/
├── src/
│   ├── quickjs_unity.c             # OneJS wrapper (copied + patched for new quickjs API)
│   └── onejs_debugger_glue.cpp     # qjs_start/stop/wait_debugger, qjs_set_breakpoint, qjs_register_script
├── CMakeLists.txt                  # Builds quickjs (from <repo>/quickjs) + qjs_debug_lib + plugin
├── build.sh                        # macOS universal (arm64 + x86_64) → libquickjs_unity.dylib
├── build-linux.sh                  # Linux x86_64 → libquickjs_unity.so
├── build-android.sh                # Android arm64-v8a / armeabi-v7a / x86_64 → libquickjs_unity.so
├── build-ios.sh                    # iOS arm64 device → libquickjs_unity.a (static)
├── build-windows.sh                # Windows x64 (MinGW cross-compile) → quickjs_unity.dll
├── build-windows-msvc.bat          # Windows x64 (native MSVC) → quickjs_unity.dll
├── build-all.sh                    # Convenience: runs every build the host supports
└── Plugins~/                       # Build outputs land here (Hidden from Unity import)
    ├── Android/{arm64-v8a,armeabi-v7a,x86_64}/libquickjs_unity.so
    ├── iOS/libquickjs_unity.a
    ├── Linux/x86_64/libquickjs_unity.so
    ├── macOS/libquickjs_unity.dylib
    └── Windows/x86_64/quickjs_unity.dll
```

## Added exports (vs. OneJS's quickjs_unity)

| Symbol | Purpose |
|---|---|
| `qjs_start_debugger(ctx, port)` | Start the CDP WebSocket server on `ws://127.0.0.1:<port>/onejs`. |
| `qjs_stop_debugger(ctx)` | Stop server, unregister handler, free worker thread. |
| `qjs_wait_debugger(ctx, timeout_ms)` | Block until VSCode attaches (1) or timeout (0). |
| `qjs_set_breakpoint(ctx, file, line, condition)` | Programmatic conditional breakpoint. |
| `qjs_debugger_is_attached(ctx)` | 1 if a client is currently connected. |
| `qjs_register_script(ctx, url, source)` | Register a script so VSCode shows source / resolves bp locations. |

## Build prerequisites

- **macOS**: Xcode CLT, CMake 3.16+
- **Linux**: gcc/g++, CMake 3.16+
- **Windows (MSVC)**: VS 2019+ "x64 Native Tools Command Prompt", CMake
- **Windows (cross)**: mingw-w64
- **Android**: Android NDK (set `NDK_ROOT` / `ANDROID_NDK_HOME`)
- **iOS**: Xcode, CMake's iOS toolchain (built-in via `-G Xcode`)

## How the swap works

The output of these scripts is consumed by the
`com.yten.onejs-debugger` UPM package's `OnejsDebuggerPlugins~/onejs-debugger-plugins.zip`,
which the editor utility extracts over `OneJS/Plugins/` (after archiving the
existing OneJS plugins to `Library/OnejsDebugger/Backup/`). See the package
README for installation/rollback flow.
