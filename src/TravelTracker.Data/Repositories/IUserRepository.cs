using System;
using System.Collections.Generic;
using System.Text;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories
{
    internal interface IUserRepository
    {
        /// <summary>
        /// Gets a single user by their ID.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns>The (possible) user found.</returns>
        Task<User?> GetByIdAsync(Guid userId);

        /// <summary>
        /// Gets a single user by their email.
        /// </summary>
        /// <param name="email">The user's email.</param>
        /// <returns>The (possible) user found.</returns>
        Task<User?> GetByEmailAsync(string email);

        /// <summary>
        /// Gets all the users in the system.
        /// </summary>
        /// <returns>A list of users.</returns>
        Task<List<User>> GetAllAsync();

        /// <summary>
        /// Adds a user to the system.
        /// </summary>
        /// <param name="user">The user to be created.</param>
        /// <returns>The successfully added user.</returns>
        Task<User> CreateAsync(User user);

        /// <summary>
        /// Modifies a user's information.
        /// </summary>
        /// <param name="user">The user with their modfied fields.</param>
        /// <returns>The updated user.</returns>
        Task<User> UpdateAsync(User user);

        /// <summary>
        /// Deletes a user from the system.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns>True if the deletion was successful. Otherwise, false.</returns>
        Task<bool> DeleteAsync(Guid userId);

        /// <summary>
        /// Modifies a user's status.
        /// </summary>
        /// <param name="userId">The ID of the user to be modified..</param>
        /// <param name="status">The new status.</param>
        /// <returns>The (possible) user found with its updated staus.</returns>
        Task<User?> ChangeStatusAsync(Guid userId, UserStatus status);
    }
}
