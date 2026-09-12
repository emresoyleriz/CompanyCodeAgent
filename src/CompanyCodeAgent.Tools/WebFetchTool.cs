using System.Net;
using System.Net.Sockets;

namespace CompanyCodeAgent.Tools;

public sealed class WebFetchTool
{
    private const int MaxCharacters = 512 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var current)) throw new ArgumentException("Geçerli mutlak URL zorunludur.", nameof(url));
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            await EnsurePublicEndpointAsync(current, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("CompanyCodeAgent/0.1");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (response.Headers.Location == null) throw new InvalidOperationException("Web yönlendirme hedefi yok.");
                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Web aracı yalnızca metin tabanlı içerik alır.");
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var buffer = new char[8192];
            var content = new System.Text.StringBuilder();
            while (content.Length < MaxCharacters)
            {
                var read = await reader.ReadAsync(buffer, 0, Math.Min(buffer.Length, MaxCharacters - content.Length));
                if (read == 0) break;
                content.Append(buffer, 0, read);
            }
            return "URL: " + current + "\nDurum: " + (int)response.StatusCode + "\n\n" + content;
        }
        throw new InvalidOperationException("Web yönlendirme sınırı aşıldı.");
    }

    public static void EnsureSafeUri(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Web aracı yalnızca HTTPS URL kabul eder.");
        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Yerel ağ hedefleri engellendi.");
        if (IPAddress.TryParse(host, out var address) && IsPrivate(address)) throw new UnauthorizedAccessException("Özel IP hedefleri engellendi.");
    }

    public static async Task EnsurePublicEndpointAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        EnsureSafeUri(uri);
        await EnsureResolvesToPublicAddressAsync(uri, cancellationToken);
    }

    private static async Task EnsureResolvesToPublicAddressAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(uri.Host, out _)) return;

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new UnauthorizedAccessException("Web hedefinin DNS adresi güvenli biçimde doğrulanamadı.", exception);
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new UnauthorizedAccessException("Web hedefi özel veya yerel ağ adresine çözülüyor.");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);
    }
}
