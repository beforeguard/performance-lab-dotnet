using System.Net;

namespace PerformanceLab.LoadTests.Http;

public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler);
    }
}