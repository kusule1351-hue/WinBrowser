namespace WinBrowser;

internal static class Urls
{
    public const string SearchTemplate = "https://www.google.com/search?q={0}";

    public static string NewTabPage { get; } =
        new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "newtab.html")).AbsoluteUri;

    public static bool IsNewTabPage(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith(NewTabPage, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns address-bar input into a URL: keeps real URLs, completes bare domains, searches the rest.</summary>
    public static string FromInput(string input)
    {
        var text = input.Trim();
        if (text.Length == 0) return NewTabPage;

        if (text.Contains("://") ||
            text.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (!text.Contains(' '))
        {
            var hostPart = text.Split('/', '?', '#')[0];
            var hostOnly = hostPart.Split(':')[0];
            if (hostOnly.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                System.Net.IPAddress.TryParse(hostOnly, out _))
            {
                return "http://" + text;
            }
            if (hostOnly.Contains('.') && !hostOnly.StartsWith('.') && !hostOnly.EndsWith('.') &&
                Uri.CheckHostName(hostOnly) == UriHostNameType.Dns)
            {
                return "https://" + text;
            }
        }

        return string.Format(SearchTemplate, Uri.EscapeDataString(text));
    }

    /// <summary>The compact form shown in the address pill, e.g. "figma.com".</summary>
    public static string DisplayHost(string? url)
    {
        if (string.IsNullOrEmpty(url) || IsNewTabPage(url)) return string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (uri.Scheme is "http" or "https")
        {
            var host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }
        if (uri.IsFile) return Path.GetFileName(uri.LocalPath);
        return url;
    }

    public static bool IsSecure(string? url) =>
        url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
