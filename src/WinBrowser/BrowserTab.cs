using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WinBrowser;

/// <summary>One vertical tab, backed by one native CEF browser (identified by <see cref="Id"/>).</summary>
public sealed class BrowserTab : INotifyPropertyChanged
{
    public BrowserTab(string url, string title = "")
    {
        _url = url;
        _title = title;
    }

    /// <summary>Native browser id; 0 until the browser is created (restored tabs load lazily).</summary>
    public int Id { get; set; }

    public bool IsCreated => Id != 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _title;
    public string Title
    {
        get => _title;
        set { if (Set(ref _title, value)) Raise(nameof(DisplayTitle)); }
    }

    private string _url;
    public string Url
    {
        get => _url;
        set { if (Set(ref _url, value)) Raise(nameof(DisplayTitle)); }
    }

    public bool IsNewTabPage => Urls.IsNewTabPage(Url);

    public string DisplayTitle =>
        IsNewTabPage ? "New Tab"
        : !string.IsNullOrWhiteSpace(Title) ? Title
        : Urls.DisplayHost(Url) is { Length: > 0 } host ? host
        : "Loading…";

    private string? _faviconUrl;
    private ImageSource? _favicon;
    public ImageSource? Favicon
    {
        get => _favicon;
        private set
        {
            if (Set(ref _favicon, value))
            {
                Raise(nameof(FaviconVisibility));
                Raise(nameof(GlyphVisibility));
            }
        }
    }

    /// <summary>Must be called on the UI thread.</summary>
    public void SetFaviconUrl(string? url)
    {
        if (url == _faviconUrl) return;
        _faviconUrl = url;
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Favicon = null;
            return;
        }

        if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = new SvgImageSource(uri) { RasterizePixelWidth = 32, RasterizePixelHeight = 32 };
            svg.OpenFailed += (_, _) => { if (_faviconUrl == url) Favicon = null; };
            Favicon = svg;
        }
        else
        {
            var bmp = new BitmapImage { DecodePixelWidth = 32, DecodePixelType = DecodePixelType.Logical };
            bmp.ImageFailed += (_, _) => { if (_faviconUrl == url) Favicon = null; };
            bmp.UriSource = uri;
            Favicon = bmp;
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (Set(ref _isLoading, value))
            {
                Raise(nameof(FaviconVisibility));
                Raise(nameof(GlyphVisibility));
                Raise(nameof(SpinnerVisibility));
            }
        }
    }

    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) Raise(nameof(CloseButtonOpacity)); }
    }

    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered;
        set { if (Set(ref _isHovered, value)) Raise(nameof(CloseButtonOpacity)); }
    }

    public Visibility SpinnerVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FaviconVisibility => !IsLoading && Favicon != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => !IsLoading && Favicon == null ? Visibility.Visible : Visibility.Collapsed;
    public double CloseButtonOpacity => IsSelected || IsHovered ? 1 : 0;

    // Read by UI Automation for the tab list item.
    public override string ToString() => DisplayTitle;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
