  namespace Notify.Function.Models;
  
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