using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;

namespace Notify.Function;

public class SendWelcomeMail(SmtpClient smtpClient, IMailtrapClient mailtrapClient)
{
	private readonly SmtpClient _smtpClient = smtpClient
		?? throw new ArgumentNullException(nameof(smtpClient));

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
			await Task.Run(() => SendMail(email, user?.Name));
			logger.LogInformation($"Welcome mail sent to {email}.");
			return req.CreateResponse(HttpStatusCode.OK);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to send welcome mail.");
			return req.CreateResponse(HttpStatusCode.InternalServerError);;
		}
	}

	private async Task SendMail(string recipientMail, string? recipientName)
  {
		// const string subject = "Willkommen bei Remember Me!";
		// string message = $"Wilkommen {recipientName ?? ""} bei Remember Me! Du hast soeben deine erste Erinnerung erstellt. Erstelle doch auch ein Konto bei uns, damit du deine Erinnerungen bearbeiten kannst!";
		// _smtpClient.Send("notify@remember-me.de", recipientMail, subject, message);
		// Console.WriteLine("Sent");

		
		try
		{
			var sandboxId = 3946680;
				SendEmailRequest request = SendEmailRequest
						.Create()
						.From("notify@remember-me.de", "Welcome Mail")
						.To(recipientMail)
						.Template("8425c86a-52bc-4ec5-a8b4-f5c3ca9019d1")
						.TemplateVariables(new Dictionary<string, object> // Optional template  parameters
						{
								{ "company_info_name", "Notify" },
								{ "company_info_address", "Test_Company_info_address" },
								{ "company_info_city", "Heidelberg" },
								{ "company_info_country", "Deutschland" }
						});
				SendEmailResponse? response = await mailtrapClient
					.Test(sandboxId) //In production  here we call .Email()
					.Send(request);
				Console.WriteLine("Response was: {0}", response);
		}
		catch (Exception ex)
		{
				Console.WriteLine("An error occurred while sending email: {0}", ex);
		}
  }
}

public class User
{
	public required string Mail { get; set; }
	public required string Name { get; set; }
}