using Microsoft.UI.Xaml;
using WinBrowser.Native;

namespace WinBrowser;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // CEF must be initialized on the UI thread; with the multi-threaded message
        // loop CEF runs its own UI thread and WinUI keeps owning this one.
        if (!CefHost.Initialize())
        {
            NativeMessageBox.ShowError("WinBrowser could not start Chromium (CEF).\n\n" +
                "Make sure libcef.dll, WinBrowserCore.dll and WinBrowser.Helper.exe are next to WinBrowser.exe.");
            Exit();
            return;
        }

        var startUrl = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
        _window = new MainWindow(startUrl);
        _window.Activate();
    }
}
