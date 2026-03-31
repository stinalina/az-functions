using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Notify.Function.Models;

namespace Notify.Function;

public class SendWelcomeMail(IMailtrapClient mailtrapClient)
{
	[Function(nameof(SendWelcomeMail))]
	public async Task<HttpResponseData> Run(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sendWelcomeMail")]
		HttpRequestData req,
		FunctionContext context)
	{
		var logger = context.GetLogger(nameof(SendWelcomeMail));
		logger.LogInformation("SendWelcomeMail function triggered.");

		var email = string.Empty;
		var user = default(User?);

		if (context.BindingContext.BindingData.TryGetValue("email", out var bd))
		{
			email = bd?.ToString();
			logger.LogInformation("Email extracted from query parameters: {Email}", email);
		}

		if (string.IsNullOrWhiteSpace(email))
		{
			try
			{
				using var reader = new StreamReader(req.Body);
				var body = await reader.ReadToEndAsync();
				user = JsonSerializer.Deserialize<User>(body);
				if (user != null && !string.IsNullOrWhiteSpace(user.Mail))
				{
					email = user.Mail;
					logger.LogInformation("Email extracted from request body: {Email}", email);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to parse User from request body.");
			}
		}

		if (string.IsNullOrWhiteSpace(email))
		{
			logger.LogError("No email provided as parameter or in request body.");
			var bad = req.CreateResponse(HttpStatusCode.BadRequest);
			await bad.WriteStringAsync("Missing email parameter or User in body");
			return bad;
		}

		try
		{
			var success = await SendMailAsync(email, logger);
			if (success)
			{
				logger.LogInformation("Welcome mail sent successfully.");
				return req.CreateResponse(HttpStatusCode.OK);
			}
			else
			{
				logger.LogError("Failed to send welcome mail");
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

	private async Task<bool> SendMailAsync(string recipientMail, ILogger logger)
  {
		try
		{
			var isProduction = Environment.GetEnvironmentVariable("Production") == "true";
			logger.LogInformation("Creating request and try to send welcome mail...");

      var mailFrom = Environment.GetEnvironmentVariable("MailFrom") 
        ?? throw new InvalidOperationException("MailFrom environment variable is not set.");

			SendEmailRequest request = SendEmailRequest
				.Create()
				.From(mailFrom)
				.To(recipientMail)
				.Template("8425c86a-52bc-4ec5-a8b4-f5c3ca9019d1");

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
}