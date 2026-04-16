using BeoControlBlazor.Components;

using BeoControlBlazorServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register DeviceService as a normal singleton; this host drives its lifecycle explicitly.
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddScoped<ILaunchSpotifyService, LaunchSpotifyService>();
builder.Services.AddSingleton<IAutostartRegistrationService, UnsupportedAutostartRegistrationService>();

var app = builder.Build();
var deviceService = app.Services.GetRequiredService<DeviceService>();
await deviceService.AutoConnectAsync();
app.Lifetime.ApplicationStopping.Register(() => deviceService.Disconnect(silent: true));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}



//app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapFallbackToFile("index.html"); // if Blazor WASM or Blazor Web App
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.Run();
