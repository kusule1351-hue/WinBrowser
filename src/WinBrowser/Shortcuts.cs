using WinBrowser.Native;

namespace WinBrowser;

public enum BrowserCommand
{
    NewTab,
    CloseTab,
    ReopenClosedTab,
    FocusAddress,
    ToggleSidebar,
    NextTab,
    PreviousTab,
    Reload,
    HardReload,
    Back,
    Forward,
    DevTools,
    Find,
    FindNext,
    Print,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    CopyUrl,
    ToggleFullscreen,
    LastTab,
    Tab1, Tab2, Tab3, Tab4, Tab5, Tab6, Tab7, Tab8,
}

/// <summary>
/// Keyboard map shared by the WinUI chrome and the page (CEF forwards key presses to us
/// before the page sees them). Pure function so it can run on CEF's thread.
/// </summary>
public static class Shortcuts
{
    private const int VK_TAB = 0x09, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_NUMPAD0 = 0x60,
        VK_ADD = 0x6B, VK_SUBTRACT = 0x6D, VK_F3 = 0x72, VK_F4 = 0x73, VK_F5 = 0x74, VK_F6 = 0x75,
        VK_F11 = 0x7A, VK_F12 = 0x7B, VK_OEM_PLUS = 0xBB, VK_OEM_MINUS = 0xBD;

    public static BrowserCommand? Map(int vk, KeyModifiers mods)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Ctrl);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool alt = mods.HasFlag(KeyModifiers.Alt);

        if (ctrl && alt) return null; // AltGr – leave it to text input.

        if (ctrl)
        {
            switch (vk)
            {
                case 'T': return shift ? BrowserCommand.ReopenClosedTab : BrowserCommand.NewTab;
                case 'W': case VK_F4: return BrowserCommand.CloseTab;
                case 'L': return BrowserCommand.FocusAddress;
                case 'S' when !shift: return BrowserCommand.ToggleSidebar;
                case VK_TAB: return shift ? BrowserCommand.PreviousTab : BrowserCommand.NextTab;
                case 'R': return shift ? BrowserCommand.HardReload : BrowserCommand.Reload;
                case VK_F5: return BrowserCommand.HardReload;
                case 'I' when shift: return BrowserCommand.DevTools;
                case 'F' when !shift: return BrowserCommand.Find;
                case 'G' when !shift: return BrowserCommand.FindNext;
                case 'P' when !shift: return BrowserCommand.Print;
                case 'C' when shift: return BrowserCommand.CopyUrl;
                case VK_OEM_PLUS: case VK_ADD: return BrowserCommand.ZoomIn;
                case VK_OEM_MINUS: case VK_SUBTRACT: return BrowserCommand.ZoomOut;
                case '0': case VK_NUMPAD0: return BrowserCommand.ZoomReset;
                case >= '1' and <= '8' when !shift: return BrowserCommand.Tab1 + (vk - '1');
                case '9' when !shift: return BrowserCommand.LastTab;
            }
            return null;
        }

        if (alt)
        {
            return vk switch
            {
                VK_LEFT => BrowserCommand.Back,
                VK_RIGHT => BrowserCommand.Forward,
                'D' => BrowserCommand.FocusAddress,
                _ => null,
            };
        }

        return vk switch
        {
            VK_F5 => shift ? BrowserCommand.HardReload : BrowserCommand.Reload,
            VK_F3 => BrowserCommand.FindNext,
            VK_F6 => BrowserCommand.FocusAddress,
            VK_F11 => BrowserCommand.ToggleFullscreen,
            VK_F12 => BrowserCommand.DevTools,
            _ => null,
        };
    }
}
