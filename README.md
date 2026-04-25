# OneJS-Debugger

Source repo for `com.yten.onejs-debugger`, a UPM package that adds
VSCode debugging to [OneJS](https://github.com/Singtaa/OneJS) via an
embedded Chrome DevTools Protocol server. End-user docs live in
[`Packages/com.yten.onejs-debugger/README.md`](Packages/com.yten.onejs-debugger/README.md).

This README is for working *on* the project.

## Repo layout

```
.
├── OneJS/                                 # Submodule (read-only — don't modify)
├── quickjs/                               # Submodule (our quickjs-ng fork w/ debug patch)
├── QuickJS-Debugger/                      # Submodule (CDP server + qjs_debug binary)
├── Auxiliary~/onejs-debugger/             # Native plugin sources + per-platform build scripts
│   ├── src/
│   │   ├── quickjs_unity.c                # Copy of OneJS's wrapper, ABI-superset
│   │   └── onejs_debugger_glue.cpp        # Bridges quickjs_unity.c ↔ DebugSession
│   ├── CMakeLists.txt
│   └── build*.sh / build*.bat
├── Packages/com.yten.onejs-debugger/   # The UPM package (committed)
│   ├── Runtime/                           # JsEnv + DllImports (LibName "quickjs_unity")
│   ├── Editor/                            # PluginSwap + EditorWindow
│   ├── OnejsDebuggerPlugins~/             # COMMITTED: zip of the debugger plugin set
│   ├── DefaultPlugins~/                   # COMMITTED: zip of stock OneJS plugins (rollback fallback)
│   └── qjs_debug~/                        # COMMITTED: standalone debugger binaries (linux/macos/windows)
└── .github/workflows/release.yml          # Pack committed artifacts → UPM tarball release
```

## Local build → commit → release flow

CI does not build. Builds run on whatever host(s) you have access to, then
the binaries are committed into the package and a tag triggers a release
that just packages what's committed.

Submodules first (one time):

```bash
git submodule update --init --recursive
```

### 1. Build the native plugin per platform

```bash
cd Auxiliary~/onejs-debugger
./build.sh                       # macOS universal (arm64 + x86_64)
./build-ios.sh                   # iOS arm64 static (macOS host only)
./build-android.sh               # Android arm64-v8a / armeabi-v7a / x86_64 (any host w/ NDK)
./build-linux.sh                 # Linux x86_64 (Linux host)
./build-windows.sh               # Windows x64 via mingw cross (any unix host)
./build-windows-msvc.bat         # Windows x64 native MSVC (Windows host)
# or ./build-all.sh — runs every script the host supports
```

Outputs land in `Auxiliary~/onejs-debugger/Plugins~/<platform>/...`.
This directory is `.gitignore`d — do not commit it directly.

### 2. Roll the build into committed package artifacts

After each build run (and on each host), invoke the packaging helper:

```bash
cd Auxiliary~/onejs-debugger
./package-local.sh
```

This:
- zips `Plugins~/` into `Packages/com.yten.onejs-debugger/OnejsDebuggerPlugins~/onejs-debugger-plugins.zip`
- snapshots `OneJS/Plugins/` into `…/DefaultPlugins~/onejs-plugins.zip` (rollback fallback)
- builds `qjs_debug` standalone for the current host into `…/qjs_debug~/<host>/qjs_debug[.exe]`

Run on each host you support, then commit the resulting changes.

### 3. Release

```bash
git tag v0.1.0
git push --tags
```

`release.yml` verifies the committed zips exist, runs `npm pack`, and
attaches the resulting `com.yten.onejs-debugger-<ver>.tgz` (plus the
standalone `qjs_debug` binaries) to the GitHub Release.

Manual `workflow_dispatch` runs upload the tarball as a workflow
artifact instead of creating a release.

## Architecture notes

- **No OneJS modifications.** OneJS is a submodule and stays untouched.
  The native plugin we ship has the same library name (`quickjs_unity`)
  with an ABI superset, so existing OneJS C# code works against either
  build. Editor utility installs the debugger build by zip-swapping
  `OneJS/Plugins/`, with the previous state archived to
  `Library/OnejsDebugger/Backup/`.
- **Embedded debugger.** `qjs_start_debugger(ctx, port)` spins up a
  worker thread running QuickJS-Debugger's WebSocket + CDP code against
  a `DebugSession` registered for the given `JSContext*`. Multiple
  contexts can be debugged simultaneously — sessions are looked up via
  a `JSContext* → DebugSession*` map, not via `JS_SetContextOpaque`
  (which OneJS already owns).
- **Conditional breakpoints.** Evaluated in C++ before the `Debugger.paused`
  event is sent. Frame locals are exposed to the condition expression
  through a temporary `with(__bp_locals__){...}` wrapper around `JS_Eval`.

