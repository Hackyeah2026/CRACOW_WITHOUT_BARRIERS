using Application;
using Blazor.DriverJs;
using Infrastructure;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddApplication();
builder.Services.AddBrowserInfrastructure();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<BrowserLocation>();
builder.Services.RegisterDriverJs();

await builder.Build().RunAsync();
