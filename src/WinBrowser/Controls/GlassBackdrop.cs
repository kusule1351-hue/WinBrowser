using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinBrowser.Controls;

/// <summary>
/// Desktop acrylic with a light, dark tint so the desktop shows through the chrome.
/// Unlike the stock DesktopAcrylicBackdrop it stays translucent while the window is inactive.
/// </summary>
public sealed partial class GlassBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);

        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true, // keep the glass when the window loses focus
            Theme = SystemBackdropTheme.Dark,
        };
        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintColor = Color.FromArgb(0xFF, 0x0E, 0x0E, 0x14),
            TintOpacity = 0.38f,
            LuminosityOpacity = 0.62f,
            FallbackColor = Color.FromArgb(0xFF, 0x17, 0x17, 0x1C),
        };
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }
}
