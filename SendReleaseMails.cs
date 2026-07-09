using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Npgsql;
using Mailtrap.Emails.Models;

namespace Notify.Function;


public class SendReleaseMails(IMailtrapClient mailtrapClient, IHostEnvironment hostEnvironment)
{
	[Function(nameof(SendReleaseMails))]
	public async Task<HttpResponseData> Run(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sendReleaseMails")]
		HttpRequestData req,
		FunctionContext context)
	{
		var logger = context.GetLogger(nameof(SendReleaseMails));
		logger.LogInformation("{name} function triggered.", nameof(SendReleaseMails));

		VersionRequest? request = null;

        try
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            request = JsonSerializer.Deserialize<VersionRequest>(body);
            logger.LogInformation("Version extracted from request body: {version}", request);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse version from request body.");
        }

		if (request is null)
		{
			var bad = req.CreateResponse(HttpStatusCode.BadRequest);
			await bad.WriteStringAsync("Failed to parse version from request body.");
			return bad;
		}

		try
		{
            var isProduction = hostEnvironment.IsProduction();
			logger.LogInformation("EnvironmentName = '{EnvName}', isProduction = {IsProduction}", hostEnvironment.EnvironmentName, isProduction);

            var recipients = await GetRecipients(logger, isProduction ? Schema.prod : Schema.dev);
			var success = await SendMailsAsync(request.version, recipients, logger, isProduction);
			if (success)
			{
				logger.LogInformation("Release mails sent successfully.");
				return req.CreateResponse(HttpStatusCode.OK);
			}
			else
			{
				logger.LogError("Failed to send Release mails");
				return req.CreateResponse(HttpStatusCode.InternalServerError);
			}
		}
		catch (Exception ex)
		{
			logger.LogError("An error occurred while sending email: {Message}", ex.Message);
            logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
			return req.CreateResponse(HttpStatusCode.InternalServerError);;
		}
	}

    private async Task<List<EmailAddress>> GetRecipients(ILogger logger, Schema schema)
    {
        var connectionString = Environment.GetEnvironmentVariable("DatabaseConnectionString")
            ?? throw new InvalidOperationException("Database connection string is not set in environment variables.");

        try
        {
            logger.LogInformation("Connecting to database {ConnectionString} ...", connectionString.Split('.').First());
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT ""Mail""
                FROM <schema>.""User""
                WHERE ""Preferences"" @> '{""subscribeReleaseMails"": true}'
                OR NOT (""Preferences"" ? 'subscribeReleaseMails')
            ".Replace("<schema>", schema.ToString());
                
            var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            List<EmailAddress> recipients = [];
            while (await reader.ReadAsync())
            {
                recipients.Add(new EmailAddress(reader.GetString(0)));
            }
            return recipients;
        }
        catch (NpgsqlException ex)
        {
            logger.LogError("PostgreSQL-Fehler: {msg}", ex.Message);
            return [];
        }
        catch (Exception ex)
        {
            logger.LogError("Error querying database: {Message}", ex.Message);
            logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
            return [];
        }
    } 

	private async Task<bool> SendMailsAsync(string version, List<EmailAddress> recipients, ILogger logger, bool isProduction)
    {
		try
		{
            var mailFrom = Environment.GetEnvironmentVariable("MailFrom") 
                ?? throw new InvalidOperationException("MailFrom environment variable is not set.");

			SendEmailRequest request = SendEmailRequest
				.Create()
				.From(mailFrom)
				.To(recipients.ToArray())
				.Template("bd270ca7-13ac-434c-88f3-1f6baa38a53d")
                .TemplateVariables(new Dictionary<string, string>
                {
                    { "version", version }
                });


			if (isProduction)
			{
				logger.LogInformation("Running in production mode, sending email via Mailtrap API.");
				SendEmailResponse? response = await mailtrapClient
					.Email()
					.Send(request);
			} 
			else
			{
				logger.LogInformation("Running in development mode, using Mailtrap sandbox.");

				var sandboxId = int.TryParse(Environment.GetEnvironmentVariable("MailSandboxId"), out var id) 
                    ? id : throw new ArgumentException("MailSandboxId environment variable is not set.");

				SendEmailResponse? response = await mailtrapClient
					.Test(sandboxId)
					.Send(request);
			}

			logger.LogInformation("Mail sending process completed successfully.");
			return true;
		}
		catch (Exception ex)
		{
			logger.LogError("An error occurred while sending email: {Message}", ex.Message);
            logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
			return false;
		}
  }

    private enum Schema { prod, dev }

    private class VersionRequest
    {
        public string version { get; set; }
    }
}