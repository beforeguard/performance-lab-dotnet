using System.Net;
using System.Net.Http.Headers;

namespace PerformanceLab.LoadTests.Http;

public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli
        };

        var client = new HttpClient(handler);
        
        // Request compression (both Gzip and Brotli)
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        
        return client;
    }
}