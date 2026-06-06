using System.Runtime.CompilerServices;
using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Notify.Function;

public class CrawlDatabaseNightly(IMailtrapClient mailtrapClient, IHostEnvironment hostEnvironment)
{
  private readonly string ConnectionString = Environment.GetEnvironmentVariable("DatabaseConnectionString")
    ?? throw new InvalidOperationException("Database connection string is not set in environment variables.");

// Cron expression: At 23:30 every day
	[Function(nameof(CrawlDatabaseNightly))]
	public async Task Run(
		[TimerTrigger("0 30 23 * * *")] TimerInfo timerInfo,
		FunctionContext context)
	{
		var logger = context.GetLogger(nameof(CrawlDatabaseNightly));
		logger.LogInformation("CrawlDatabaseNightly function triggered.");

    var isProduction = hostEnvironment.IsProduction();
    logger.LogInformation("EnvironmentName = '{EnvName}', isProduction = {IsProduction}", hostEnvironment.EnvironmentName, isProduction);
    var schema = isProduction ? "prod" : "dev";

		var tomorrow = DateTime.UtcNow.Date.AddDays(1);
    var notifications = new List<NotificationEntry>();
    var notificationsWithMail = new List<NotificationEntry>();

    try
    {
      logger.LogInformation("Connecting to database {ConnectionString} ...", ConnectionString.Split('.').First());
      await using var conn = new NpgsqlConnection(ConnectionString);
      await conn.OpenAsync();

      var sql = @"SELECT ""Id"", ""CreatedAt"", ""Content"", ""Subject"", ""Mail""
        FROM <schema>.""Notification""
        WHERE ""IsDraft"" = false AND ""DueDate""::date = @duedate".Replace("<schema>", schema);
        
      var cmd = new NpgsqlCommand(sql, conn);
      cmd.Parameters.AddWithValue("duedate", tomorrow);
      logger.LogInformation("Add {Tomorrow} as due date", tomorrow);

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
      logger.LogError("Error querying database: {Message}", ex.Message);
      logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
    }

    if (notifications.Count > 0)
    {
      logger.LogInformation("Found {Count} notifications due tomorrow:", notifications.Count);
      foreach (var notification in notifications)
      {
        await SendMailAsync(notification, logger);
			  logger.LogInformation("Notification mail sent to {Mail}.", notification.Mail);

        //TODO: erst nutzen, wenn wir wissen was mit den gelöschten Notes passieren soll
        // für Stats wollen wir uns das schon irgendwie vermerken...
        //await DeleteNotificationAsync(notification.Id, schema, logger);
      }
      return;
    }
    logger.LogInformation("Nothing found to due tomorrow");
	}

  private async Task SendMailAsync(NotificationEntry notification, ILogger logger)
  {
		try
		{
      var isProduction = hostEnvironment.IsProduction();
			logger.LogInformation("Creating request and try to send mail...");

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
			logger.LogInformation("Email sended successfully");
		}
		catch (Exception ex)
		{
      logger.LogError("An error occurred while sending email: {Message}", ex.Message);
      logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
		}
  }

  private async Task DeleteNotificationAsync(Guid id, string schema, ILogger logger)
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

  public class NotificationEntry
  {
    public Guid Id { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string Content { get; set; }
    public required string Subject { get; set; }
    public required string Mail { get; set; }
    public required string Name { get; set; }
  }
}