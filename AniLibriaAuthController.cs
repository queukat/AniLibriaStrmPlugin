using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AniLibriaStrmPlugin;

[Route("AniLibriaAuth")]
public class AniLibriaAuthController : ControllerBase
{
    private static readonly string OldPublic = "https://www.anilibria.tv/public/";
    private static readonly string ApiUrl = OldPublic + "api/index.php";
    private static readonly string LoginPhp = OldPublic + "login.php";

    /// <summary>
    ///     (A)   mail+passwd ( sessionId  -).
    /// </summary>
    [HttpPost("SignInLoginPass")]
    public async Task<object> SignInLoginPass([FromBody] LoginRequest req)
    {
        AppendLog($"SignInLoginPass called. mail={req?.mail ?? "null"}");
        if (req == null || string.IsNullOrEmpty(req.mail) || string.IsNullOrEmpty(req.passwd))
            return new { success = false, error = "No mail/passwd" };

        var dict = new Dictionary<string, string>
        {
            ["mail"] = req.mail!,
            ["passwd"] = req.passwd!
        };

        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler);

        var content = new FormUrlEncodedContent(dict);
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(LoginPhp, content);
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }

        var body = await response.Content.ReadAsStringAsync();
        AppendLog($"Got response. Status={response.StatusCode}, Body[0..1000]={body.Substring(0, Math.Min(body.Length, 1000))}");

        //   sessionId
        string? sessionId = null;
        try
        {
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("sessionId", out var sidEl))
                sessionId = sidEl.GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AniLibriaAuthController] JSON parse error: " + ex.Message);
        }

        //    sessionId  JSON,   
        if (string.IsNullOrEmpty(sessionId))
        {
            var cookies = cookieContainer.GetCookies(new Uri(LoginPhp));
            foreach (Cookie ck in cookies)
                if (ck.Name == "PHPSESSID")
                    sessionId = ck.Value;
        }

        AppendLog($"sessionId parsed: {sessionId ?? "null"}");

        if (string.IsNullOrEmpty(sessionId))
            return new { success = false, error = "Cannot find sessionId", serverResponse = body };

        var config = Plugin.Instance.Configuration;
        config.AniLibriaSession = sessionId!;
        Plugin.Instance.UpdateConfiguration(config);

        return new { success = true, sessionId = sessionId!, serverResponse = body };
    }

    [HttpPost("StartOtp")]
    public async Task<object> StartOtp()
    {
        var config = Plugin.Instance.Configuration;
        if (string.IsNullOrEmpty(config.AniDeviceId))
            config.AniDeviceId = Guid.NewGuid().ToString("N");
        Plugin.Instance.UpdateConfiguration(config);

        var dict = new Dictionary<string, string>
        {
            ["query"] = "auth_get_otp",
            ["deviceId"] = config.AniDeviceId
        };

        var (respOk, respBody) = await PostForm(ApiUrl, dict);
        Console.WriteLine("[AniLibriaAuthController] StartOtp - " + respBody);

        if (!respOk)
            return new { success = false, error = respBody };

        string? otpCode = null;
        try
        {
            var doc = JsonDocument.Parse(respBody);
            otpCode = doc.RootElement.GetProperty("otp").GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AniLibriaAuthController] StartOtp parse error: " + ex.Message);
        }

        if (string.IsNullOrEmpty(otpCode))
            return new { success = false, error = "No otp in response", serverResponse = respBody };

        config.CurrentOtpCode = otpCode!;
        Plugin.Instance.UpdateConfiguration(config);

        return new { success = true, otp = otpCode!, serverResponse = respBody };
    }

    [HttpPost("AcceptOtp")]
    public async Task<object> AcceptOtp([FromBody] OtpRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.code))
            return new { success = false, error = "No code" };

        var dict = new Dictionary<string, string>
        {
            ["query"] = "auth_accept_otp",
            ["code"] = req.code!
        };

        var (respOk, respBody) = await PostForm(ApiUrl, dict);
        Console.WriteLine("[AniLibriaAuthController] AcceptOtp - " + respBody);

        if (!respOk)
            return new { success = false, error = respBody };

        return new { success = true, message = "Accepted", serverResponse = respBody };
    }

    [HttpPost("SignInOtp")]
    public async Task<object> SignInOtp([FromBody] OtpRequest req)
    {
        var config = Plugin.Instance.Configuration;
        if (req == null || string.IsNullOrEmpty(req.code))
            return new { success = false, error = "No code" };
        if (string.IsNullOrEmpty(config.AniDeviceId))
            return new { success = false, error = "No deviceId in config" };

        var dict = new Dictionary<string, string>
        {
            ["query"] = "auth_login_otp",
            ["deviceId"] = config.AniDeviceId,
            ["code"] = req.code!
        };

        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler);

        var content = new FormUrlEncodedContent(dict);
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(ApiUrl, content);
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }

        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine("[AniLibriaAuthController] SignInOtp - Status: " + response.StatusCode);
        Console.WriteLine("[AniLibriaAuthController] SignInOtp - Body: " + body);

        if (!response.IsSuccessStatusCode)
            return new { success = false, error = body, serverResponse = body };

        string? sessionId = null;
        var cookies = cookieContainer.GetCookies(new Uri(ApiUrl));
        foreach (Cookie ck in cookies)
            if (ck.Name == "PHPSESSID")
                sessionId = ck.Value;

        if (string.IsNullOrEmpty(sessionId))
            return new { success = false, error = "Cannot find PHPSESSID", serverResponse = body };

        config.AniLibriaSession = sessionId!;
        Plugin.Instance.UpdateConfiguration(config);

        return new { success = true, sessionId = sessionId!, serverResponse = body };
    }

    private static async Task<(bool ok, string body)> PostForm(string url, Dictionary<string, string> data)
    {
        try
        {
            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(data);
            var resp = await client.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, body);
            return (true, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void AppendLog(string msg)
    {
        var timestamped = $"{DateTime.Now:HH:mm:ss} {msg}";
        Console.WriteLine("[AniLibriaFavoritesTask] " + timestamped);
        Plugin.Instance.AppendTaskLog(timestamped);
    }
}

public class OtpRequest
{
    public string? code { get; set; }
}

public class LoginRequest
{
    public string? mail { get; set; }
    public string? passwd { get; set; }
}
