using System.Net;
using System.Net.Mail;
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
		// Definitely, hardcoding a token isn't a good idea.
		// This example uses it for simplicity, but in real-world scenarios
		// you should consider more secure approaches for storing secrets.
				
		// Environment variables can be an option, as well as other solutions:
		// https://learn.microsoft.com/aspnet/core/security/app-secrets
		// or https://learn.microsoft.com/aspnet/core/security/key-vault-configuration
		options.ApiToken = Environment.GetEnvironmentVariable("MailTrapAllrounderToken")
			?? throw new InvalidOperationException("MailTrap API token is not set in environment variables.");
});

builder.Services.AddSingleton<SmtpClient>(sp =>
{
    var host = Environment.GetEnvironmentVariable("MailHost");
    var user = Environment.GetEnvironmentVariable("MailUser");
    var password = Environment.GetEnvironmentVariable("MailPassword");

	if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
	{
		throw new InvalidOperationException("SMTP configuration is missing in environment variables.");
	}
	
	var client = new SmtpClient(host, 25)
	{
		Credentials = new NetworkCredential(user, password),
		EnableSsl = true
	};
		
  return client;
});

builder.Build().Run();
