using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class RideRequests
{
    public int RideRequestId { get; set; }

    public int? UserId { get; set; }

    public string? PickupLocation { get; set; }

    public string? DestinationLocation { get; set; }

    public DateTime? PreferredPickupTime { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<CarpoolMatches> CarpoolMatches { get; set; } = new List<CarpoolMatches>();

    public virtual Users? User { get; set; }
}
