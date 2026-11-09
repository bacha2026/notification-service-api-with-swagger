using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

/// <summary>Persistence port for local account authentication.</summary>
public interface IUserRepository
{
    Task<IReadOnlyList<string>> GetAdminEmailsAsync(CancellationToken cancellationToken);
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> GetByPasswordResetTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    void Add(User user);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
