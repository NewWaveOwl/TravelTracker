using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TravelTracker.Domain.Models
{
    /// <summary>
    /// Represents a single user
    /// A user has an ID, a username, an email, a possible address, a role, a status, a date of creation, and a date of its latest field update(s).
    /// </summary>
    public sealed class User
    {
        public Guid UserId { get; set; }   
        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Address { get; set; } = string.Empty;

        //Can be either a user or an admin
        public UserRole Role { get; set; } = UserRole.User;

        //Can be either Active, Deactive, or banned
        public UserStatus Status { get; set; } = UserStatus.Active;

        //The date on which the user was added to the system
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        //The date on which the user's info got updated
        public DateTime? UpdatedAt {  get; set; }
    }
}
