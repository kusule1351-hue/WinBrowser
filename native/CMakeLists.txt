cmake_minimum_required(VERSION 3.21)
project(WinBrowserNative CXX)

# Location of the extracted CEF binary distribution.
set(CEF_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../third_party/cef" CACHE PATH "CEF binary distribution")
set(CMAKE_MODULE_PATH ${CMAKE_MODULE_PATH} "${CEF_ROOT}/cmake")

# The WinUI shell is a .NET process, so we use a separate helper exe for the
# Chromium sub-processes and run without the bootstrap/sandbox.
set(USE_SANDBOX OFF CACHE BOOL "" FORCE)

find_package(CEF REQUIRED)
add_subdirectory(${CEF_LIBCEF_DLL_WRAPPER_PATH} libcef_dll_wrapper)
ADD_LOGICAL_TARGET("libcef_lib" "${CEF_LIB_DEBUG}" "${CEF_LIB_RELEASE}")

# Everything (CEF runtime + our binaries) lands in one folder that the C# project copies.
set(WB_BIN_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../build/native/bin")
set(CEF_TARGET_OUT_DIR "${WB_BIN_DIR}")

# ---- WinBrowserCore.dll : CEF host with a flat C API --------------------------
add_library(WinBrowserCore SHARED
  core/wb_api.h
  core/wb_core.cpp
  core/wb_client.h
  core/wb_client.cpp
)
SET_LIBRARY_TARGET_PROPERTIES(WinBrowserCore)
target_compile_definitions(WinBrowserCore PRIVATE WB_CORE_EXPORTS)
add_dependencies(WinBrowserCore libcef_dll_wrapper)
target_link_libraries(WinBrowserCore libcef_lib libcef_dll_wrapper ${CEF_STANDARD_LIBS})
set_target_properties(WinBrowserCore PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${WB_BIN_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${WB_BIN_DIR}"
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/lib")

# ---- WinBrowser.Helper.exe : renderer/GPU/utility sub-processes ---------------
add_executable(WinBrowser.Helper WIN32 helper/helper.cpp)
SET_EXECUTABLE_TARGET_PROPERTIES(WinBrowser.Helper)
add_dependencies(WinBrowser.Helper libcef_dll_wrapper)
target_link_libraries(WinBrowser.Helper libcef_lib libcef_dll_wrapper ${CEF_STANDARD_LIBS})
set_target_properties(WinBrowser.Helper PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${WB_BIN_DIR}"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${WB_BIN_DIR}")
ADD_WINDOWS_MANIFEST("${CMAKE_CURRENT_SOURCE_DIR}/helper" WinBrowser.Helper "exe")

# Copy the CEF runtime next to our binaries.
COPY_FILES(WinBrowserCore "${CEF_BINARY_FILES}" "${CEF_BINARY_DIR}" "${WB_BIN_DIR}")
COPY_FILES(WinBrowserCore "${CEF_RESOURCE_FILES}" "${CEF_RESOURCE_DIR}" "${WB_BIN_DIR}")

PRINT_CEF_CONFIG()
