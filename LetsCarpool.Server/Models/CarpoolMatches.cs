using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class CarpoolMatches
{
    public int CarpoolMatchId { get; set; }

    public int? DriverId { get; set; }

    public int? RiderId { get; set; }

    public int? RideRequestId { get; set; }

    public string? MatchStatus { get; set; }

    public DateTime? MatchTime { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Users? Driver { get; set; }

    public virtual RideRequests? RideRequest { get; set; }

    public virtual Users? Rider { get; set; }
}
