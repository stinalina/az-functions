using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Notify.Function;

public class CrawlDatabaseNightly(IMailtrapClient mailtrapClient)
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

		var tomorrow = DateTime.UtcNow.Date.AddDays(1);
    var notifications = new List<NotificationEntry>();
    var notificationsWithMail = new List<NotificationEntry>();

    try
    {
      logger.LogInformation("Connecting to database...");
      await using var conn = new NpgsqlConnection(ConnectionString);
      await conn.OpenAsync();

      var isProduction = Environment.GetEnvironmentVariable("Production") == "true";
      var schema = isProduction ? "prod" : "dev";
      var sql = @"SELECT n.""Id"", n.""CreatedAt"", n.""DueDate"", n.""Content"", n.""Subject"", u.""Mail"", u.""Name""
        FROM <schema>.""Notification"" n
        JOIN <schema>.""User"" u ON n.""UserId"" = u.""Id""
        WHERE n.""DueDate""::date = @duedate".Replace("<schema>", schema);
        
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
          DueDate = reader.GetDateTime(2),
          Content = reader.GetString(3),
          Subject = reader.GetString(4),
          Mail = reader.GetString(5),
          Name = reader.GetString(6)
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
      }
      return;
    }
    logger.LogInformation("Nothing found to due tomorrow");
	}

  private async Task SendMailAsync(NotificationEntry notification, ILogger logger)
  {
    if (notification.Name == "Unknown") {
      notification.Name = "Unbekannter Nutzer";
    }

		try
		{
      var isProduction = Environment.GetEnvironmentVariable("Production") == "true";
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