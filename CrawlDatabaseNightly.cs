using System.Runtime.CompilerServices;
using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notify.Function.Models;
using Npgsql;

namespace Notify.Function;

public class CrawlDatabaseNightly(IMailtrapClient mailtrapClient, IHostEnvironment hostEnvironment)
{
  private readonly string ConnectionString = Environment.GetEnvironmentVariable("DatabaseConnectionString")
    ?? throw new InvalidOperationException("Database connection string is not set in environment variables.");

  private ILogger _logger;

// Cron expression: At 23:30 every day
	[Function(nameof(CrawlDatabaseNightly))]
	public async Task Run(
		[TimerTrigger("0 30 23 * * *")] TimerInfo timerInfo,
		FunctionContext context)
	{
		_logger = context.GetLogger(nameof(CrawlDatabaseNightly));
		_logger.LogInformation("CrawlDatabaseNightly function triggered.");

    var isProduction = hostEnvironment.IsProduction();
    _logger.LogInformation("EnvironmentName = '{EnvName}', isProduction = {IsProduction}", hostEnvironment.EnvironmentName, isProduction);
    var schema = isProduction ? "prod" : "dev";

		var tomorrow = DateTime.UtcNow.Date.AddDays(1);
    var notifications = new List<NotificationEntry>();
    var notificationsWithMail = new List<NotificationEntry>();

    try
    {
      _logger.LogInformation("Connecting to database {ConnectionString} ...", ConnectionString.Split('.').First());
      await using var conn = new NpgsqlConnection(ConnectionString);
      await conn.OpenAsync();

      var sql = @"SELECT ""Id"", ""CreatedAt"", ""Content"", ""Subject"", ""Mail""
        FROM <schema>.""Notification""
        WHERE ""IsDraft"" = false AND ""DueDate""::date = @duedate".Replace("<schema>", schema);
        
      var cmd = new NpgsqlCommand(sql, conn);
      cmd.Parameters.AddWithValue("duedate", tomorrow);
      _logger.LogInformation("Add {Tomorrow} as due date", tomorrow);

      await using var reader = await cmd.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        notifications.Add(new NotificationEntry
        {
          Id = reader.GetGuid(0),
          CreatedAt = reader.GetDateTime(1),
          Content = reader.GetString(2),
          Subject = reader.GetString(3),
          Mail = reader.GetString(4),
          Name = reader.GetString(4).Split('@').First()
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError("Error querying database: {Message}", ex.Message);
      _logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
    }

    if (notifications.Count > 0)
    {
      _logger.LogInformation("Found {Count} notifications due tomorrow:", notifications.Count);
      foreach (var notification in notifications)
      {
        await SendMailAsync(notification);
			  _logger.LogInformation("Notification mail sent to {Mail}.", notification.Mail);

        //TODO: erst nutzen, wenn wir wissen was mit den gelöschten Notes passieren soll
        // für Stats wollen wir uns das schon irgendwie vermerken...
        //await DeleteNotificationAsync(notification.Id, schema, logger);
      }
      return;
    }
    _logger.LogInformation("Nothing found to due tomorrow");
	}

  private async Task SendMailAsync(NotificationEntry notification)
  {
		try
		{
      var isProduction = hostEnvironment.IsProduction();
			_logger.LogInformation("Creating request and try to send mail...");

      var mailFrom = Environment.GetEnvironmentVariable("MailFrom") 
        ?? throw new InvalidOperationException("MailFrom environment variable is not set.");

			SendEmailRequest request = SendEmailRequest
				.Create()
        .From(mailFrom)
        .To(notification.Mail)
        .Template("75d0d9f7-1d08-43cd-bd81-bf4587e39cee")
        .TemplateVariables(new Dictionary<string, string>
        {
          { "subject", notification.Subject },
          { "username", notification.Name },
          { "content", notification.Content }
        });

			if (isProduction)
			{
				_logger.LogInformation("Running in production mode, sending email via Mailtrap API.");
				SendEmailResponse? response = await mailtrapClient
					.Email()
					.Send(request);
			} 
			else
			{
			 _logger.LogInformation("Running in development mode, using Mailtrap sandbox.");

				var sandboxId = int.TryParse(Environment.GetEnvironmentVariable("MailSandboxId"), out var id) 
          ? id : throw new ArgumentException("MailSandboxId environment variable is not set.");

				SendEmailResponse? response = await mailtrapClient
					.Test(sandboxId)
					.Send(request);
			}
			_logger.LogInformation("Email sended successfully");
		}
		catch (Exception ex)
		{
      _logger.LogError("An error occurred while sending email: {Message}", ex.Message);
      _logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
		}
  }

  private async Task DeleteNotificationAsync(Guid id, string schema)
  {
    var sql = @"DELETE FROM <schema>.""Notification"" WHERE ""Id"" = @id".Replace("<schema>", schema);

    try
    {
      logger.LogInformation("Connecting to database {ConnectionString} ...", ConnectionString.Split('.').First());
      await using var conn = new NpgsqlConnection(ConnectionString);
      await conn.OpenAsync();

      var cmd = new NpgsqlCommand(sql, conn);
      cmd.Parameters.AddWithValue("id", id);
      await using var reader = await cmd.ExecuteReaderAsync();
      logger.LogInformation("Deleting notification with Id {Id}", id);
    } 
    catch (Exception ex)
    {
      logger.LogError("Error deleting notification with Id {Id}: {Message}", id, ex.Message);
      logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
    }
  }

  private async Task GetAttachments(attachmentIds... string)
  {
    try
    {
      using var mailtrapFactory = new MailtrapClientFactory("YOUR_API_KEY");
          var client = mailtrapFactory.CreateClient();
          var inboxId = 54321;
          var messageId = 67890;
          var attachmentId = 222222;
          var attachment = await client
              .Account(YOUR_ACCOUNT_ID)
              .Inbox(inboxId)
              .Message(messageId)
              .Attachment(attachmentId)
              .GetDetails();
    } catch(HttpRequestError e)
    {
       //TODO response Errors https://docs.mailtrap.io/developers/email-sandbox/attachments
    }
  }
}