using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class UserRepository : JsonRepositoryBase<User>, IUserRepository
{
    public UserRepository(JsonFileStore store) : base(store, "users.json", u => u.Id) { }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<User>> GetByRoleAsync(UserRole role)
    {
        var all = await GetAllAsync();
        return all.Where(u => u.Role == role).ToList();
    }
}
