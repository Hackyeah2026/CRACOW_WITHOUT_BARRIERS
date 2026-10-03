using Infrastructure;
using Infrastructure.Mongo;
using Infrastructure.Server;
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
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRouteEndpoints();
app.MapHealthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Web.Client._Imports).Assembly);

app.Run();
