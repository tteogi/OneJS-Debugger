# OneJS Debugger

VSCode debugging for [OneJS](https://github.com/Singtaa/OneJS) — set
breakpoints (incl. conditional ones), step through code, inspect locals,
and stream `console.log` output, all directly against your TypeScript
source. Works in the editor, in standalone builds, and over the network
to a mobile device.

The debugger server runs **inside** the Unity process: when you start your
game, port 9229 is open and waiting for VSCode to attach — no extra tools,
no separate process to launch, no platform-specific glue.

## Install

**Option A — git URL (recommended)**

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.singtaa.onejs": "3.0.3",
    "com.yten.onejs-debugger": "https://github.com/tteogi/OneJS-Debugger.git#upm"
  }
}
```

Pin a specific release by replacing `#upm` with `#upm/v0.1.0`.

**Option B — tarball**

Grab the prebuilt `.tgz` from the
[Releases page](https://github.com/tteogi/OneJS-Debugger/releases) and
install via **Window → Package Manager → + → Add package from tarball…**

## Switch on the debugger

OneJS ships its own native plugins under the `com.singtaa.onejs/Plugins/`
folder. The debugger build replaces those with an ABI-superset version
(same exports + new debugger ones). The package never modifies OneJS
directly — instead, an editor utility swaps zip archives in and out:

1. Open **OneJS → Debugger → Status Window** (or use the menu items directly).
2. Click **Install Debugger Plugin**. Your existing OneJS plugins are
   archived to `Library/OnejsDebugger/Backup/onejs-plugins-<timestamp>.zip`,
   and the debugger build is unpacked over `com.singtaa.onejs/Plugins/`.
3. To go back: **Rollback to OneJS** restores the most recent backup
   (or the bundled snapshot if no user backup exists).

Backups roll automatically — the five most recent of each kind are kept.

## Use it from C\#

```csharp
using OnejsDebugger;

// Existing OneJS setup -----------------
var ctx = new QuickJSContext();
// or: var bridge = new QuickJSUIBridge(root, workingDir);

// Add the debugger ---------------------
var debugger = new JsEnv(ctx.NativePtr, port: 9229);

// Block startup until VSCode attaches (optional)
await debugger.WaitDebuggerAsync(timeoutMs: 30_000);

// Run your scripts as usual; breakpoints will hit immediately.
ctx.Eval(File.ReadAllText("Assets/Scripts/main.js"), "main.js");
```

`JsEnv` does not own the OneJS context — it just opens / closes the
WebSocket server on it. Dispose it when you tear down OneJS.

### Conditional breakpoints

Set them like any normal VSCode breakpoint — right-click the gutter,
**Add Conditional Breakpoint**, and type an expression. The expression
runs in the breakpoint's frame (locals visible via a `with()` shim) and
the debugger only pauses if it's truthy. Throwing or syntactically
invalid conditions are silently treated as falsy.

Programmatic version:

```csharp
debugger.SetBreakpoint("Assets/Scripts/main.ts", line: 42, condition: "i === 50");
```

## Configure VSCode

The easiest way is to click **OneJS → Debugger → Status Window → Write .vscode/launch.json**.
The editor resolves the bundled `qjs_debug` binary path automatically and
writes a ready-to-use config.

Or drop this manually into `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "node",
      "request": "attach",
      "name": "Attach to OneJS (Unity)",
      "port": 9229,
      "address": "127.0.0.1",
      "localRoot": "${workspaceFolder}",
      "sourceMaps": true,
      "restart": true
    },
    {
      "type": "node",
      "request": "launch",
      "name": "Launch with qjs_debug",
      "runtimeExecutable": "<package-path>/qjs_debug~/macos/qjs_debug",
      "program": "${workspaceFolder}/Assets/Scripts/index.js",
      "localRoot": "${workspaceFolder}",
      "sourceMaps": true
    }
  ]
}
```

**Attach to OneJS (Unity)** — attaches to the embedded CDP server while Unity
is in Play mode. `"restart": true` makes VSCode auto-reattach each time you
stop and re-enter Play without clicking Attach again.

**Launch with qjs_debug** — runs a `.js` file outside of Unity using the
bundled `qjs_debug` standalone binary. Useful for unit-testing pure TypeScript
logic. Replace `runtimeExecutable` with the path for your OS
(`qjs_debug~/windows/qjs_debug.exe`, `qjs_debug~/linux/qjs_debug`), or use
the Status Window button to have the path written automatically.

For mobile builds, set `"address"` to the device's IP in the Attach
configuration and make sure your `JsEnv` is initialized with the correct port.

## Supported platforms

| Platform | Status |
|---|---|
| Windows x64 | ✅ |
| macOS arm64 / x64 | ✅ |
| Linux x86_64 | ✅ |
| Android arm64-v8a, armeabi-v7a, x86_64 | ✅ |
| iOS arm64 | ✅ (static link, debugger code is dead unless you call `qjs_start_debugger`) |
| WebGL | ❌ (1.0 limitation) |

## Standalone debugger CLI

The package ships `qjs_debug~/{linux,macos,windows}/qjs_debug` (Hidden
folder, not consumed by Unity). Use it to debug a `.js` file outside
Unity — VSCode attaches the same way.

```bash
./qjs_debug~/macos/qjs_debug --port 9229 path/to/script.js
```

## Troubleshooting

- **"Port already in use"** — Another Unity instance is holding 9229.
  Either close it or change the port in your `JsEnv` constructor.
- **VSCode says "Could not connect"** — Make sure your build was made
  with the debugger plugin installed (`Status Window → Current` should
  read **Debugger**, not **Default**).
- **Breakpoints don't hit** — The debugger only instruments code parsed
  *after* the WebSocket is up. Defer your `Eval` until
  `WaitDebuggerAsync` returns true, or accept that the very first lines
  may be skipped on cold start.

## License

MIT. See [LICENSE](../../LICENSE).
