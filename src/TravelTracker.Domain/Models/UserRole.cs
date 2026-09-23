using System;
using System.Collections.Generic;
using System.Text;

namespace TravelTracker.Domain.Models
{
    public sealed class UserRole
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }
}
