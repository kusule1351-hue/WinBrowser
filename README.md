# WinBrowser

A minimal, Arc-inspired browser for Windows: **Chromium (CEF 154)** for the pages and a **WinUI 3** shell with vertical tabs, a Mica backdrop and the standard Windows caption buttons.

## Build & run

Requirements: Visual Studio 2022 Build Tools (MSVC + Windows SDK), .NET 10 SDK.

```powershell
.\build.ps1 -Run
```

The first build downloads the CEF minimal distribution (~170 MB) into `third_party\cef`. The app is unpackaged and self-contained with respect to the Windows App SDK. The output folder is `src\WinBrowser\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\`.

## Layout

```
native/                 C++ (CMake), links libcef + libcef_dll_wrapper (/MT)
  core/wb_api.h         flat C API the shell P/Invokes
  core/wb_core.cpp      tab registry, CefInitialize, window geometry
  core/wb_client.cpp    per-tab CefClient (display/load/keyboard/popups/downloads/commands)
  helper/               WinBrowser.Helper.exe – Chromium renderer/GPU/utility sub-processes
src/WinBrowser/         C# WinUI 3 shell
  MainWindow.xaml(.cs)  sidebar, address pill, vertical tabs, page card, title-bar regions
  Native/CefHost.cs     P/Invoke + unmanaged callbacks
  Shortcuts.cs          keyboard map shared by the shell and the page
  Assets/newtab.html    new-tab page
```

### How the pieces fit

- CEF runs with `multi_threaded_message_loop`: Chromium owns its own UI thread, and WinUI keeps the main thread. The shell marshals every callback through the `DispatcherQueue`.
- Each tab is a native Chromium child `HWND` of the WinUI window. It is positioned over the `BrowserHost` card, with rounded corners from `SetWindowRgn`. Inactive tabs are hidden, and restored tabs are created lazily the first time you open them.
- The title bar is extended into the content (`AppWindow.TitleBar.ExtendsContentIntoTitleBar`), so Windows still draws its own minimize, maximize and close buttons. `InputNonClientPointerSource` makes the top strip draggable while leaving the nav buttons and the find bar clickable.
- Closing a tab destroys only that browser's `HWND` (from `DoClose`). Otherwise CEF would send `WM_CLOSE` to the whole app window. Quitting closes all browsers, then calls `CefShutdown`.
- Chrome's own command layer is filtered (`OnChromeCommand`): "new tab/window" opens a sidebar tab, and Chrome-only UI such as history, downloads and incognito is blocked, so no stray Chromium windows appear.

## Shortcuts

| Keys | Action |
|---|---|
| Ctrl+T / Ctrl+Shift+T | New tab / reopen closed tab |
| Ctrl+W, Ctrl+F4, middle-click | Close tab |
| Ctrl+L, Alt+D, F6 | Address bar |
| Ctrl+S | Toggle sidebar |
| Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+1…9 | Switch tabs |
| Alt+← / Alt+→ | Back / forward |
| Ctrl+R, F5 / Ctrl+Shift+R, Ctrl+F5 | Reload / hard reload |
| Ctrl+F, F3 / Ctrl+G | Find in page / next match |
| Ctrl+ + / − / 0 | Zoom |
| Ctrl+Shift+C | Copy page URL |
| F11 | Full screen |
| F12, Ctrl+Shift+I | DevTools |

The profile (cookies, cache, `cef.log`) and `session.json` live in `%LOCALAPPDATA%\WinBrowser`.

## Known limitations

- Native Chromium windows sit above XAML (the "airspace" limitation), so nothing in WinUI can overlap the page. For that reason the find bar lives in the title strip.
- The rounded corners on the page come from a window region, so they are not anti-aliased.
- The default search engine is Google (`Urls.SearchTemplate`).

## License

[MIT](LICENSE). CEF and Chromium, which are downloaded at build time, are distributed under their own BSD-style licenses (see `third_party/cef/LICENSE.txt` and `CREDITS.html`).
