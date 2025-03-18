using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class RidePreferences
{
    public int PreferenceId { get; set; }

    public int? UserId { get; set; }

    public int? MaxDetourTime { get; set; }

    public bool? IsFlexiblePickupTime { get; set; }

    public string? PreferredPickupLocation { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Users? User { get; set; }
}
