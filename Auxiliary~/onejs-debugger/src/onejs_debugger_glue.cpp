// onejs_debugger_glue.cpp
//
// C++ glue that exposes a C ABI on top of QuickJS-Debugger's DebugSession,
// callable from quickjs_unity.c. Adds debugger lifecycle exports
// (qjs_start_debugger / qjs_stop_debugger / qjs_wait_debugger /
// qjs_set_breakpoint) without touching the existing OneJS exports.

#include "websocket_server.h"
#include "cdp_handler.h"
#include "debug_session.h"

#include <atomic>
#include <chrono>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>

extern "C" {
#include "quickjs.h"
}

namespace {

struct DebuggerInstance {
    JSContext* ctx = nullptr;
    DebugSession session;
    WebSocketServer ws;
    std::unique_ptr<CDPHandler> cdp;
    std::thread worker;
    std::atomic<bool> stop{false};
    int port = 0;
    std::string target_name = "OneJS";
};

std::mutex g_inst_mutex;
std::unordered_map<JSContext*, std::unique_ptr<DebuggerInstance>> g_instances;

DebuggerInstance* find_instance(JSContext* ctx) {
    std::lock_guard<std::mutex> lock(g_inst_mutex);
    auto it = g_instances.find(ctx);
    return it == g_instances.end() ? nullptr : it->second.get();
}

void run_worker(DebuggerInstance* inst) {
    // The WebSocketServer API is blocking. Loop:
    //   1) wait for a client to connect (HTTP discovery + WS handshake)
    //   2) read frames until disconnect
    //   3) repeat unless `stop` was set
    while (!inst->stop.load()) {
        if (!inst->ws.is_connected()) {
            std::string url = "ws://127.0.0.1:" + std::to_string(inst->port) + "/onejs";
            if (!inst->ws.wait_for_connection(inst->target_name, url)) {
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
                continue;
            }
        }
        std::string msg = inst->ws.receive();
        if (msg.empty()) {
            // disconnect
            inst->session.on_disconnect();
            continue;
        }
        if (inst->cdp) inst->cdp->handle_message(msg);
    }
}

} // namespace

#ifdef _WIN32
#define QJS_API __declspec(dllexport)
#else
#define QJS_API __attribute__((visibility("default")))
#endif

extern "C" {

// Returns 0 on success, negative on error.
QJS_API int qjs_start_debugger(JSContext* ctx, int port) {
    if (!ctx || port <= 0 || port > 65535) return -1;
    {
        std::lock_guard<std::mutex> lock(g_inst_mutex);
        if (g_instances.count(ctx)) return -2;
    }

    auto inst = std::make_unique<DebuggerInstance>();
    inst->ctx = ctx;
    inst->port = port;

    if (!inst->ws.start(port)) {
        return -3;
    }

    inst->cdp = std::make_unique<CDPHandler>(inst->ws, inst->session);
    auto* cdp_ptr = inst->cdp.get();
    inst->session.set_send_event([cdp_ptr](const std::string& method,
                                            const json::Value& params) {
        cdp_ptr->send_event(method, params);
    });

    DebugSession::register_for_context(ctx, &inst->session);
    JS_SetDebugTraceHandler(ctx, &DebugSession::debug_trace_handler);
    inst->session.set_enabled(true);

    DebuggerInstance* raw = inst.get();
    inst->worker = std::thread(run_worker, raw);

    {
        std::lock_guard<std::mutex> lock(g_inst_mutex);
        g_instances[ctx] = std::move(inst);
    }
    return 0;
}

QJS_API void qjs_stop_debugger(JSContext* ctx) {
    if (!ctx) return;
    std::unique_ptr<DebuggerInstance> inst;
    {
        std::lock_guard<std::mutex> lock(g_inst_mutex);
        auto it = g_instances.find(ctx);
        if (it == g_instances.end()) return;
        inst = std::move(it->second);
        g_instances.erase(it);
    }

    inst->stop.store(true);
    inst->ws.disconnect();
    inst->ws.stop();
    if (inst->worker.joinable()) inst->worker.join();
    inst->session.set_enabled(false);
    JS_SetDebugTraceHandler(ctx, nullptr);
    DebugSession::unregister_context(ctx);
}

// Block until a debugger client connects or `timeout_ms` elapses (0 = forever).
// Returns 1 if attached, 0 if timed out, -1 if no debugger is running on ctx.
QJS_API int qjs_wait_debugger(JSContext* ctx, int timeout_ms) {
    auto* inst = find_instance(ctx);
    if (!inst) return -1;

    using clock = std::chrono::steady_clock;
    auto deadline = clock::now() + std::chrono::milliseconds(timeout_ms);
    while (!inst->ws.is_connected()) {
        if (timeout_ms > 0 && clock::now() >= deadline) return 0;
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    return 1;
}

// Programmatic breakpoint registration. `condition` may be NULL or empty.
// Returns the breakpoint id (>=1), or -1 on error.
QJS_API int qjs_set_breakpoint(JSContext* ctx, const char* file, int line,
                                const char* condition) {
    auto* inst = find_instance(ctx);
    if (!inst || !file || line < 1) return -1;

    json::Value res = inst->session.set_breakpoint_by_url(
        file, line - 1, 0, false, condition ? std::string(condition) : "");
    if (!res.is_object() || !res.has("breakpointId")) return -1;
    std::string bp_id = res["breakpointId"].get_string();
    auto colon = bp_id.find(':');
    try {
        return std::stoi(colon == std::string::npos ? bp_id : bp_id.substr(0, colon));
    } catch (...) {
        return -1;
    }
}

QJS_API int qjs_debugger_is_attached(JSContext* ctx) {
    auto* inst = find_instance(ctx);
    return inst && inst->ws.is_connected() ? 1 : 0;
}

// Add a script to the debug session so VSCode can map breakpoints / show source.
// `url` is the source file path (e.g. "Assets/Scripts/foo.ts" or absolute).
// Returns 0 on success, -1 on error.
QJS_API int qjs_register_script(JSContext* ctx, const char* url, const char* source) {
    auto* inst = find_instance(ctx);
    if (!inst || !url || !source) return -1;
    inst->session.add_script(url, source);
    return 0;
}

} // extern "C"
