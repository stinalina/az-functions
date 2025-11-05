using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace Notify.Function;

public class SendWelcomeMail(SmtpClient smtpClient)
{
	private readonly SmtpClient _smtpClient = smtpClient
		?? throw new ArgumentNullException(nameof(smtpClient));

	[Function(nameof(SendWelcomeMail))]
	public async Task<HttpResponseData> Run(
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = "sendWelcomeMail/{email}")]
		HttpRequestData req,
		FunctionContext context)
	{
		var logger = context.GetLogger(nameof(SendWelcomeMail));
		logger.LogInformation("SendWelcomeMail function triggered.");

		var email = string.Empty;
		if (context.BindingContext.BindingData.TryGetValue("email", out var bd))
		{
			email = bd?.ToString();
		}

		if (string.IsNullOrWhiteSpace(email))
		{
			logger.LogError("No email route parameter provided.");
			var bad = req.CreateResponse(HttpStatusCode.BadRequest);
			await bad.WriteStringAsync("Missing route parameter: email");
			return bad;
		}

		try
		{
			// send mail (synchronous method called from a background task to avoid blocking)
			await Task.Run(() => SendMail(email));
			logger.LogInformation($"Welcome mail sent to {email}.");
			return req.CreateResponse(HttpStatusCode.OK);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to send welcome mail.");
			return req.CreateResponse(HttpStatusCode.InternalServerError);;
		}
	}

	private void SendMail(string recipientMail)
  {
		const string subject = "Willkommen bei Remember Me!";
		const string message = "Wilkommen bei Remember Me!; Dies ist eine Willkommensnachricht.";
		this._smtpClient.Send("notify@remember-me.de", recipientMail, subject, message);
		Console.WriteLine("Sent");
  }
}