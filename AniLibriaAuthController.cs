using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AniLibriaStrmPlugin;

/// <summary>
///  REST-контроллер для авторизации и OTP-входа через новый AniLibria API v1.
/// </summary>
[Route("AniLibriaAuth")]
public class AniLibriaAuthController : ControllerBase
{
    private const string ApiBase = "https://api.anilibria.app/api/v1";

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    // ─────────────────────────── 1. Логин/пароль ───────────────────────────

    [HttpPost("SignInLoginPass")]
    public async Task<object> SignInLoginPass([FromBody] LoginRequest req)
    {
        AppendLog($"SignInLoginPass called. login={req?.mail ?? "null"}");

        if (req == null || string.IsNullOrEmpty(req.mail) || string.IsNullOrEmpty(req.passwd))
            return new { success = false, error = "No login/pass" };

        var body = JsonSerializer.Serialize(new { login = req.mail, password = req.passwd });
        var resp = await PostJson(ApiBase + "/accounts/users/auth/login", body);

        if (!resp.ok)
            return new { success = false, error = resp.body };

        string? token = null;
        try
        {
            token = JsonDocument.Parse(resp.body).RootElement.GetProperty("token").GetString();
        }
        catch { /* ignore */ }

        if (string.IsNullOrEmpty(token))
            return new { success = false, error = "No token in response", serverResponse = resp.body };

        var cfg = Plugin.Instance.Configuration;
        cfg.AniLibriaToken = token;
        Plugin.Instance.UpdateConfiguration(cfg);

        return new { success = true, token, serverResponse = resp.body };
    }

    // ────────────────────────────── 2. OTP ────────────────────────────────

    [HttpPost("StartOtp")]
    public async Task<object> StartOtp()
    {
        var cfg = Plugin.Instance.Configuration;
        if (string.IsNullOrEmpty(cfg.AniDeviceId))
            cfg.AniDeviceId = Guid.NewGuid().ToString("N");
        Plugin.Instance.UpdateConfiguration(cfg);

        var body = JsonSerializer.Serialize(new { device_id = cfg.AniDeviceId });
        var resp = await PostJson(ApiBase + "/accounts/otp/get", body);

        if (!resp.ok)
            return new { success = false, error = resp.body };

        string? otp = null;
        try { otp = JsonDocument.Parse(resp.body).RootElement.GetProperty("otp").GetString(); }
        catch { /* ignore */ }

        if (string.IsNullOrEmpty(otp))
            return new { success = false, error = "No otp in response", serverResponse = resp.body };

        cfg.CurrentOtpCode = otp;
        Plugin.Instance.UpdateConfiguration(cfg);

        return new { success = true, otp, serverResponse = resp.body };
    }

    [HttpPost("AcceptOtp")]
    public async Task<object> AcceptOtp([FromBody] OtpRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.code))
            return new { success = false, error = "No code" };

        var body = JsonSerializer.Serialize(new { code = req.code });
        var resp = await PostJson(ApiBase + "/accounts/otp/accept", body,
            bearer: Plugin.Instance.Configuration.AniLibriaToken);

        return resp.ok
            ? new { success = true, serverResponse = resp.body }
            : new { success = false, error = resp.body };
    }

    [HttpPost("SignInOtp")]
    public async Task<object> SignInOtp([FromBody] OtpRequest req)
    {
        var cfg = Plugin.Instance.Configuration;
        if (req == null || string.IsNullOrEmpty(req.code))
            return new { success = false, error = "No code" };
        if (string.IsNullOrEmpty(cfg.AniDeviceId))
            return new { success = false, error = "No deviceId" };

        var body = JsonSerializer.Serialize(new { code = req.code, device_id = cfg.AniDeviceId });
        var resp = await PostJson(ApiBase + "/accounts/otp/login", body);

        if (!resp.ok)
            return new { success = false, error = resp.body, serverResponse = resp.body };

        string? token = null;
        try { token = JsonDocument.Parse(resp.body).RootElement.GetProperty("token").GetString(); }
        catch { /* ignore */ }

        if (string.IsNullOrEmpty(token))
            return new { success = false, error = "No token in response", serverResponse = resp.body };

        cfg.AniLibriaToken = token;
        Plugin.Instance.UpdateConfiguration(cfg);

        return new { success = true, token, serverResponse = resp.body };
    }

    // ──────────────────────────── helpers ─────────────────────────────

    private static async Task<(bool ok, string body)> PostJson(string url, string json, string? bearer = null)
    {
        try
        {
            using var client = new HttpClient();
            if (!string.IsNullOrEmpty(bearer))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearer);

            var resp = await client.PostAsync(url,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static void AppendLog(string msg)
    {
        var timestamped = $"{DateTime.Now:HH:mm:ss} {msg}";
        Console.WriteLine("[AniLibriaAuth] " + timestamped);
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
