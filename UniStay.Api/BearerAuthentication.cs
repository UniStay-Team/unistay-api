using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using UniStay.Application;

namespace UniStay.Api;

public sealed class BearerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AccountService accounts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.Length == 0) return AuthenticateResult.NoResult();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.Fail("Invalid authorization.");
        var session = await accounts.AuthenticateAsync(header[7..], Context.RequestAborted);
        if (session is null) return AuthenticateResult.Fail("Invalid or expired session.");
        Context.Items["session"] = session;
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, session.AccountId.ToString()), new Claim(ClaimTypes.Role, session.Account.Role) }, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(new { error = new { status = 401, details = "Требуется вход." } });
    }
}
