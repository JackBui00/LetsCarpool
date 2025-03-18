using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class RideHistory
{
    public int RideHistoryId { get; set; }

    public int? DriverId { get; set; }

    public int? RiderId { get; set; }

    public string? StartLocation { get; set; }

    public string? EndLocation { get; set; }

    public DateTime? RideStartTime { get; set; }

    public DateTime? RideEndTime { get; set; }

    public double? TotalDistance { get; set; }

    public int? Rating { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Users? Driver { get; set; }

    public virtual Users? Rider { get; set; }
}
