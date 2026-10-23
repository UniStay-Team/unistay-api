using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UniStay.Application;
using UniStay.Core;

namespace UniStay.Api;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AccountsController(AccountService service) : ControllerBase
{
    [HttpPost("register/")]
    public async Task<ActionResult<AccountResponse>> Register(RegisterRequest input, CancellationToken ct) => StatusCode(201, await service.RegisterAsync(input, ct));
    [HttpPost("login/")]
    public async Task<LoginResponse> Login(LoginRequest input, CancellationToken ct) => await service.LoginAsync(input, ct);
    [Authorize, HttpPost("logout/")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await service.LogoutAsync(((Session)HttpContext.Items["session"]!).Id, ct);
        return NoContent();
    }
    [HttpPost("verify-email/")]
    public async Task<MessageResponse> Verify(VerifyRequest input, CancellationToken ct)
    { await service.VerifyAsync(input.Token, ct); return new("Email подтверждён."); }
    [HttpPost("resend-verification/"), EnableRateLimiting("mail")]
    public async Task<MessageResponse> Resend(EmailRequest input, CancellationToken ct)
    { await service.RequestMailAsync(input.Email, "verify", ct); return GenericMailResponse; }
    [HttpPost("password-reset/"), EnableRateLimiting("mail")]
    public async Task<MessageResponse> RequestReset(EmailRequest input, CancellationToken ct)
    { await service.RequestMailAsync(input.Email, "reset", ct); return GenericMailResponse; }
    [HttpPost("password-reset/confirm/")]
    public async Task<MessageResponse> Reset(ResetRequest input, CancellationToken ct)
    { await service.ResetAsync(input, ct); return new("Пароль изменён. Войдите заново."); }
    private static MessageResponse GenericMailResponse => new("Если аккаунт существует и действие требуется, письмо будет отправлено.");
}
[ApiController, Authorize, Route("api/v1/me")]
public sealed class MeController(AccountService service) : ControllerBase
{
    private Session Session => (Session)HttpContext.Items["session"]!;
    [HttpGet("")]
    public AccountResponse Me() => AccountResponse.From(Session.Account);
    [HttpGet("profile/")]
    public ProfileResponse Profile() => ProfileResponse.From(Session.Account.Profile);
    [HttpPatch("profile/")]
    public async Task<ProfileResponse> Patch(ProfilePatch patch, CancellationToken ct) => await service.PatchProfileAsync(Session.AccountId, patch, ct);
}
