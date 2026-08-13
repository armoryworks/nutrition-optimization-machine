using System.Net;
using System.Net.Sockets;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// SSRF protection for outbound requests to user-supplied URLs (webhooks).
    /// Two layers: <see cref="TryValidateUrl"/> gives fast feedback at write time,
    /// and <see cref="BuildGuardedHandler"/> re-checks the *resolved IP* at
    /// connect time and connects to that exact address — closing the
    /// DNS-rebinding window a write-time check alone would leave open.
    /// </summary>
    public static class SsrfGuard
    {
        /// <summary>True if the address is loopback, link-local, private, CGNAT, ULA, or otherwise non-public.</summary>
        public static bool IsBlocked(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

            if (IPAddress.IsLoopback(ip)) return true;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 0                                   // 0.0.0.0/8 "this network"
                    || b[0] == 10                                  // 10/8 private
                    || b[0] == 127                                 // 127/8 loopback
                    || (b[0] == 169 && b[1] == 254)                // 169.254/16 link-local (incl. cloud metadata)
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16/12 private
                    || (b[0] == 192 && b[1] == 168)                // 192.168/16 private
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64/10 CGNAT
                    || b[0] >= 224;                                // 224/4 multicast, 240/4 reserved, 255.255.255.255
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
                var b = ip.GetAddressBytes();
                return (b[0] & 0xFE) == 0xFC;                      // fc00::/7 unique-local
            }

            return true; // unknown family — fail closed
        }

        /// <summary>Fast structural check for write-time validation: absolute http(s) URL with a host that isn't a blocked literal IP.</summary>
        public static bool TryValidateUrl(string? url, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                error = "Webhook URL must be an absolute URL.";
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                error = "Webhook URL must use http or https.";
                return false;
            }
            if (IPAddress.TryParse(uri.Host, out var literal) && IsBlocked(literal))
            {
                error = "Webhook URL may not point at an internal or reserved address.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// An <see cref="SocketsHttpHandler"/> that resolves the destination host,
        /// rejects any non-public resolved address, and connects to a validated IP
        /// directly. Redirects are disabled (a redirect could bounce to an internal
        /// host) and a short timeout bounds the request.
        /// </summary>
        public static SocketsHttpHandler BuildGuardedHandler()
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var host = context.DnsEndPoint.Host;
                    IPAddress[] resolved;
                    if (IPAddress.TryParse(host, out var literal))
                    {
                        resolved = new[] { literal };
                    }
                    else
                    {
                        resolved = await Dns.GetHostAddressesAsync(host, cancellationToken);
                    }

                    var allowed = resolved.Where(a => !IsBlocked(a)).ToArray();
                    if (allowed.Length == 0)
                    {
                        throw new HttpRequestException("Blocked by SSRF guard: destination resolves to a non-public address.");
                    }

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
        }
    }
}
