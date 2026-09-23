using System;
using System.Collections.Generic;
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

        public Guid Role {  get; set; }

        public Guid Status {  get; set; }
    }
}
