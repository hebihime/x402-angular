using System.Net;

namespace Ordering.Mcp;

/// <summary>
/// Paying seam: on HTTP 402, ask <see cref="IPaymentHeaderProvider"/> for an
/// X-PAYMENT and retry once. Used only for confirm. Tests and the fake-payer
/// env var inject a header the fake facilitator accepts; a real x402 signer
/// would implement the same interface.
///
/// Construct without an inner handler when passing to
/// <c>WebApplicationFactory.CreateDefaultClient</c> — the factory supplies it.
/// Pass an inner handler (typically <see cref="HttpClientHandler"/>) when
/// building a standalone client.
/// </summary>
public sealed class ChallengeRetryHandler : DelegatingHandler
{
    private readonly IPaymentHeaderProvider _headers;

    public ChallengeRetryHandler(IPaymentHeaderProvider headers)
    {
        _headers = headers;
    }

    public ChallengeRetryHandler(IPaymentHeaderProvider headers, HttpMessageHandler inner)
        : base(inner)
    {
        _headers = headers;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[]? body = null;
        MediaTypeHeaderValues? contentHeaders = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentHeaders = MediaTypeHeaderValues.Capture(request.Content);
        }

        var first = await base.SendAsync(Clone(request, body, contentHeaders), cancellationToken);
        if (first.StatusCode != HttpStatusCode.PaymentRequired)
        {
            return first;
        }

        var header = _headers.CreateHeader(request, first);
        if (header is null)
        {
            return first;
        }

        first.Dispose();
        var retry = Clone(request, body, contentHeaders);
        retry.Headers.Remove("X-PAYMENT");
        retry.Headers.TryAddWithoutValidation("X-PAYMENT", header);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage Clone(
        HttpRequestMessage request,
        byte[]? body,
        MediaTypeHeaderValues? contentHeaders)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            contentHeaders?.CopyTo(clone.Content);
        }

        return clone;
    }

    private sealed class MediaTypeHeaderValues
    {
        private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers = [];

        public static MediaTypeHeaderValues Capture(HttpContent content)
        {
            var captured = new MediaTypeHeaderValues();
            foreach (var header in content.Headers)
            {
                captured._headers.Add(header);
            }

            return captured;
        }

        public void CopyTo(HttpContent content)
        {
            foreach (var header in _headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }
}
