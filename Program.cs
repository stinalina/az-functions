using System.Net;
using System.Net.Mail;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

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
