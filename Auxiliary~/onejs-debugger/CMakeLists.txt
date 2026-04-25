cmake_minimum_required(VERSION 3.16)
project(quickjs_unity_debugger LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Repo layout:
#   <repo>/quickjs/            (our quickjs-ng fork, with JS_SetDebugTraceHandler)
#   <repo>/QuickJS-Debugger/   (CDP server static lib qjs_debug_lib)
#   <repo>/Auxiliary~/onejs-debugger/  (this directory)
#
# We piggy-back on QuickJS-Debugger's existing top-level CMakeLists, which
# wires up both the `qjs` target (from quickjs/) and `qjs_debug_lib`. By
# adding it as a subdirectory we get both for free, then build a single
# shared library (`quickjs_unity`, ABI-superset of OneJS's) that ships
# OneJS's wrapper exports plus the debugger glue exports.

get_filename_component(REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)

# QuickJS-Debugger's CMakeLists expects to be added from its own root, which
# uses ${CMAKE_SOURCE_DIR} (the original project source dir) to locate the
# `quickjs/` subdir. Setting CMAKE_SOURCE_DIR via add_subdirectory binding is
# not possible, but QuickJS-Debugger's quickjs/ is at the same relative path,
# so we can include it directly: it uses ${CMAKE_SOURCE_DIR} which when this
# project is the root will be REPO_ROOT/Auxiliary~/onejs-debugger. We work
# around that by adding both subdirs explicitly with their own binary dirs.

# Configure quickjs-ng (debugger-patched fork).
set(BUILD_SHARED_LIBS_SAVED ${BUILD_SHARED_LIBS})
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BUILD_QJS_LIBC ON CACHE BOOL "" FORCE)
add_subdirectory(${REPO_ROOT}/quickjs ${CMAKE_BINARY_DIR}/_quickjs)
set(BUILD_SHARED_LIBS ${BUILD_SHARED_LIBS_SAVED} CACHE BOOL "" FORCE)

# Build the debugger static library directly here so it sees `qjs` from above.
# We re-declare its target rather than add_subdirectory'ing the QuickJS-Debugger
# tree (whose CMakeLists.txt assumes its own source root).
add_library(qjs_debug_lib STATIC
    ${REPO_ROOT}/QuickJS-Debugger/debugger/src/websocket_server.cpp
    ${REPO_ROOT}/QuickJS-Debugger/debugger/src/cdp_handler.cpp
    ${REPO_ROOT}/QuickJS-Debugger/debugger/src/debug_session.cpp
)
set_target_properties(qjs_debug_lib PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_include_directories(qjs_debug_lib PUBLIC
    ${REPO_ROOT}/QuickJS-Debugger/debugger/src
    ${REPO_ROOT}/quickjs
)
target_link_libraries(qjs_debug_lib PUBLIC qjs)

if(WIN32)
    target_link_libraries(qjs_debug_lib PUBLIC ws2_32)
    target_compile_definitions(qjs_debug_lib PUBLIC
        _CRT_SECURE_NO_WARNINGS
        WIN32_LEAN_AND_MEAN
        _WIN32_WINNT=0x0601
    )
endif()
if(UNIX)
    find_package(Threads REQUIRED)
    target_link_libraries(qjs_debug_lib PUBLIC Threads::Threads)
endif()

# --- The native plugin Unity loads (same name as OneJS's, ABI superset) ---
# iOS / macOS-Catalyst-style platforms link via __Internal: build STATIC.
# Other platforms get a SHARED library Unity loads at runtime.
if(CMAKE_SYSTEM_NAME STREQUAL "iOS" OR ONEJS_DEBUGGER_STATIC)
    add_library(quickjs_unity STATIC
        src/quickjs_unity.c
        src/onejs_debugger_glue.cpp
    )
else()
    add_library(quickjs_unity SHARED
        src/quickjs_unity.c
        src/onejs_debugger_glue.cpp
    )
endif()
target_include_directories(quickjs_unity PRIVATE
    ${REPO_ROOT}/quickjs
    ${REPO_ROOT}/QuickJS-Debugger/debugger/src
)
target_link_libraries(quickjs_unity PRIVATE qjs qjs_debug_lib)

if(WIN32)
    target_compile_definitions(quickjs_unity PRIVATE
        _CRT_SECURE_NO_WARNINGS
        WIN32_LEAN_AND_MEAN
    )
endif()

# Strip soname/install_name shenanigans that confuse Unity on macOS. Use the
# same naming OneJS expects: libquickjs_unity.dylib / .so / quickjs_unity.dll.
set_target_properties(quickjs_unity PROPERTIES
    OUTPUT_NAME quickjs_unity
    PREFIX "lib"          # emits libquickjs_unity.* on Unix
)
if(WIN32)
    set_target_properties(quickjs_unity PROPERTIES PREFIX "")
endif()

install(TARGETS quickjs_unity
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
)
