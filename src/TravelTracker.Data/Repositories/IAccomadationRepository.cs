using System;
using System.Collections.Generic;
using System.Text;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories
{
    internal interface IAccomadationRepository
    {
        Task<Accomodation?> GetByIdAsync(Guid accommodationId);
        Task<IReadOnlyCollection<Accomodation>> GetAllAsync();
        Task<Accomodation> CreateAsync(Accomodation accommodation);
        Task<Accomodation> UpdateAsync(Accomodation accommodation);
        Task<bool> DeleteAsync(Guid accommodationId);
    }
}
