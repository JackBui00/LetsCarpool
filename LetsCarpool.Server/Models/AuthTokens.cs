using System;
using System.Collections.Generic;

namespace LetsCarpool.Server.Models;

public partial class AuthTokens
{
    public int TokenId { get; set; }

    public int? UserId { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Users? User { get; set; }
}
