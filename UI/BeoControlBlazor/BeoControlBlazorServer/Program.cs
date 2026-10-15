using BeoControlBlazor.Components;

using BeoControlBlazorServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register DeviceService as a normal singleton; this host drives its lifecycle explicitly.
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddScoped<BlazorSpotifyService>();
builder.Services.AddScoped<ISpotifyService>(sp => sp.GetRequiredService<BlazorSpotifyService>());
builder.Services.AddSingleton<IAutostartRegistrationService, UnsupportedAutostartRegistrationService>();

// HTTP API: enums as text (e.g. "defaultSource": "PC")
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

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

// Small HTTP API for local automation (e.g. a menu bar app switching the PC2 to the local input
// during a call). Same access as the web UI: no authentication.
object Snapshot(DeviceService ds) => new
{
    connected = ds.IsConnected,
    device = ds.CurrentDevice?.Type.ToString(),
    pc2 = ds.Pc2Routing,
    source = ds.LastSource is { } src ? new { command = src.Command, index = src.Index } : null,
    audio = ds.Pc2Routing is not null ? ds.CurrentPc2AudioSetup : null,
};

app.MapGet("/api/status", (DeviceService ds) => Results.Ok(Snapshot(ds))).DisableAntiforgery();

// Server-sent events: the full status (as /api/status) whenever it changes. Wakes on device
// status messages and also checks every second, since not every change raises one (e.g. a
// standby key from Masterlink only switches the mixer).
app.MapGet("/api/events", async (HttpContext ctx, DeviceService ds, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    var jsonOptions = ctx.RequestServices
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    var wake = System.Threading.Channels.Channel.CreateBounded<bool>(1);
    void OnChanged(BeoControl.Interfaces.StatusMessage _) => wake.Writer.TryWrite(true);
    ds.OnStatusChanged += OnChanged;
    try
    {
        string? last = null;
        var lastWrite = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Snapshot(ds), jsonOptions);
            if (json != last)
            {
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                last = json;
                lastWrite = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastWrite > TimeSpan.FromSeconds(15))
            {
                await ctx.Response.WriteAsync(": ping\n\n", ct);   // keeps proxies and clients from timing out
                lastWrite = DateTime.UtcNow;
            }
            await ctx.Response.Body.FlushAsync(ct);
            using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tick.CancelAfter(TimeSpan.FromSeconds(1));
            try { await wake.Reader.ReadAsync(tick.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        ds.OnStatusChanged -= OnChanged;
    }
}).DisableAntiforgery();

app.MapPost("/api/command/{cmd}", (string cmd, string? arg, DeviceService ds) =>
{
    if (!ds.IsConnected)
        return Results.Problem("No device connected", statusCode: 503);
    ds.SendCommand(cmd, arg);
    return Results.Ok(new { ok = true, pc2 = ds.Pc2Routing });
}).DisableAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapFallbackToFile("index.html"); // if Blazor WASM or Blazor Web App
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.Run();
