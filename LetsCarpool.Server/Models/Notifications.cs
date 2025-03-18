using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class Notifications
{
    public int NotificationId { get; set; }

    public int? UserId { get; set; }

    public string? Message { get; set; }

    public bool? IsRead { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Users? User { get; set; }
}
