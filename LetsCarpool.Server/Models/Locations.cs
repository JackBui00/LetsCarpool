using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class Locations
{
    public int LocationId { get; set; }

    public int? UserId { get; set; }

    public string? Address { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? LocationName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Users? User { get; set; }
}
