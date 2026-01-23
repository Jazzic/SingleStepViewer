using SingleStepViewer.Data.Entities;

namespace SingleStepViewer.Services.Interfaces;

public interface IUserService
{
    Task<ApplicationUser?> GetUserByIdAsync(string userId);
    Task<ApplicationUser?> GetUserByEmailAsync(string email);
    Task<IEnumerable<ApplicationUser>> GetAllUsersAsync();
    Task<bool> DeleteUserAsync(string userId);
}
