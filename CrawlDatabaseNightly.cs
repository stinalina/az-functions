using Mailtrap;
using Mailtrap.Emails.Requests;
using Mailtrap.Emails.Responses;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Net.Mail;

namespace Notify.Function;

public class CrawlDatabaseNightly(SmtpClient smtpClient, IMailtrapClient mailtrapClient)
{
  private readonly string ConnectionString = Environment.GetEnvironmentVariable("DatabaseConnectionString")
    ?? throw new InvalidOperationException("Database connection string is not set in environment variables.");

	private readonly SmtpClient _smtpClient = smtpClient
		?? throw new ArgumentNullException(nameof(smtpClient));

// Cron expression: At 23:30 every day
	[Function(nameof(CrawlDatabaseNightly))]
	public async Task Run(
		[TimerTrigger("0 30 23 * * *", RunOnStartup = true)] TimerInfo timerInfo,
		FunctionContext context)
	{
		var logger = context.GetLogger(nameof(CrawlDatabaseNightly));
		logger.LogInformation("CrawlDatabaseNightly function triggered.");

		var tomorrow = DateTime.UtcNow.Date.AddDays(1);
    var notifications = new List<NotificationEntry>();
    var notificationsWithMail = new List<NotificationEntry>();

    try
    {
      await using var conn = new NpgsqlConnection(ConnectionString);
      await conn.OpenAsync();

    var cmd = new NpgsqlCommand(
      @"SELECT n.""Id"", n.""CreatedAt"", n.""DueDate"", n.""Content"", n.""Subject"", u.""Mail""
      FROM dev.""Notification"" n
      JOIN dev.""User"" u ON n.""UserId"" = u.""Id""
      WHERE n.""DueDate""::date = @duedate", conn);
      //cmd.Parameters.AddWithValue("duedate", tomorrow);
      cmd.Parameters.AddWithValue("duedate", new DateTime(2026, 2, 11));

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
        });
      }
    }
    catch (Exception ex)
    {
      logger.LogError($"Error querying database: {ex.Message}");
    }

    if (notifications.Count > 0)
    {
      logger.LogInformation($"Found {notifications.Count} notifications due tomorrow:");
      foreach (var notification in notifications)
      {
        Console.WriteLine($"- {notification.Id} for {notification.Mail} due on {notification.DueDate}");
        await Task.Run(() => SendMail(notification));
			  logger.LogInformation($"Notification mail sent to {notification.Mail}.");
        // tmp deactivated. Free MailTrap can't send that many mails.
        //_smtpClient.Send("notify@remember-me.de", notification.Mail, notification.Subject, notification.Content);
      }
    }
    return;
	}

  private async Task SendMail(NotificationEntry notification)
  {
		try
		{
			var sandboxId = 3946680;
				SendEmailRequest request = SendEmailRequest
						.Create()
						.From("notify@remember-me.de", "Send Test Notification")
						.To(notification.Mail)
            .Template("9f7cfbd8-1061-4e10-8ea6-f37ed5905c7b")
						.Subject(notification.Subject)
            .Text("Hey! Anbei deine Erinnerung von Remember Me!")
            .Html(
                $@"<html>
                    <body>
                        {notification.Content}
                    </body>
                </html>"
            )
            .CustomVariable("content", notification.Content);
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

  public class NotificationEntry
  {
    public Guid Id { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string Content { get; set; }
    public required string Subject {get; set; }
    public required string Mail {get; set; }
  }

}