using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using WinBrowser.Controls;
using WinBrowser.Native;
using WinRT.Interop;

namespace WinBrowser;

public sealed partial class MainWindow : Window, ICefHostEvents
{
    private const double SidebarWidth = 260;
    private const double TitleBarHeight = 40;
    private const double PageCornerRadius = 10;
    private const double PageBorder = 1;
    private const double PageGap = 10;

    private readonly ObservableCollection<BrowserTab> _tabs = [];
    private readonly Dictionary<int, BrowserTab> _byId = [];
    private readonly Stack<string> _closedUrls = new();
    private readonly DispatcherQueue _ui;
    private readonly nint _hwnd;
    private readonly string? _startUrl;

    private BrowserTab? _active;
    private bool _sidebarVisible = true;
    private bool _fullscreen;
    private bool _editingAddress;
    private bool _pageFocused;
    private bool _syncingSelection;
    private bool _shuttingDown;
    private bool _readyToClose;
    private bool _pointerOverAddress;

    public MainWindow(string? startUrl)
    {
        InitializeComponent();
        _ui = DispatcherQueue;
        _hwnd = WindowNative.GetWindowHandle(this);
        _startUrl = startUrl;

        CefHost.Events = this;
        TabList.ItemsSource = _tabs;

        ConfigureWindow();

        Root.Loaded += Root_Loaded;
        Root.SizeChanged += (_, _) => UpdateTitleBarRegions();
        Toolbar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        FindBar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        Root.PreviewKeyDown += Root_PreviewKeyDown;
        Root.GotFocus += (_, _) => _pageFocused = false;
        AddressBox.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(AddressBox_PointerPressed), true);
        Activated += MainWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
    }

    // =====================================================================================
    // Window chrome
    // =====================================================================================

    private void ConfigureWindow()
    {
        // Content extends into the title bar; Windows keeps drawing its own
        // minimize / maximize / close buttons in the top-right corner.
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonPressedForegroundColor = Colors.White;

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "WinBrowser.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        // Translucent glass behind the chrome; Mica / solid as fallbacks.
        if (GlassBackdrop.IsSupported)
            SystemBackdrop = new GlassBackdrop();
        else if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        else
            Root.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1B, 0x1B, 0x1F));

        // Open at a comfortable size, centred on the current monitor.
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(1360 * scale), (int)(work.Width * 0.92));
        int height = Math.Min((int)(860 * scale), (int)(work.Height * 0.92));
        AppWindow.MoveAndResize(new RectInt32(
            work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(560 * scale);
            presenter.PreferredMinimumHeight = (int)(360 * scale);
        }
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        Root.XamlRoot.Changed += (_, _) =>
        {
            UpdateTitleBarRegions();
            UpdateBrowserBounds();
        };
        ApplyLayout();
        RestoreSession();
    }

    /// <summary>The whole top strip drags the window, except the buttons that live in it.</summary>
    private void UpdateTitleBarRegions()
    {
        if (Root.XamlRoot is null) return;
        double scale = Root.XamlRoot.RasterizationScale;
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);

        if (_fullscreen)
        {
            source.ClearRegionRects(NonClientRegionKind.Caption);
            source.ClearRegionRects(NonClientRegionKind.Passthrough);
            return;
        }

        source.SetRegionRects(NonClientRegionKind.Caption,
            [new RectInt32(0, 0, (int)Math.Ceiling(Root.ActualWidth * scale), (int)(TitleBarHeight * scale))]);

        var passthrough = new List<RectInt32> { ToPixels(Toolbar, scale) };
        if (FindBar.Visibility == Visibility.Visible) passthrough.Add(ToPixels(FindBar, scale));
        source.SetRegionRects(NonClientRegionKind.Passthrough, passthrough.ToArray());
    }

    private RectInt32 ToPixels(FrameworkElement element, double scale)
    {
        var origin = element.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        return new RectInt32(
            (int)Math.Round(origin.X * scale), (int)Math.Round(origin.Y * scale),
            (int)Math.Round(element.ActualWidth * scale), (int)Math.Round(element.ActualHeight * scale));
    }

    // =====================================================================================
    // Page surface
    // =====================================================================================

    /// <summary>The page card in physical pixels, inside its hairline border.</summary>
    private RectInt32 BrowserRect()
    {
        if (Root.XamlRoot is null) return default;
        double scale = Root.XamlRoot.RasterizationScale;
        var r = ToPixels(BrowserHost, scale);
        int inset = _fullscreen ? 0 : (int)Math.Round(PageBorder * scale);
        return new RectInt32(r.X + inset, r.Y + inset, Math.Max(0, r.Width - 2 * inset), Math.Max(0, r.Height - 2 * inset));
    }

    private int CornerRadiusPx =>
        _fullscreen || Root.XamlRoot is null ? 0
        : (int)Math.Round((PageCornerRadius - PageBorder) * Root.XamlRoot.RasterizationScale);

    private void UpdateBrowserBounds()
    {
        if (_active is not { IsCreated: true }) return;
        var r = BrowserRect();
        CefHost.SetBounds(_active.Id, r.X, r.Y, r.Width, r.Height, CornerRadiusPx);
    }

    private void BrowserHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBrowserBounds();

    private void FocusPage()
    {
        if (_active is { IsCreated: true }) CefHost.Focus(_active.Id);
    }

    /// <summary>Moves Win32 keyboard focus back to the XAML island (it may sit in the page).</summary>
    private void FocusXaml(Control control)
    {
        var island = FindWindowEx(_hwnd, 0, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (island != 0) SetFocus(island);
        control.Focus(FocusState.Programmatic);
    }

    // =====================================================================================
    // Tabs
    // =====================================================================================

    private BrowserTab OpenTab(string url, bool activate, int index = 0)
    {
        var tab = new BrowserTab(url);
        _tabs.Insert(Math.Clamp(index, 0, _tabs.Count), tab);
        if (activate) ActivateTab(tab);
        else EnsureCreated(tab);
        return tab;
    }

    private void OpenNewTabAndFocusAddress()
    {
        OpenTab(Urls.NewTabPage, activate: true, index: 0);
        BeginAddressEdit(focus: true);
    }

    private void EnsureCreated(BrowserTab tab)
    {
        if (tab.IsCreated) return;
        var r = BrowserRect();
        tab.Id = CefHost.Create(_hwnd, tab.Url, r.X, r.Y, r.Width, r.Height, visible: false);
        _byId[tab.Id] = tab;
    }

    private void ActivateTab(BrowserTab tab, bool focusPage = true)
    {
        var previous = _active;
        if (previous != null) previous.PropertyChanged -= ActiveTab_PropertyChanged;
        _active = tab;
        tab.PropertyChanged += ActiveTab_PropertyChanged;
        EnsureCreated(tab);

        if (previous != tab)
        {
            if (previous != null) previous.IsSelected = false;
            tab.IsSelected = true;

            var r = BrowserRect();
            CefHost.SetBounds(tab.Id, r.X, r.Y, r.Width, r.Height, CornerRadiusPx);
            CefHost.SetVisible(tab.Id, true);
            if (previous is { IsCreated: true }) CefHost.SetVisible(previous.Id, false);

            CloseFindBar(refocusPage: false);
        }

        if (!ReferenceEquals(TabList.SelectedItem, tab))
        {
            _syncingSelection = true;
            TabList.SelectedItem = tab;
            _syncingSelection = false;
        }
        TabList.ScrollIntoView(tab);

        _editingAddress = false;
        UpdateChrome();
        if (focusPage && !tab.IsNewTabPage) FocusPage();
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        if (!tab.IsNewTabPage && !string.IsNullOrEmpty(tab.Url)) _closedUrls.Push(tab.Url);
        _tabs.RemoveAt(index);
        if (tab.IsCreated) CefHost.Close(tab.Id);

        if (tab == _active)
        {
            _active = null;
            if (_tabs.Count == 0) OpenNewTabAndFocusAddress();
            else ActivateTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
    }

    private void CycleTab(int delta)
    {
        if (_active is null || _tabs.Count < 2) return;
        int i = (_tabs.IndexOf(_active) + delta + _tabs.Count) % _tabs.Count;
        ActivateTab(_tabs[i]);
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (TabList.SelectedItem is BrowserTab tab)
        {
            if (tab != _active) ActivateTab(tab);
        }
        else if (_active != null && _tabs.Contains(_active))
        {
            // Drag-reordering removes and re-inserts the item; keep the active tab selected.
            _ui.TryEnqueue(() =>
            {
                if (_active != null && _tabs.Contains(_active) && TabList.SelectedItem is null)
                {
                    _syncingSelection = true;
                    TabList.SelectedItem = _active;
                    _syncingSelection = false;
                }
            });
        }
    }

    private void TabItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) tab.IsHovered = true;
    }

    private void TabItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) tab.IsHovered = false;
    }

    private void TabItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Middle-click closes, like every other browser.
        if (e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed &&
            (sender as FrameworkElement)?.DataContext is BrowserTab tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BrowserTab tab) CloseTab(tab);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => OpenNewTabAndFocusAddress();

    // =====================================================================================
    // Toolbar, address bar, find bar
    // =====================================================================================

    private void UpdateChrome()
    {
        var tab = _active;
        BackButton.IsEnabled = tab?.CanGoBack == true;
        ForwardButton.IsEnabled = tab?.CanGoForward == true;
        ReloadIcon.Symbol = tab?.IsLoading == true ? "x" : "rotate-ccw";
        ToolTipService.SetToolTip(ReloadButton, tab?.IsLoading == true ? "Stop (Esc)" : "Reload (Ctrl+R)");
        AppWindow.Title = tab is null ? "WinBrowser" : $"{tab.DisplayTitle} – WinBrowser";
        UpdateAddressDisplay();
    }

    private void UpdateAddressDisplay()
    {
        var url = _active?.Url;
        bool newTab = _active?.IsNewTabPage ?? true;
        // Site favicon for secure pages, a warning for plain http, a search glyph on the new-tab page.
        bool showFavicon = !newTab && Urls.IsSecure(url) && _active?.Favicon != null;
        AddressFavicon.Source = showFavicon ? _active!.Favicon : null;
        AddressFavicon.Visibility = showFavicon ? Visibility.Visible : Visibility.Collapsed;
        AddressIcon.Visibility = showFavicon ? Visibility.Collapsed : Visibility.Visible;
        AddressIcon.Symbol = newTab ? "search" : Urls.IsSecure(url) ? "globe" : "triangle-alert";
        CopyLinkButton.Visibility = newTab ? Visibility.Collapsed : Visibility.Visible;
        if (!_editingAddress) AddressBox.Text = Urls.DisplayHost(url);
    }

    private void BeginAddressEdit(bool focus)
    {
        if (!_sidebarVisible) SetSidebarVisible(true);
        _editingAddress = true;
        AddressBox.Text = _active is null || _active.IsNewTabPage ? string.Empty : _active.Url;
        if (focus) FocusXaml(AddressBox);
        // Select after the click that focused the box has positioned the caret.
        _ui.TryEnqueue(DispatcherQueuePriority.Low, () => AddressBox.SelectAll());
    }

    private void EndAddressEdit()
    {
        _editingAddress = false;
        UpdateAddressDisplay();
    }

    private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_editingAddress) BeginAddressEdit(focus: false);
    }

    private void AddressBox_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The box can still own XAML focus while the page has keyboard focus, in which
        // case GotFocus never fires again; start editing on click instead.
        if (!_editingAddress) BeginAddressEdit(focus: false);
    }

    private void AddressBox_LostFocus(object sender, RoutedEventArgs e) => EndAddressEdit();

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            var url = Urls.FromInput(AddressBox.Text);
            if (_active is null)
            {
                OpenTab(url, activate: true);
            }
            else
            {
                _active.Url = url;
                CefHost.Navigate(_active.Id, url);
            }
            EndAddressEdit();
            FocusPage();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            EndAddressEdit();
            FocusPage();
        }
    }

    /// <summary>The template's "×" clear button clutters the pill (and looks like close-tab).</summary>
    private void TextBox_HideClearButton(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject box) return;
        var queue = new Queue<DependencyObject>([box]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is Button { Name: "DeleteButton" } clear)
            {
                clear.Width = 0;
                clear.MinWidth = 0;
                clear.Margin = new Thickness(0);
                clear.IsTabStop = false;
                return;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
    }

    private async void CopyLink_Click(object sender, RoutedEventArgs e) => await CopyActiveUrl();

    private async Task CopyActiveUrl()
    {
        if (_active is null || _active.IsNewTabPage) return;
        var package = new DataPackage();
        package.SetText(_active.Url);
        Clipboard.SetContent(package);

        CopyLinkIcon.Symbol = "check";
        CopyLinkButton.Opacity = 1;
        await Task.Delay(1200);
        CopyLinkIcon.Symbol = "share";
        CopyLinkButton.Opacity = _pointerOverAddress ? 1 : 0;
    }

    private void ActiveTab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserTab.Favicon) && sender == _active) UpdateAddressDisplay();
    }

    private void AddressPill_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverAddress = true;
        CopyLinkButton.Opacity = 1;
        AddressPill.Background = new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF));
    }

    private void AddressPill_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverAddress = false;
        CopyLinkButton.Opacity = 0;
        AddressPill.Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{downloads}\"") { UseShellExecute = true });
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name && Enum.TryParse<BrowserCommand>(name, out var command))
            Execute(command);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Execute(BrowserCommand.Back);
    private void Forward_Click(object sender, RoutedEventArgs e) => Execute(BrowserCommand.Forward);

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { IsCreated: true }) return;
        if (_active.IsLoading) CefHost.Stop(_active.Id);
        else CefHost.Reload(_active.Id, ignoreCache: false);
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e) => SetSidebarVisible(!_sidebarVisible);

    private void SetSidebarVisible(bool visible)
    {
        _sidebarVisible = visible;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        bool showSidebar = _sidebarVisible && !_fullscreen;
        SidebarColumn.Width = new GridLength(showSidebar ? SidebarWidth : 0);
        Sidebar.Visibility = showSidebar ? Visibility.Visible : Visibility.Collapsed;
        TitleRow.Height = new GridLength(_fullscreen ? 0 : TitleBarHeight);
        Toolbar.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        BrowserHost.Margin = _fullscreen ? new Thickness(0) : new Thickness(showSidebar ? 0 : PageGap, 0, PageGap, PageGap);
        BrowserHost.BorderThickness = new Thickness(_fullscreen ? 0 : PageBorder);
        // With the sidebar open the nav buttons line up with its right edge (Arc-style).
        Toolbar.Width = showSidebar ? SidebarWidth - 18 : double.NaN;
        BrowserHost.CornerRadius = new CornerRadius(_fullscreen ? 0 : PageCornerRadius);

        Root.UpdateLayout();
        UpdateBrowserBounds();
        UpdateTitleBarRegions();
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen) return;
        _fullscreen = fullscreen;
        AppWindow.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        ApplyLayout();
    }

    private void ShowFindBar()
    {
        if (_active is not { IsCreated: true }) return;
        FindBar.Visibility = Visibility.Visible;
        Root.UpdateLayout();
        UpdateTitleBarRegions();
        FocusXaml(FindBox);
        FindBox.SelectAll();
        if (FindBox.Text.Length > 0) CefHost.Find(_active.Id, FindBox.Text, forward: true);
    }

    private void CloseFindBar(bool refocusPage)
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        FindBar.Visibility = Visibility.Collapsed;
        foreach (var tab in _tabs.Where(t => t.IsCreated)) CefHost.StopFind(tab.Id);
        UpdateTitleBarRegions();
        if (refocusPage) FocusPage();
    }

    private void FindNext(bool forward)
    {
        if (_active is { IsCreated: true } && FindBox.Text.Length > 0)
            CefHost.Find(_active.Id, FindBox.Text, forward);
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_active is not { IsCreated: true }) return;
        if (FindBox.Text.Length == 0) CefHost.StopFind(_active.Id);
        else CefHost.Find(_active.Id, FindBox.Text, forward: true);
    }

    private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            FindNext(forward: !IsDown(VirtualKey.Shift));
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseFindBar(refocusPage: true);
        }
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindNext(forward: false);
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(forward: true);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar(refocusPage: true);

    // =====================================================================================
    // Commands & keyboard
    // =====================================================================================

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var mods = KeyModifiers.None;
        if (IsDown(VirtualKey.Control)) mods |= KeyModifiers.Ctrl;
        if (IsDown(VirtualKey.Shift)) mods |= KeyModifiers.Shift;
        if (IsDown(VirtualKey.Menu)) mods |= KeyModifiers.Alt;

        if (Shortcuts.Map((int)e.Key, mods) is { } command)
        {
            e.Handled = true;
            Execute(command);
        }
    }

    private void Execute(BrowserCommand command)
    {
        var tab = _active;
        int id = tab is { IsCreated: true } ? tab.Id : 0;

        switch (command)
        {
            case BrowserCommand.NewTab: OpenNewTabAndFocusAddress(); break;
            case BrowserCommand.CloseTab: if (tab != null) CloseTab(tab); break;
            case BrowserCommand.ReopenClosedTab:
                if (_closedUrls.TryPop(out var url)) OpenTab(url, activate: true);
                break;
            case BrowserCommand.FocusAddress: BeginAddressEdit(focus: true); break;
            case BrowserCommand.ToggleSidebar: SetSidebarVisible(!_sidebarVisible); break;
            case BrowserCommand.NextTab: CycleTab(+1); break;
            case BrowserCommand.PreviousTab: CycleTab(-1); break;
            case BrowserCommand.Tab1 or BrowserCommand.Tab2 or BrowserCommand.Tab3 or BrowserCommand.Tab4 or
                 BrowserCommand.Tab5 or BrowserCommand.Tab6 or BrowserCommand.Tab7 or BrowserCommand.Tab8:
                int n = command - BrowserCommand.Tab1;
                if (n < _tabs.Count) ActivateTab(_tabs[n]);
                break;
            case BrowserCommand.LastTab: if (_tabs.Count > 0) ActivateTab(_tabs[^1]); break;
            case BrowserCommand.ToggleFullscreen: SetFullscreen(!_fullscreen); break;
            case BrowserCommand.CopyUrl: _ = CopyActiveUrl(); break;
            case BrowserCommand.Find: ShowFindBar(); break;
            case BrowserCommand.FindNext:
                if (FindBar.Visibility == Visibility.Visible) FindNext(forward: true); else ShowFindBar();
                break;
        }

        if (id == 0) return;
        switch (command)
        {
            case BrowserCommand.Reload: CefHost.Reload(id, ignoreCache: false); break;
            case BrowserCommand.HardReload: CefHost.Reload(id, ignoreCache: true); break;
            case BrowserCommand.Back: CefHost.GoBack(id); break;
            case BrowserCommand.Forward: CefHost.GoForward(id); break;
            case BrowserCommand.DevTools: CefHost.ShowDevTools(id); break;
            case BrowserCommand.Print: CefHost.Print(id); break;
            case BrowserCommand.ZoomIn: CefHost.Zoom(id, +1); break;
            case BrowserCommand.ZoomOut: CefHost.Zoom(id, -1); break;
            case BrowserCommand.ZoomReset: CefHost.Zoom(id, 0); break;
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Give keyboard focus back to the page when returning to the window.
        if (args.WindowActivationState != WindowActivationState.Deactivated && _pageFocused)
            _ui.TryEnqueue(FocusPage);
    }

    // =====================================================================================
    // Chromium events (arrive on CEF's UI thread → marshal to ours)
    // =====================================================================================

    private void OnUi(int id, Action<BrowserTab> action) =>
        _ui.TryEnqueue(() =>
        {
            if (_byId.TryGetValue(id, out var tab)) action(tab);
        });

    void ICefHostEvents.OnCreated(int id) => OnUi(id, tab =>
    {
        if (tab == _active) UpdateBrowserBounds();
    });

    void ICefHostEvents.OnTitle(int id, string title) => OnUi(id, tab =>
    {
        tab.Title = title;
        if (tab == _active) UpdateChrome();
    });

    void ICefHostEvents.OnAddress(int id, string url) => OnUi(id, tab =>
    {
        if (!string.Equals(Urls.DisplayHost(tab.Url), Urls.DisplayHost(url), StringComparison.OrdinalIgnoreCase))
            tab.SetFaviconUrl(null);
        tab.Url = url;
        if (tab == _active) UpdateChrome();
    });

    void ICefHostEvents.OnLoading(int id, bool isLoading, bool canGoBack, bool canGoForward) => OnUi(id, tab =>
    {
        tab.IsLoading = isLoading;
        tab.CanGoBack = canGoBack;
        tab.CanGoForward = canGoForward;
        if (tab == _active) UpdateChrome();
    });

    void ICefHostEvents.OnFavicon(int id, string url) => OnUi(id, tab => tab.SetFaviconUrl(url));

    void ICefHostEvents.OnNewTab(int id, string url, bool foreground) => OnUi(id, opener =>
    {
        if (_shuttingDown || string.IsNullOrEmpty(url)) return;
        OpenTab(url, activate: foreground, index: _tabs.IndexOf(opener) + 1);
    });

    void ICefHostEvents.OnClosed(int id) => _ui.TryEnqueue(() =>
    {
        _byId.Remove(id);
        if (_shuttingDown && CefHost.Count() == 0) FinishShutdown();
    });

    void ICefHostEvents.OnFullscreen(int id, bool fullscreen) => OnUi(id, tab =>
    {
        if (tab == _active) SetFullscreen(fullscreen);
    });

    bool ICefHostEvents.OnShortcut(int id, int virtualKey, KeyModifiers modifiers)
    {
        if (Shortcuts.Map(virtualKey, modifiers) is not { } command) return false;
        _ui.TryEnqueue(() => Execute(command));
        return true;
    }

    void ICefHostEvents.OnGotFocus(int id) => _ui.TryEnqueue(() =>
    {
        _pageFocused = true;
        if (_editingAddress) EndAddressEdit();
    });

    // =====================================================================================
    // Session & shutdown
    // =====================================================================================

    private void RestoreSession()
    {
        var session = Session.Load();
        if (session != null)
        {
            _sidebarVisible = session.SidebarVisible;
            ApplyLayout();
            foreach (var saved in session.Tabs.Where(t => !string.IsNullOrEmpty(t.Url)))
                _tabs.Add(new BrowserTab(saved.Url, saved.Title)); // created lazily on first activation
        }

        if (!string.IsNullOrEmpty(_startUrl))
            OpenTab(Urls.FromInput(_startUrl), activate: true);
        else if (_tabs.Count > 0)
            ActivateTab(_tabs[Math.Clamp(session!.ActiveIndex, 0, _tabs.Count - 1)]);
        else
            OpenNewTabAndFocusAddress();
    }

    private void SaveSession()
    {
        var session = new Session
        {
            SidebarVisible = _sidebarVisible,
            ActiveIndex = _active is null ? 0 : Math.Max(0, _tabs.Where(t => !t.IsNewTabPage).ToList().IndexOf(_active)),
            Tabs = _tabs.Where(t => !t.IsNewTabPage)
                        .Select(t => new Session.Tab { Url = t.Url, Title = t.Title })
                        .ToList(),
        };
        session.Save();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_readyToClose) return;
        args.Cancel = true;
        if (_shuttingDown) return;

        // Browsers must be closed (and CefShutdown called) before the process exits.
        _shuttingDown = true;
        SaveSession();
        AppWindow.Hide();

        if (CefHost.Count() == 0)
        {
            FinishShutdown();
            return;
        }
        CefHost.CloseAll();

        var watchdog = _ui.CreateTimer();
        watchdog.Interval = TimeSpan.FromSeconds(5);
        watchdog.IsRepeating = false;
        watchdog.Tick += (_, _) => FinishShutdown();
        watchdog.Start();
    }

    private void FinishShutdown()
    {
        if (_readyToClose) return;
        _readyToClose = true;

        if (CefHost.Count() == 0) CefHost.Shutdown();
        else Environment.Exit(0); // A browser refused to close; don't hang on exit.

        Close();
        Application.Current.Exit();
    }

    // =====================================================================================
    // Win32
    // =====================================================================================

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hwnd);
}
