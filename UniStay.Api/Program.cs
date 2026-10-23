using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UniStay.Api;
using UniStay.Application;
using UniStay.Core;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Migration API is local-only until production mail and deployment are configured.");
var connection = builder.Configuration.GetConnectionString("UniStay") ?? throw new InvalidOperationException("ConnectionStrings__UniStay required.");
var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connection);
if (parsed.Database is null || !parsed.Database.StartsWith("unistay_csharp", StringComparison.Ordinal))
    throw new InvalidOperationException("Use a separate unistay_csharp database. The existing Django database is forbidden.");
builder.Services.AddDbContext<UniStayDb>(o => o.UseNpgsql(connection));
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<HousingService>();
builder.Services.AddScoped<PhotoService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddScoped<IAccountMail, LocalAccountMail>();
builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, BearerAuthentication>("Bearer", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddJsonOptions(o => { o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; o.JsonSerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString; o.JsonSerializerOptions.Converters.Add(new OptionalConverterFactory()); o.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter()); });
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.Configure<ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = context =>
    new BadRequestObjectResult(new
    {
        error = new
        {
            status = 400,
            details = context.ModelState.Where(k => k.Value?.Errors.Count > 0)
        .ToDictionary(k => JsonNamingPolicy.SnakeCaseLower.ConvertName(k.Key), k => k.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Некорректное значение." : e.ErrorMessage).ToArray())
        }
    }));
builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer<ApiSecurityDocument>(); o.AddSchemaTransformer((schema, context, ct) =>
    {
        var type = context.JsonTypeInfo.Type;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>))
        {
            var inner = Nullable.GetUnderlyingType(type.GenericTypeArguments[0]) ?? type.GenericTypeArguments[0];
            schema.Properties = null; schema.Required = null;
            schema.Type = (inner == typeof(bool) ? Microsoft.OpenApi.JsonSchemaType.Boolean : inner == typeof(int) ? Microsoft.OpenApi.JsonSchemaType.Integer : inner == typeof(decimal) ? Microsoft.OpenApi.JsonSchemaType.Number | Microsoft.OpenApi.JsonSchemaType.String : inner == typeof(string[]) ? Microsoft.OpenApi.JsonSchemaType.Array : Microsoft.OpenApi.JsonSchemaType.String) | Microsoft.OpenApi.JsonSchemaType.Null;
            if (inner == typeof(DateOnly)) schema.Format = "date";
            if (inner == typeof(string[])) schema.Items = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.String };
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000", "http://localhost:4200", "http://127.0.0.1:4200").AllowAnyHeader().AllowAnyMethod()));
var relaxedTestLimits = builder.Configuration.GetValue<bool>("TestRelaxRateLimits") && (parsed.Database.StartsWith("unistay_csharp_test_") || parsed.Database.StartsWith("unistay_csharp_flutter_"));
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = relaxedTestLimits ? 1000 : 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("mail", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = relaxedTestLimits ? 1000 : 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = new { status = 429, details = "Слишком много запросов." } }, ct);
    };
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (ApiFault ex)
    {
        context.Response.StatusCode = ex.Status;
        await context.Response.WriteAsJsonAsync(new { error = new { status = ex.Status, details = ex.Details } });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        app.Logger.LogError("Unhandled request failure ({ExceptionType}).", ex.GetType().Name);
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = new { status = 500, details = "Ошибка сервера." } });
    }
});
var media = builder.Configuration["MediaDirectory"] ?? throw new InvalidOperationException("MediaDirectory required.");
Directory.CreateDirectory(media);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(media)), RequestPath = "/media" });
app.UseStatusCodePages(async status =>
{
    var response = status.HttpContext.Response;
    await response.WriteAsJsonAsync(new { error = new { status = response.StatusCode, details = "Запрос не выполнен." } });
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("Authorization") && context.User.Identity?.IsAuthenticated != true && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = new { status = 401, details = "Недействительная сессия." } });
        return;
    }
    await next(context);
});
app.Use(async (context, next) =>
{
    // Endpoint routing returns 405 without invoking controller authorization.
    var path = context.Request.Path.Value ?? "";
    var match = System.Text.RegularExpressions.Regex.Match(path, @"^/api/v1/admin/(me|users(?:/[+-]?[0-9]+(?:/profile)?)?|listings(?:/[+-]?[0-9]+)?|photos(?:/[+-]?[0-9]+)?|applications(?:/[+-]?[0-9]+)?|application-events(?:/[+-]?[0-9]+)?)/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (match.Success)
    {
        var actor = (context.Items["session"] as Session)?.Account ?? throw new ApiFault(401, "Требуется вход.");
        if (!actor.IsActive || !actor.IsStaff || !actor.EmailVerified) throw new ApiFault(403, "Требуются административные права.");
        var resource = match.Groups[1].Value.ToLowerInvariant();
        if (resource != "me")
        {
            var permission = resource.Contains("/profile") ? "profiles" : resource.Split('/')[0].Replace("application-events", "application_events");
            AdminService.Require(actor, permission + (context.Request.Method == "PATCH" && (permission == "users" || permission == "profiles") ? ".update" : ".read"));
        }
    }
    await next(context);
});
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi("/api/schema/");
app.UseSwaggerUI(o => { o.RoutePrefix = "api/docs"; o.SwaggerEndpoint("/api/schema/", "UniStay API"); });
app.MapGet("/health/", async (UniStayDb db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Json(new { status = "unavailable" }, statusCode: 503); }
    catch { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});
if (args.Contains("--cleanup-sessions"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<UniStayDb>();
    var now = DateTimeOffset.UtcNow;
    await db.Sessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync();
    await db.AccountTokens.Where(t => t.ExpiresAt <= now).ExecuteDeleteAsync();
    Console.WriteLine("Expired sessions and account links removed.");
    return;
}
if (args.Contains("--seed-demo"))
{
    if (parsed.Database != "unistay_csharp_demo" || !builder.Configuration.GetValue<bool>("DemoMode"))
        throw new InvalidOperationException("Seed requires DemoMode=true and the separate unistay_csharp_demo database.");
    using var scope = app.Services.CreateScope();
    await DemoSeed.RunAsync(scope.ServiceProvider.GetRequiredService<UniStayDb>(), scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>(),
        (await Console.In.ReadToEndAsync()).Trim(), media);
    Console.WriteLine("Synthetic demo reset completed.");
    return;
}
if (args.Contains("--create-admin"))
{
    using var scope = app.Services.CreateScope();
    var input = JsonSerializer.Deserialize<RegisterRequest>(await Console.In.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Admin input required on stdin.");
    // Positional DTO validation is performed manually for this non-HTTP entry point.
    if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(input.Email) || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || input.Email.Length > 254)
        throw new InvalidOperationException("Invalid administrator fields.");
    var db = scope.ServiceProvider.GetRequiredService<UniStayDb>();
    var email = input.Email.Trim().ToLowerInvariant();
    if (await db.Accounts.AnyAsync(a => a.Email == email)) throw new InvalidOperationException("Account already exists; no changes made.");
    var account = new Account { Email = email, Name = input.Name.Trim(), Role = "student", EmailVerified = true, IsStaff = true, IsSuperuser = true };
    AccountService.ValidatePassword(input.Password, account);
    account.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(account, input.Password);
    db.Accounts.Add(account); await db.SaveChangesAsync();
    Console.WriteLine("Administrator created.");
    return;
}
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<UniStayDb>().Database.MigrateAsync();
    return;
}
app.Run();
