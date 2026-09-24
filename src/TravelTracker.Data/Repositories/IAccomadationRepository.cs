using System;
using System.Collections.Generic;
using System.Text;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories
{
    internal interface IAccomadationRepository
    {
        /// <summary>
        /// Gets an accomodation by its ID.
        /// </summary>
        /// <param name="accommodationId">The accomodation ID.</param>
        /// <returns>The (possible) accomodation found.</returns>
        Task<Accomodation?> GetByIdAsync(Guid accommodationId);

        /// <summary>
        /// Gets all the accomodations in the system.
        /// </summary>
        /// <returns>A list of accomodations.c</returns>
        Task<List<Accomodation>> GetAllAsync();

        /// <summary>
        /// Adds an accomodation to the system.
        /// </summary>
        /// <param name="accommodation">The accomodation to be created.</param>
        /// <returns>The new accomodation.</returns>
        Task<Accomodation> CreateAsync(Accomodation accommodation);

        /// <summary>
        /// Modifies an accomodation's information.
        /// </summary>
        /// <param name="accommodation">The accomodation with updated fields.</param>
        /// <returns>The modified accomodation.</returns>
        Task<Accomodation> UpdateAsync(Accomodation accommodation);

        /// <summary>
        /// Deletes an accomodation from the system.
        /// </summary>
        /// <param name="accommodationId">The accomodation ID.</param>
        /// <returns>True if the deletion was successful. Otherwise, false.</returns>
        Task<bool> DeleteAsync(Guid accommodationId);
    }
}
