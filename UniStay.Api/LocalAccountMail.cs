using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using UniStay.Application;

namespace UniStay.Api;
// Local-only delivery. No token is exposed through HTTP or application logs.
public sealed class LocalAccountMail(IConfiguration config) : IAccountMail
{
    public async Task DeliverAsync(string email, string kind, string token, long accountId, CancellationToken ct)
    {
        var folder = config["LocalMailDirectory"] ?? throw new InvalidOperationException("LocalMailDirectory required.");
        Directory.CreateDirectory(folder);
        var uid = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(accountId.ToString()));
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { email, kind, token, uid }), ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
