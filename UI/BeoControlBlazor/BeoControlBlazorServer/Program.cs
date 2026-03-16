using BeoControlBlazor.Components;

using BeoControlBlazorServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register DeviceService as singleton and as a hosted service for auto-connect on startup.
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceService>());

var app = builder.Build();

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
