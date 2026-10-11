#include "wb_client.h"

#include <shlobj.h>

#include <set>
#include <string>

#include "include/cef_id_mappers.h"
#include "include/cef_task.h"
#include "include/wrapper/cef_helpers.h"

namespace wb {

namespace {

class FnTask : public CefTask {
 public:
  explicit FnTask(std::function<void()> fn) : fn_(std::move(fn)) {}
  void Execute() override { fn_(); }

 private:
  std::function<void()> fn_;
  IMPLEMENT_REFCOUNTING(FnTask);
};

bool OpensNewTab(cef_window_open_disposition_t d) {
  return d == CEF_WOD_NEW_FOREGROUND_TAB || d == CEF_WOD_NEW_BACKGROUND_TAB ||
         d == CEF_WOD_NEW_WINDOW || d == CEF_WOD_SINGLETON_TAB ||
         d == CEF_WOD_SWITCH_TO_TAB || d == CEF_WOD_OFF_THE_RECORD;
}

bool ContinueDownload(const CefString& suggested_name,
                      CefRefPtr<CefBeforeDownloadCallback> callback) {
  std::wstring path;
  PWSTR downloads = nullptr;
  if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Downloads, 0, nullptr, &downloads))) {
    path = downloads;
    path += L"\\";
    CoTaskMemFree(downloads);
  }
  path += suggested_name.ToWString();
  // Show the Save As dialog, pre-filled with the Downloads folder.
  callback->Continue(path, true);
  return true;
}

}  // namespace

WbCallbacks g_callbacks = {};

void RunOnUI(std::function<void()> fn) {
  if (CefCurrentlyOn(TID_UI)) {
    fn();
  } else {
    CefPostTask(TID_UI, new FnTask(std::move(fn)));
  }
}

// ---- Life span ------------------------------------------------------------------

bool TabClient::OnBeforePopup(CefRefPtr<CefBrowser> browser,
                              CefRefPtr<CefFrame> frame,
                              int popup_id,
                              const CefString& target_url,
                              const CefString& target_frame_name,
                              cef_window_open_disposition_t target_disposition,
                              bool user_gesture,
                              const CefPopupFeatures& popupFeatures,
                              CefWindowInfo& windowInfo,
                              CefRefPtr<CefClient>& client,
                              CefBrowserSettings& settings,
                              CefRefPtr<CefDictionaryValue>& extra_info,
                              bool* no_javascript_access) {
  if (target_disposition == CEF_WOD_NEW_POPUP) {
    // Sized popups (OAuth sign-in, payment windows...) keep their opener relation.
    client = new PopupClient();
    return false;
  }
  if (g_callbacks.on_new_tab) {
    g_callbacks.on_new_tab(id_, target_url.ToWString().c_str(),
                           target_disposition != CEF_WOD_NEW_BACKGROUND_TAB);
  }
  return true;
}

void TabClient::OnAfterCreated(CefRefPtr<CefBrowser> browser) {
  OnBrowserCreated(id_, browser);
}

bool TabClient::DoClose(CefRefPtr<CefBrowser> browser) {
  // The browser is a child of the WinUI window. Returning false would make CEF send
  // WM_CLOSE to that top-level window (closing the whole app), so instead tear down
  // just the browser's own window; OnBeforeClose follows.
  HWND hwnd = browser->GetHost()->GetWindowHandle();
  if (hwnd) {
    CefPostTask(TID_UI, new FnTask([hwnd] {
                  if (::IsWindow(hwnd))
                    ::DestroyWindow(hwnd);
                }));
  }
  return true;
}

void TabClient::OnBeforeClose(CefRefPtr<CefBrowser> browser) {
  OnBrowserClosed(id_);
}

// ---- Display --------------------------------------------------------------------

void TabClient::OnAddressChange(CefRefPtr<CefBrowser> browser,
                                CefRefPtr<CefFrame> frame,
                                const CefString& url) {
  if (frame->IsMain() && g_callbacks.on_address)
    g_callbacks.on_address(id_, url.ToWString().c_str());
}

void TabClient::OnTitleChange(CefRefPtr<CefBrowser> browser, const CefString& title) {
  if (g_callbacks.on_title)
    g_callbacks.on_title(id_, title.ToWString().c_str());
}

void TabClient::OnFaviconURLChange(CefRefPtr<CefBrowser> browser,
                                   const std::vector<CefString>& icon_urls) {
  if (!g_callbacks.on_favicon)
    return;
  // Prefer raster formats the shell can decode; fall back to whatever came first.
  std::wstring best;
  for (const auto& u : icon_urls) {
    std::wstring s = u.ToWString();
    if (best.empty())
      best = s;
    if (s.find(L".svg") == std::wstring::npos) {
      best = s;
      break;
    }
  }
  g_callbacks.on_favicon(id_, best.c_str());
}

void TabClient::OnFullscreenModeChange(CefRefPtr<CefBrowser> browser, bool fullscreen) {
  if (g_callbacks.on_fullscreen)
    g_callbacks.on_fullscreen(id_, fullscreen ? 1 : 0);
}

// ---- Load -----------------------------------------------------------------------

void TabClient::OnLoadingStateChange(CefRefPtr<CefBrowser> browser,
                                     bool isLoading,
                                     bool canGoBack,
                                     bool canGoForward) {
  if (g_callbacks.on_loading)
    g_callbacks.on_loading(id_, isLoading, canGoBack, canGoForward);
}

// ---- Keyboard -------------------------------------------------------------------

bool TabClient::OnPreKeyEvent(CefRefPtr<CefBrowser> browser,
                              const CefKeyEvent& event,
                              CefEventHandle os_event,
                              bool* is_keyboard_shortcut) {
  if (event.type != KEYEVENT_RAWKEYDOWN || !g_callbacks.on_shortcut)
    return false;

  int mods = 0;
  if (event.modifiers & EVENTFLAG_SHIFT_DOWN) mods |= WB_MOD_SHIFT;
  if (event.modifiers & EVENTFLAG_CONTROL_DOWN) mods |= WB_MOD_CTRL;
  if (event.modifiers & EVENTFLAG_ALT_DOWN) mods |= WB_MOD_ALT;

  const int vk = event.windows_key_code;
  const bool function_key = vk >= VK_F1 && vk <= VK_F24;
  if (!(mods & (WB_MOD_CTRL | WB_MOD_ALT)) && !function_key)
    return false;

  return g_callbacks.on_shortcut(id_, vk, mods) != 0;
}

// ---- Focus ----------------------------------------------------------------------

void TabClient::OnGotFocus(CefRefPtr<CefBrowser> browser) {
  if (g_callbacks.on_got_focus)
    g_callbacks.on_got_focus(id_);
}

// ---- Requests -------------------------------------------------------------------

bool TabClient::OnOpenURLFromTab(CefRefPtr<CefBrowser> browser,
                                 CefRefPtr<CefFrame> frame,
                                 const CefString& target_url,
                                 cef_window_open_disposition_t target_disposition,
                                 bool user_gesture) {
  // Ctrl/middle-click on links.
  if (!OpensNewTab(target_disposition) || !g_callbacks.on_new_tab)
    return false;
  g_callbacks.on_new_tab(id_, target_url.ToWString().c_str(),
                         target_disposition != CEF_WOD_NEW_BACKGROUND_TAB);
  return true;
}

// ---- Downloads ------------------------------------------------------------------

bool TabClient::OnBeforeDownload(CefRefPtr<CefBrowser> browser,
                                 CefRefPtr<CefDownloadItem> download_item,
                                 const CefString& suggested_name,
                                 CefRefPtr<CefBeforeDownloadCallback> callback) {
  return ContinueDownload(suggested_name, callback);
}

// ---- Chrome commands ----------------------------------------------------------------

bool TabClient::OnChromeCommand(CefRefPtr<CefBrowser> browser,
                                int command_id,
                                cef_window_open_disposition_t disposition) {
  // "New tab/window" belong in our sidebar, not in a bare Chromium window.
  static const int kNewTab = cef_id_for_command_id_name("IDC_NEW_TAB");
  static const int kNewWindow = cef_id_for_command_id_name("IDC_NEW_WINDOW");
  if (command_id == kNewTab || command_id == kNewWindow) {
    if (g_callbacks.on_shortcut)
      g_callbacks.on_shortcut(id_, 'T', WB_MOD_CTRL);
    return true;
  }

  // Page-level commands Chrome may run itself; everything else (new incognito
  // window, history, downloads, bookmarks, settings...) has no UI here.
  static const std::set<int> kAllowed = [] {
    std::set<int> ids;
    for (const char* name :
         {"IDC_BACK", "IDC_FORWARD", "IDC_RELOAD", "IDC_RELOAD_BYPASSING_CACHE",
          "IDC_RELOAD_CLEARING_CACHE", "IDC_STOP", "IDC_PRINT", "IDC_BASIC_PRINT",
          "IDC_SAVE_PAGE", "IDC_VIEW_SOURCE", "IDC_ZOOM_PLUS", "IDC_ZOOM_MINUS",
          "IDC_ZOOM_NORMAL", "IDC_DEV_TOOLS", "IDC_DEV_TOOLS_CONSOLE",
          "IDC_DEV_TOOLS_INSPECT", "IDC_DEV_TOOLS_TOGGLE", "IDC_DEV_TOOLS_DEVICES",
          "IDC_CUT", "IDC_COPY", "IDC_PASTE", "IDC_FIND"}) {
      ids.insert(cef_id_for_command_id_name(name));
    }
    return ids;
  }();
  return kAllowed.count(command_id) == 0;
}

// ---- Context menu ---------------------------------------------------------------------

void TabClient::OnBeforeContextMenu(CefRefPtr<CefBrowser> browser,
                                    CefRefPtr<CefFrame> frame,
                                    CefRefPtr<CefContextMenuParams> params,
                                    CefRefPtr<CefMenuModel> model) {
  // Single-window browser: links open in tabs only.
  for (const char* name : {"IDC_CONTENT_CONTEXT_OPENLINKNEWWINDOW",
                           "IDC_CONTENT_CONTEXT_OPENLINKOFFTHERECORD",
                           "IDC_CONTENT_CONTEXT_OPENLINKINPROFILE"}) {
    model->Remove(cef_id_for_command_id_name(name));
  }
}

bool PopupClient::OnBeforeDownload(CefRefPtr<CefBrowser> browser,
                                   CefRefPtr<CefDownloadItem> download_item,
                                   const CefString& suggested_name,
                                   CefRefPtr<CefBeforeDownloadCallback> callback) {
  return ContinueDownload(suggested_name, callback);
}

}  // namespace wb
