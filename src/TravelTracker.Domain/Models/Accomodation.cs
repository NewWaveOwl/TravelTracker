using System;
using System.Collections.Generic;
using System.Text;
using TravelTracker.Domain.Types;

namespace TravelTracker.Domain.Models
{
    /// <summary>
    /// Represents a single Accomodation.
    /// An accomodation has an ID, a name, and an address.
    /// </summary>
    public sealed class Accomodation
    {
        public EntityId<Accomodation> Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }
}
