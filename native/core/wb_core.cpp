// Tab registry and the exported wb_* API.
#include <atomic>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include "include/cef_app.h"
#include "include/cef_browser.h"
#include "wb_api.h"
#include "wb_client.h"

namespace wb {
namespace {

struct Tab {
  CefRefPtr<CefBrowser> browser;
  HWND hwnd = nullptr;
  RECT bounds = {};
  int radius = 0;
  bool visible = true;
  bool closing = false;
  std::wstring pending_url;  // Navigation requested before the browser existed.
};

std::mutex g_mutex;
std::map<int, std::shared_ptr<Tab>> g_tabs;
std::atomic<int> g_next_id{0};
cef_color_t g_background = 0xFFFFFFFF;

std::shared_ptr<Tab> Find(int id) {
  std::lock_guard<std::mutex> lock(g_mutex);
  auto it = g_tabs.find(id);
  return it == g_tabs.end() ? nullptr : it->second;
}

CefRefPtr<CefBrowser> BrowserOf(int id) {
  std::lock_guard<std::mutex> lock(g_mutex);
  auto it = g_tabs.find(id);
  return it == g_tabs.end() ? nullptr : it->second->browser;
}

// Positions the native browser window. Safe to call from the WinUI thread: the
// browser HWND is owned by CEF's UI thread but Win32 marshals the messages.
void ApplyGeometry(HWND hwnd, RECT r, int radius, bool visible) {
  if (!hwnd || !::IsWindow(hwnd))
    return;
  const int w = r.right - r.left;
  const int h = r.bottom - r.top;
  UINT flags = SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW);
  ::SetWindowPos(hwnd, HWND_TOP, r.left, r.top, w, h, flags);
  if (visible && w > 0 && h > 0) {
    HRGN rgn = radius > 0
                   ? ::CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2)
                   : nullptr;
    ::SetWindowRgn(hwnd, rgn, TRUE);  // The system owns |rgn| afterwards.
  }
}

}  // namespace

void OnBrowserCreated(int id, CefRefPtr<CefBrowser> browser) {
  HWND hwnd = browser->GetHost()->GetWindowHandle();
  RECT bounds;
  int radius;
  bool visible, closing;
  std::wstring pending;
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_tabs.find(id);
    if (it == g_tabs.end())
      return;
    Tab& tab = *it->second;
    tab.browser = browser;
    tab.hwnd = hwnd;
    bounds = tab.bounds;
    radius = tab.radius;
    visible = tab.visible;
    closing = tab.closing;
    pending.swap(tab.pending_url);
  }

  if (closing) {
    browser->GetHost()->CloseBrowser(true);
    return;
  }
  ApplyGeometry(hwnd, bounds, radius, visible);
  if (!pending.empty())
    browser->GetMainFrame()->LoadURL(pending);
  if (g_callbacks.on_created)
    g_callbacks.on_created(id);
}

void OnBrowserClosed(int id) {
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_tabs.erase(id);
  }
  if (g_callbacks.on_closed)
    g_callbacks.on_closed(id);
}

}  // namespace wb

using namespace wb;

// ---- Process ----------------------------------------------------------------------

WB_API int __stdcall wb_initialize(const WbCallbacks* callbacks,
                                   const wchar_t* subprocess_path,
                                   const wchar_t* cache_dir,
                                   unsigned int background_argb) {
  if (callbacks)
    g_callbacks = *callbacks;
  g_background = background_argb;

  CefMainArgs main_args(::GetModuleHandleW(nullptr));
  CefSettings settings;
  settings.no_sandbox = true;
  settings.multi_threaded_message_loop = true;
  settings.persist_session_cookies = true;
  settings.use_views_default_popup = true;
  settings.background_color = background_argb;
  settings.log_severity = LOGSEVERITY_WARNING;
  CefString(&settings.browser_subprocess_path) = subprocess_path;

  std::wstring root = cache_dir;
  CefString(&settings.root_cache_path) = root;
  CefString(&settings.cache_path) = root + L"\\Default";
  CefString(&settings.log_file) = root + L"\\cef.log";

  return CefInitialize(main_args, settings, nullptr, nullptr) ? 1 : 0;
}

WB_API void __stdcall wb_shutdown() {
  CefShutdown();
}

// ---- Tabs ---------------------------------------------------------------------------

WB_API int __stdcall wb_create(HWND parent, const wchar_t* url,
                               int x, int y, int width, int height, int visible) {
  const int id = ++g_next_id;
  auto tab = std::make_shared<Tab>();
  tab->bounds = {x, y, x + width, y + height};
  tab->visible = visible != 0;
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_tabs[id] = tab;
  }

  std::wstring start_url = url ? url : L"";
  RunOnUI([id, parent, start_url, x, y, width, height, visible] {
    CefWindowInfo info;
    info.SetAsChild(parent, CefRect(x, y, width, height));
    if (!visible)
      info.style &= ~WS_VISIBLE;

    CefBrowserSettings browser_settings;
    browser_settings.background_color = g_background;

    CefBrowserHost::CreateBrowser(info, new TabClient(id), start_url,
                                  browser_settings, nullptr, nullptr);
  });
  return id;
}

WB_API void __stdcall wb_close(int id) {
  CefRefPtr<CefBrowser> browser;
  HWND hwnd = nullptr;
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_tabs.find(id);
    if (it == g_tabs.end() || it->second->closing)
      return;
    it->second->closing = true;
    browser = it->second->browser;
    hwnd = it->second->hwnd;
  }
  if (hwnd)
    ::ShowWindow(hwnd, SW_HIDE);
  // If the browser doesn't exist yet OnBrowserCreated closes it.
  if (browser)
    RunOnUI([browser] { browser->GetHost()->CloseBrowser(true); });
}

WB_API void __stdcall wb_close_all() {
  std::vector<int> ids;
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    for (auto& [id, tab] : g_tabs)
      ids.push_back(id);
  }
  for (int id : ids)
    wb_close(id);
}

WB_API int __stdcall wb_count() {
  std::lock_guard<std::mutex> lock(g_mutex);
  return static_cast<int>(g_tabs.size());
}

// ---- Geometry -------------------------------------------------------------------------

WB_API void __stdcall wb_set_bounds(int id, int x, int y, int width, int height, int corner_radius) {
  HWND hwnd;
  RECT r = {x, y, x + width, y + height};
  bool visible;
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_tabs.find(id);
    if (it == g_tabs.end())
      return;
    it->second->bounds = r;
    it->second->radius = corner_radius;
    hwnd = it->second->hwnd;
    visible = it->second->visible && !it->second->closing;
  }
  ApplyGeometry(hwnd, r, corner_radius, visible);
}

WB_API void __stdcall wb_set_visible(int id, int visible) {
  HWND hwnd;
  RECT r;
  int radius;
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_tabs.find(id);
    if (it == g_tabs.end())
      return;
    it->second->visible = visible != 0;
    hwnd = it->second->hwnd;
    r = it->second->bounds;
    radius = it->second->radius;
    if (it->second->closing)
      visible = 0;
  }
  ApplyGeometry(hwnd, r, radius, visible != 0);
}

WB_API void __stdcall wb_focus(int id) {
  if (auto b = BrowserOf(id))
    RunOnUI([b] { b->GetHost()->SetFocus(true); });
}

// ---- Navigation -----------------------------------------------------------------------

WB_API void __stdcall wb_navigate(int id, const wchar_t* url) {
  std::wstring target = url ? url : L"";
  {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_tabs.find(id);
    if (it == g_tabs.end())
      return;
    if (!it->second->browser) {
      it->second->pending_url = target;
      return;
    }
  }
  if (auto b = BrowserOf(id))
    b->GetMainFrame()->LoadURL(target);
}

WB_API void __stdcall wb_go_back(int id) {
  if (auto b = BrowserOf(id)) b->GoBack();
}

WB_API void __stdcall wb_go_forward(int id) {
  if (auto b = BrowserOf(id)) b->GoForward();
}

WB_API void __stdcall wb_reload(int id, int ignore_cache) {
  if (auto b = BrowserOf(id)) {
    if (ignore_cache)
      b->ReloadIgnoreCache();
    else
      b->Reload();
  }
}

WB_API void __stdcall wb_stop(int id) {
  if (auto b = BrowserOf(id)) b->StopLoad();
}

// ---- Page tools -------------------------------------------------------------------------

WB_API void __stdcall wb_zoom(int id, int direction) {
  if (auto b = BrowserOf(id)) {
    cef_zoom_command_t cmd = direction > 0   ? CEF_ZOOM_COMMAND_IN
                             : direction < 0 ? CEF_ZOOM_COMMAND_OUT
                                             : CEF_ZOOM_COMMAND_RESET;
    RunOnUI([b, cmd] {
      if (b->GetHost()->CanZoom(cmd))
        b->GetHost()->Zoom(cmd);
    });
  }
}

WB_API void __stdcall wb_show_devtools(int id) {
  if (auto b = BrowserOf(id)) {
    RunOnUI([b] {
      CefWindowInfo info;
      info.SetAsPopup(nullptr, "DevTools");
      CefBrowserSettings settings;
      b->GetHost()->ShowDevTools(info, new PopupClient(), settings, CefPoint());
    });
  }
}

WB_API void __stdcall wb_find(int id, const wchar_t* text, int forward) {
  std::wstring query = text ? text : L"";
  if (auto b = BrowserOf(id)) {
    RunOnUI([b, query, forward] {
      if (query.empty())
        b->GetHost()->StopFinding(true);
      else
        b->GetHost()->Find(query, forward != 0, false, true);
    });
  }
}

WB_API void __stdcall wb_stop_find(int id) {
  if (auto b = BrowserOf(id))
    RunOnUI([b] { b->GetHost()->StopFinding(true); });
}

WB_API void __stdcall wb_print(int id) {
  if (auto b = BrowserOf(id))
    RunOnUI([b] { b->GetHost()->Print(); });
}

WB_API void __stdcall wb_exit_fullscreen(int id) {
  if (auto b = BrowserOf(id))
    RunOnUI([b] { b->GetHost()->ExitFullscreen(true); });
}
