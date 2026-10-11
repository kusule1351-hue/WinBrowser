using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WinBrowser.Native;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>
/// Events raised by Chromium. All of them are delivered on CEF's UI thread;
/// subscribers must marshal to the WinUI dispatcher themselves.
/// </summary>
public interface ICefHostEvents
{
    void OnCreated(int id);
    void OnTitle(int id, string title);
    void OnAddress(int id, string url);
    void OnLoading(int id, bool isLoading, bool canGoBack, bool canGoForward);
    void OnFavicon(int id, string url);
    void OnNewTab(int id, string url, bool foreground);
    void OnClosed(int id);
    void OnFullscreen(int id, bool fullscreen);
    /// <summary>Called synchronously while a key is pressed inside a page. Return true to consume it.</summary>
    bool OnShortcut(int id, int virtualKey, KeyModifiers modifiers);
    void OnGotFocus(int id);
}

/// <summary>P/Invoke surface of WinBrowserCore.dll (see native/core/wb_api.h).</summary>
public static unsafe partial class CefHost
{
    private const string Dll = "WinBrowserCore.dll";

    public static ICefHostEvents? Events { get; set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WbCallbacks
    {
        public delegate* unmanaged[Stdcall]<int, void> OnCreated;
        public delegate* unmanaged[Stdcall]<int, char*, void> OnTitle;
        public delegate* unmanaged[Stdcall]<int, char*, void> OnAddress;
        public delegate* unmanaged[Stdcall]<int, int, int, int, void> OnLoading;
        public delegate* unmanaged[Stdcall]<int, char*, void> OnFavicon;
        public delegate* unmanaged[Stdcall]<int, char*, int, void> OnNewTab;
        public delegate* unmanaged[Stdcall]<int, void> OnClosed;
        public delegate* unmanaged[Stdcall]<int, int, void> OnFullscreen;
        public delegate* unmanaged[Stdcall]<int, int, int, int> OnShortcut;
        public delegate* unmanaged[Stdcall]<int, void> OnGotFocus;
    }

    public static bool Initialize()
    {
        var baseDir = AppContext.BaseDirectory;
        var helper = Path.Combine(baseDir, "WinBrowser.Helper.exe");
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinBrowser", "Profile");
        Directory.CreateDirectory(cache);

        var callbacks = new WbCallbacks
        {
            OnCreated = &CbCreated,
            OnTitle = &CbTitle,
            OnAddress = &CbAddress,
            OnLoading = &CbLoading,
            OnFavicon = &CbFavicon,
            OnNewTab = &CbNewTab,
            OnClosed = &CbClosed,
            OnFullscreen = &CbFullscreen,
            OnShortcut = &CbShortcut,
            OnGotFocus = &CbGotFocus,
        };

        try
        {
            // Matches the content placeholder colour so new tabs don't flash white.
            return wb_initialize(&callbacks, helper, cache, 0xFF1C1C21) != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    // ---- Callbacks (CEF UI thread) -------------------------------------------------

    private static string Str(char* s) => s == null ? string.Empty : new string(s);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbCreated(int id) => Events?.OnCreated(id);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbTitle(int id, char* title) => Events?.OnTitle(id, Str(title));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbAddress(int id, char* url) => Events?.OnAddress(id, Str(url));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbLoading(int id, int loading, int back, int fwd) =>
        Events?.OnLoading(id, loading != 0, back != 0, fwd != 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbFavicon(int id, char* url) => Events?.OnFavicon(id, Str(url));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbNewTab(int id, char* url, int foreground) =>
        Events?.OnNewTab(id, Str(url), foreground != 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbClosed(int id) => Events?.OnClosed(id);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbFullscreen(int id, int fs) => Events?.OnFullscreen(id, fs != 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CbShortcut(int id, int vk, int mods)
    {
        try { return Events?.OnShortcut(id, vk, (KeyModifiers)mods) == true ? 1 : 0; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CbGotFocus(int id) => Events?.OnGotFocus(id);

    // ---- Imports -------------------------------------------------------------------

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int wb_initialize(WbCallbacks* callbacks, string subprocessPath, string cacheDir, uint backgroundArgb);

    [LibraryImport(Dll, EntryPoint = "wb_shutdown")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Shutdown();

    [LibraryImport(Dll, EntryPoint = "wb_create", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial int Create(nint parent, string url, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool visible);

    [LibraryImport(Dll, EntryPoint = "wb_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Close(int id);

    [LibraryImport(Dll, EntryPoint = "wb_close_all")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void CloseAll();

    [LibraryImport(Dll, EntryPoint = "wb_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial int Count();

    [LibraryImport(Dll, EntryPoint = "wb_set_bounds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void SetBounds(int id, int x, int y, int width, int height, int cornerRadius);

    [LibraryImport(Dll, EntryPoint = "wb_set_visible")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void SetVisible(int id, [MarshalAs(UnmanagedType.Bool)] bool visible);

    [LibraryImport(Dll, EntryPoint = "wb_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Focus(int id);

    [LibraryImport(Dll, EntryPoint = "wb_navigate", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Navigate(int id, string url);

    [LibraryImport(Dll, EntryPoint = "wb_go_back")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void GoBack(int id);

    [LibraryImport(Dll, EntryPoint = "wb_go_forward")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void GoForward(int id);

    [LibraryImport(Dll, EntryPoint = "wb_reload")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Reload(int id, [MarshalAs(UnmanagedType.Bool)] bool ignoreCache);

    [LibraryImport(Dll, EntryPoint = "wb_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Stop(int id);

    [LibraryImport(Dll, EntryPoint = "wb_zoom")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Zoom(int id, int direction);

    [LibraryImport(Dll, EntryPoint = "wb_show_devtools")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void ShowDevTools(int id);

    [LibraryImport(Dll, EntryPoint = "wb_find", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Find(int id, string text, [MarshalAs(UnmanagedType.Bool)] bool forward);

    [LibraryImport(Dll, EntryPoint = "wb_stop_find")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void StopFind(int id);

    [LibraryImport(Dll, EntryPoint = "wb_print")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void Print(int id);

    [LibraryImport(Dll, EntryPoint = "wb_exit_fullscreen")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial void ExitFullscreen(int id);
}

internal static partial class NativeMessageBox
{
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hwnd, string text, string caption, uint type);

    public static void ShowError(string text) => MessageBox(0, text, "WinBrowser", 0x10 /* MB_ICONERROR */);
}
