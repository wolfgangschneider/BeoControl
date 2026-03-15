using BeoControl.Components;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RazorConsole.Core;

Console.CursorVisible = false;
Console.WriteLine("  BeoRemote — starting up...");
Console.WriteLine("  Initialising host...");

var builder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<App>(configure: config =>
    {
        config.ConfigureServices(s =>
            s.Configure<ConsoleAppOptions>(opt =>
                opt.EnableTerminalResizing = true
            )
        );
    });

Console.WriteLine("  Building services...");
var host = builder.Build();

Console.WriteLine("  Launching UI...");
await host.RunAsync();

Console.ReadLine();