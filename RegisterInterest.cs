using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Notify.Function.Models;
using Azure.Data.Tables;

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

		// Prüfe Query-Parameter onlyValue
		var onlyValue = !string.IsNullOrEmpty(req.Query["onlyValue"]) && 
			req.Query["onlyValue"].Equals("true", StringComparison.OrdinalIgnoreCase);
		
		if (onlyValue)
		{
			var callCount = await GetCallCounter(logger);
			var response = req.CreateResponse(HttpStatusCode.OK);
			await response.WriteAsJsonAsync(new { success = true, callCount });
			return response;
		}

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
			var success = await SendMailAsync(email, logger);
			if (success)
			{
				logger.LogInformation($"RegisterInterest mail sent to {email}.");
				
				// Zähler aktualisieren
				var callCount = await IncrementCallCounter(logger);
				
				var response = req.CreateResponse(HttpStatusCode.OK);
				await response.WriteAsJsonAsync(new { success = true, callCount });
				return response;
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
			return req.CreateResponse(HttpStatusCode.InternalServerError);
		}
	}

	private async Task<bool> SendMailAsync(string recipientMail, ILogger logger)
  {
		try
		{
			var sandboxId = 3946680;
				SendEmailRequest request = SendEmailRequest
						.Create()
						.From("notify@rememberMe.de", "Register Interest Mail")
						.To(recipientMail)
						.Subject("Interesse bekunden");
				request.TextBody = $"Hallo {recipientMail},\n\nvielen Dank für dein Interesse an rememberMe! Wir halten dich auf dem Laufenden über Neuigkeiten und Updates rund um unsere App.\n\nBeste Grüße,\nDein rememberMe Team";
				SendEmailResponse? response = await mailtrapClient
					.Test(sandboxId) //In production  here we call .Email()
					.Send(request);
				logger.LogError("Response was: {0}", response);
				return true;
		}
		catch (Exception ex)
		{
				logger.LogError(ex, "An error occurred while sending email.");
				return false;
		}
  }

	private async Task<int> IncrementCallCounter(ILogger logger)
	{
		try
		{
			var connectionString = this.GetConnectionString(logger);
			if (string.IsNullOrWhiteSpace(connectionString))
			{
				var environment =
					Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
					Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
				var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
				if (isDevelopment)
				{
					connectionString = "UseDevelopmentStorage=true";
				}
				else
				{
					logger.LogError("AzureWebJobsStorage environment variable is not set.");
					throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set.");
				}
			}

			var tableClient = new TableClient(connectionString, "functionmetrics");
			await tableClient.CreateAsync();

			const string partitionKey = "RegisterInterest";
			const string rowKey = "CallCount";

			CallCounterMetric counter;
			try
			{
				var result = await tableClient.GetEntityAsync<CallCounterMetric>(partitionKey, rowKey);
				counter = result.Value;
			}
			catch (Azure.RequestFailedException ex) when (ex.Status == 404)
			{
				counter = new CallCounterMetric
				{
					Count = 0,
					PartitionKey = partitionKey,
					RowKey = rowKey,
				};
			}

			counter.Count++;
			await tableClient.UpsertEntityAsync(counter, TableUpdateMode.Replace);

			logger.LogInformation($"RegisterInterest call count: {counter.Count}");
			return counter.Count;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to update call counter");
			return 0;
		}
	}

	private async Task<int> GetCallCounter(ILogger logger)
	{
		try
		{
			var connectionString = this.GetConnectionString(logger);
			var tableClient = new TableClient(connectionString, "functionmetrics");
			await tableClient.CreateAsync();

			const string partitionKey = "RegisterInterest";
			const string rowKey = "CallCount";

			CallCounterMetric counter;
			try
			{
				var result = await tableClient.GetEntityAsync<CallCounterMetric>(partitionKey, rowKey);
				counter = result.Value;
			}
			catch (Azure.RequestFailedException ex) when (ex.Status == 404)
			{
				counter = new CallCounterMetric
				{
					Count = 0,
					PartitionKey = partitionKey,
					RowKey = rowKey,
				};
			}

			return counter.Count;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to get call counter");
			return 0;
		}
	}

	private string GetConnectionString(ILogger logger)
	{
		var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
		if (string.IsNullOrWhiteSpace(connectionString))
		{
			var environment =
				Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
				Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
			var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
			if (isDevelopment)
			{
				connectionString = "UseDevelopmentStorage=true";
			}
			else
			{
				logger.LogError("AzureWebJobsStorage environment variable is not set.");
				throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set.");
			}
		}
		return connectionString;
	}
}


public class CallCounterMetric : ITableEntity
{
	public int Count { get; set; }
	public string PartitionKey { get; set; } = string.Empty;
	public string RowKey { get; set; } = string.Empty;
	public DateTimeOffset? Timestamp { get; set; }
	public Azure.ETag ETag { get; set; }
}
