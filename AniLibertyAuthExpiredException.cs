using System.Net;

namespace AniLibertyStrmPlugin;

public sealed class AniLibertyAuthExpiredException : Exception
{
    public AniLibertyAuthExpiredException(HttpStatusCode statusCode, string endpoint, string responseBody)
        : base($"AniLiberty authorization failed with HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string Endpoint { get; }
    public string ResponseBody { get; }

    public static bool IsAuthFailure(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
