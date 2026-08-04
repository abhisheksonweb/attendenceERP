using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<IReadOnlyList<User>> GetByRoleAsync(Enums.UserRole role);
}
