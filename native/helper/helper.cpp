// Sub-process entry point for Chromium's renderer, GPU and utility processes.
#include <windows.h>

#include "include/cef_app.h"

int APIENTRY wWinMain(HINSTANCE instance, HINSTANCE, LPWSTR, int) {
  CefMainArgs main_args(instance);
  return CefExecuteProcess(main_args, nullptr, nullptr);
}
