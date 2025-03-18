using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class Users
{
    public int UserId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string Email { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public int? RoleId { get; set; }

    public string? Address { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<AuthTokens> AuthTokens { get; set; } = new List<AuthTokens>();

    public virtual ICollection<CarpoolMatches> CarpoolMatchesDriver { get; set; } = new List<CarpoolMatches>();

    public virtual ICollection<CarpoolMatches> CarpoolMatchesRider { get; set; } = new List<CarpoolMatches>();

    public virtual ICollection<Locations> Locations { get; set; } = new List<Locations>();

    public virtual ICollection<Notifications> Notifications { get; set; } = new List<Notifications>();

    public virtual ICollection<RideHistory> RideHistoryDriver { get; set; } = new List<RideHistory>();

    public virtual ICollection<RideHistory> RideHistoryRider { get; set; } = new List<RideHistory>();

    public virtual ICollection<RidePreferences> RidePreferences { get; set; } = new List<RidePreferences>();

    public virtual ICollection<RideRequests> RideRequests { get; set; } = new List<RideRequests>();
}
