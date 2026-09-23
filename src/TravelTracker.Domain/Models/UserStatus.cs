using System;
using System.Collections.Generic;
using System.Text;

namespace TravelTracker.Domain.Models
{
    public sealed class UserStatus
    {
        public Guid StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;
    }
}
