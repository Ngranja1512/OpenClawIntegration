using CopilotDigest.Models;
using CopilotDigest.Services;
using CopilotDigest.Workers;

var runOnce = args.Contains("--run-once");
var listModels = args.Contains("--list-models");

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration
builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("CopilotDigest"));

// Register HTTP clients
builder.Services.AddHttpClient<ICopilotService, CopilotService>();

// Register other services
builder.Services.AddSingleton<IResearchService, ResearchService>();
builder.Services.AddSingleton<IEmailService, EmailService>();

if (runOnce)
{
    // One-shot mode: run the summary cycle once and exit.
    // Used by the GitHub Actions scheduled workflow.
    builder.Services.AddHostedService<OneShotWorker>();
}
else
{
    // Daemon mode: run on the configured cron schedule indefinitely.
    builder.Services.AddHostedService<SummaryWorker>();
}

var host = builder.Build();
if (listModels)
{
    // One-shot mode: list available models for the configured API endpoint and exit.
    var copilot = host.Services.GetRequiredService<ICopilotService>();
    var models = await copilot.GetAvailableModelsAsync();
    Console.WriteLine($"Available models at configured ApiUrl:");
    foreach (var id in models)
        Console.WriteLine($"  {id}");
    return;
}

host.Run();
