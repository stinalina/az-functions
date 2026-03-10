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

public class RegisterInterest(IMailtrapClient mailtrapClient)
{
	[Function(nameof(RegisterInterest))]
	public async Task<HttpResponseData> Run(
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "registerInterest")]
		HttpRequestData req,
		FunctionContext context)
	{
		var logger = context.GetLogger(nameof(RegisterInterest));
		logger.LogInformation("RegisterInterest function triggered.");

		var email = string.Empty;
		var user = default(User?);

		if (context.BindingContext.BindingData.TryGetValue("email", out var bd))
		{
			email = bd?.ToString();
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
			// send mail (synchronous method called from a background task to avoid blocking)
			var success =await Task.Run(() => SendMail(email, user?.Name));
			if (success)
			{
				logger.LogInformation($"RegisterInterest mail sent to {email}.");
				return req.CreateResponse(HttpStatusCode.OK);
			}
			else
			{
				logger.LogError($"Failed to send RegisterInterest mail to {email}.");
				return req.CreateResponse(HttpStatusCode.InternalServerError);
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to send RegisterInterest mail.");
			return req.CreateResponse(HttpStatusCode.InternalServerError);;
		}
	}

	private async Task<bool> SendMail(string recipientMail, string? recipientName)
  {
		try
		{
			var sandboxId = 3946680;
				SendEmailRequest request = SendEmailRequest
						.Create()
						.From("notify@rememberMe.de", "Register Interest Mail")
						.To(recipientMail)
						.Subject("Interesse bekunden");
				request.TextBody = $"Hallo {recipientName ?? recipientMail},\n\nvielen Dank für dein Interesse an rememberMe! Wir halten dich auf dem Laufenden über Neuigkeiten und Updates rund um unsere App.\n\nBeste Grüße,\nDein rememberMe Team";
				SendEmailResponse? response = await mailtrapClient
					.Test(sandboxId) //In production  here we call .Email()
					.Send(request);
				Console.WriteLine("Response was: {0}", response);
				return true;
		}
		catch (Exception ex)
		{
				Console.WriteLine("An error occurred while sending email: {0}", ex);
				return false;
		}
  }
}
