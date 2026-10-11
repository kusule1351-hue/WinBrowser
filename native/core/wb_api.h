// Flat C API exported by WinBrowserCore.dll and consumed by the WinUI shell via P/Invoke.
//
// Threading: every wb_* function may be called from the WinUI thread. Callbacks are
// invoked on CEF's UI thread and must not block; the shell marshals them to its own
// dispatcher. Strings are UTF-16 and only valid for the duration of the callback.
#pragma once

#include <windows.h>

#ifdef WB_CORE_EXPORTS
#define WB_API extern "C" __declspec(dllexport)
#else
#define WB_API extern "C" __declspec(dllimport)
#endif

// Modifier flags passed to on_shortcut.
#define WB_MOD_SHIFT 1
#define WB_MOD_CTRL 2
#define WB_MOD_ALT 4

typedef struct WbCallbacks {
  void(__stdcall* on_created)(int id);
  void(__stdcall* on_title)(int id, const wchar_t* title);
  void(__stdcall* on_address)(int id, const wchar_t* url);
  void(__stdcall* on_loading)(int id, int is_loading, int can_back, int can_forward);
  void(__stdcall* on_favicon)(int id, const wchar_t* url);
  void(__stdcall* on_new_tab)(int id, const wchar_t* url, int foreground);
  void(__stdcall* on_closed)(int id);
  void(__stdcall* on_fullscreen)(int id, int fullscreen);
  // Return non-zero if the shell consumed the key (keeps it from the page).
  int(__stdcall* on_shortcut)(int id, int virtual_key, int modifiers);
  void(__stdcall* on_got_focus)(int id);
} WbCallbacks;

// Starts CEF with the multi-threaded message loop. Returns 1 on success.
WB_API int __stdcall wb_initialize(const WbCallbacks* callbacks,
                                   const wchar_t* subprocess_path,
                                   const wchar_t* cache_dir,
                                   unsigned int background_argb);
// Must be called on the thread that called wb_initialize, after all browsers closed.
WB_API void __stdcall wb_shutdown();

// Creates a browser as a child of `parent`. Returns the tab id (> 0) immediately;
// on_created fires once the native window exists.
WB_API int __stdcall wb_create(HWND parent, const wchar_t* url,
                               int x, int y, int width, int height, int visible);
WB_API void __stdcall wb_close(int id);
WB_API void __stdcall wb_close_all();
WB_API int __stdcall wb_count();

WB_API void __stdcall wb_set_bounds(int id, int x, int y, int width, int height, int corner_radius);
WB_API void __stdcall wb_set_visible(int id, int visible);
WB_API void __stdcall wb_focus(int id);

WB_API void __stdcall wb_navigate(int id, const wchar_t* url);
WB_API void __stdcall wb_go_back(int id);
WB_API void __stdcall wb_go_forward(int id);
WB_API void __stdcall wb_reload(int id, int ignore_cache);
WB_API void __stdcall wb_stop(int id);
WB_API void __stdcall wb_zoom(int id, int direction);  // -1 out, 0 reset, +1 in
WB_API void __stdcall wb_show_devtools(int id);
WB_API void __stdcall wb_find(int id, const wchar_t* text, int forward);
WB_API void __stdcall wb_stop_find(int id);
WB_API void __stdcall wb_print(int id);
WB_API void __stdcall wb_exit_fullscreen(int id);
