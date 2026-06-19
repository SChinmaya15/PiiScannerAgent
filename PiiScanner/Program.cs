using PiiScanner;
using System.Net.Http;

var builder = Host.CreateApplicationBuilder(args);



// Register HttpClient as a singleton to be injected into Worker
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton<Scan>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
