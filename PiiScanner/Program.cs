using PiiScanner;
using System.Net.Http;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Makes the host behave like a proper Windows Service (SCM start/stop, Event Log).
// This is a no-op when running as a console app, so `dotnet run` still works normally.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PiiScannerAgent";
});

// Register HttpClient as a singleton to be injected into Worker
builder.Services.AddSingleton(sp =>
{
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };

    return new HttpClient(handler);
});

builder.Services.AddSingleton<Scan>();
builder.Services.AddHostedService<Worker>();

// Configure logging: console + file logger writing to install folder with "dd_MM.txt" names.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new FileLoggerProvider(new FileLoggerOptions
{
    Directory = AppContext.BaseDirectory,       // installation folder after MSI install
    FileNameFormat = "dd_MM'.txt'"             // files like 05_06.txt
}));

var host = builder.Build();
host.Run();
