using System;
using System.Collections.Generic;
using System.Text;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories
{
    internal interface IUserRepository
    {
        Task<User?> GetByIdAsync(Guid userId);
        Task<User?> GetByEmailAsync(string email);
        Task<IReadOnlyCollection<User>> GetAllAsync();
        Task<User> CreateAsync(User user);
        Task<User> UpdateAsync(User user);
        Task<bool> DeleteAsync(Guid userId);
        Task<User?> ChangeStatusAsync(Guid userId, UserStatus status);
    }
}
