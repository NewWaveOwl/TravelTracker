using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TravelTracker.Domain.Models
{
    /// <summary>
    /// Represents a single user.
    /// A user has an ID, a username, a role and a status.
    /// </summary>
    public sealed class User
    {
        public Guid UserId { get; set; }   
        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Address { get; set; }

        public UserRole Role { get; set; } = UserRole.User;

        public UserStatus Status { get; set; } = UserStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt {  get; set; }
    }
}
