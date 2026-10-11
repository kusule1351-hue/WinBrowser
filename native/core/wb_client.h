#pragma once

#include <functional>

#include "include/cef_client.h"
#include "wb_api.h"

namespace wb {

// Global callbacks provided by the shell (copied in wb_initialize).
extern WbCallbacks g_callbacks;

// Runs |fn| on CEF's browser UI thread (inline if already there).
void RunOnUI(std::function<void()> fn);

// Called by TabClient as browser lifetime events happen (implemented in wb_core.cpp).
void OnBrowserCreated(int id, CefRefPtr<CefBrowser> browser);
void OnBrowserClosed(int id);

// One client per tab. Everything it observes is forwarded to the shell tagged with
// the tab id.
class TabClient : public CefClient,
                  public CefLifeSpanHandler,
                  public CefDisplayHandler,
                  public CefLoadHandler,
                  public CefKeyboardHandler,
                  public CefFocusHandler,
                  public CefRequestHandler,
                  public CefDownloadHandler,
                  public CefCommandHandler,
                  public CefContextMenuHandler {
 public:
  explicit TabClient(int id) : id_(id) {}

  CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override { return this; }
  CefRefPtr<CefDisplayHandler> GetDisplayHandler() override { return this; }
  CefRefPtr<CefLoadHandler> GetLoadHandler() override { return this; }
  CefRefPtr<CefKeyboardHandler> GetKeyboardHandler() override { return this; }
  CefRefPtr<CefFocusHandler> GetFocusHandler() override { return this; }
  CefRefPtr<CefRequestHandler> GetRequestHandler() override { return this; }
  CefRefPtr<CefDownloadHandler> GetDownloadHandler() override { return this; }
  CefRefPtr<CefCommandHandler> GetCommandHandler() override { return this; }
  CefRefPtr<CefContextMenuHandler> GetContextMenuHandler() override { return this; }

  // CefLifeSpanHandler
  bool OnBeforePopup(CefRefPtr<CefBrowser> browser,
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
                     bool* no_javascript_access) override;
  void OnAfterCreated(CefRefPtr<CefBrowser> browser) override;
  bool DoClose(CefRefPtr<CefBrowser> browser) override;
  void OnBeforeClose(CefRefPtr<CefBrowser> browser) override;

  // CefDisplayHandler
  void OnAddressChange(CefRefPtr<CefBrowser> browser,
                       CefRefPtr<CefFrame> frame,
                       const CefString& url) override;
  void OnTitleChange(CefRefPtr<CefBrowser> browser, const CefString& title) override;
  void OnFaviconURLChange(CefRefPtr<CefBrowser> browser,
                          const std::vector<CefString>& icon_urls) override;
  void OnFullscreenModeChange(CefRefPtr<CefBrowser> browser, bool fullscreen) override;

  // CefLoadHandler
  void OnLoadingStateChange(CefRefPtr<CefBrowser> browser,
                            bool isLoading,
                            bool canGoBack,
                            bool canGoForward) override;

  // CefKeyboardHandler
  bool OnPreKeyEvent(CefRefPtr<CefBrowser> browser,
                     const CefKeyEvent& event,
                     CefEventHandle os_event,
                     bool* is_keyboard_shortcut) override;

  // CefFocusHandler
  void OnGotFocus(CefRefPtr<CefBrowser> browser) override;

  // CefRequestHandler
  bool OnOpenURLFromTab(CefRefPtr<CefBrowser> browser,
                        CefRefPtr<CefFrame> frame,
                        const CefString& target_url,
                        cef_window_open_disposition_t target_disposition,
                        bool user_gesture) override;

  // CefDownloadHandler
  bool OnBeforeDownload(CefRefPtr<CefBrowser> browser,
                        CefRefPtr<CefDownloadItem> download_item,
                        const CefString& suggested_name,
                        CefRefPtr<CefBeforeDownloadCallback> callback) override;

  // CefCommandHandler (Chrome-style browsers still run Chrome's command layer)
  bool OnChromeCommand(CefRefPtr<CefBrowser> browser,
                       int command_id,
                       cef_window_open_disposition_t disposition) override;

  // CefContextMenuHandler
  void OnBeforeContextMenu(CefRefPtr<CefBrowser> browser,
                           CefRefPtr<CefFrame> frame,
                           CefRefPtr<CefContextMenuParams> params,
                           CefRefPtr<CefMenuModel> model) override;

 private:
  const int id_;
  IMPLEMENT_REFCOUNTING(TabClient);
};

// Client for real popup windows (window.open with features) and DevTools: they are
// top-level windows that manage themselves and must not touch tab state.
class PopupClient : public CefClient, public CefDownloadHandler {
 public:
  CefRefPtr<CefDownloadHandler> GetDownloadHandler() override { return this; }
  bool OnBeforeDownload(CefRefPtr<CefBrowser> browser,
                        CefRefPtr<CefDownloadItem> download_item,
                        const CefString& suggested_name,
                        CefRefPtr<CefBeforeDownloadCallback> callback) override;

 private:
  IMPLEMENT_REFCOUNTING(PopupClient);
};

}  // namespace wb
