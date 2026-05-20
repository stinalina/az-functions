using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mailtrap;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddMailtrapClient(options =>
{
	options.ApiToken = Environment.GetEnvironmentVariable("MailTrapAllrounderToken")
		?? throw new InvalidOperationException("MailTrap API token is not set in environment variables.");
});

builder.Build().Run();
