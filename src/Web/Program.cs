using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Infrastructure;
using Infrastructure.Mongo;
using Infrastructure.Mongo.Reports;
using Infrastructure.Server;
using Microsoft.AspNetCore.Authentication.Cookies;
using Web.Components;
using Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddServerInfrastructure(
    builder.Configuration.GetSection(OpenRouteServiceOptions.Section).Get<OpenRouteServiceOptions>() ?? new());

// Baza działa tylko na hoście: adres połączenia z hasłem nie może trafić do przeglądarki.
builder.Services.AddMongo(
    builder.Configuration.GetSection(MongoOptions.Section).Get<MongoOptions>() ?? new());
builder.Services.AddMongoReports(
    builder.Configuration.GetSection(OfficialsOptions.Section).Get<OfficialsOptions>() ?? new());

// Enumy jako tekst, tak jak w DomainJson po stronie przeglądarki.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Sesja urzędnika: ciasteczko HttpOnly niedostępne dla skryptów; SameSite=Strict chroni przed żądaniami z innych stron.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "kbb.official";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // API odpowiada kodem, zamiast przekierowywać na stronę logowania.
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    })
    // Sesja mieszkańca: osobne ciasteczko, żeby nie mieszała się z sesją urzędnika w tej samej przeglądarce.
    .AddCookie(AccountEndpoints.UserScheme, options =>
    {
        options.Cookie.Name = "kbb.user";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ReportEndpoints.OfficialPolicy, policy => policy.RequireRole(ReportEndpoints.OfficialPolicy))
    .AddPolicy(AccountEndpoints.UserPolicy, policy => policy.AddAuthenticationSchemes(AccountEndpoints.UserScheme).RequireAuthenticatedUser());

// Limity per adres IP: zgłoszenia, logowanie (przeciw zgadywaniu haseł) i zakładanie kont (przeciw masowej rejestracji).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ReportEndpoints.SubmitLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10) }));
    options.AddPolicy(ReportEndpoints.LoginLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(AccountEndpoints.RegisterLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Strona "nie znaleziono" tylko dla stron aplikacji: API ma zwracać sam kod (np. 401), a nie przepisywać żądanie na stronę.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRouteEndpoints();
app.MapHealthEndpoints();
app.MapReportEndpoints();
app.MapHazardEndpoints();
app.MapAccountEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Web.Client._Imports).Assembly);

app.Run();
