using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;

namespace NSA.Persistence.Concrete;

/// <summary>EF Core adapter for local account lookup and persistence.</summary>
public sealed class UserRepository(NotificationDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.Credential)
            .Include(user => user.MfaMethods)
            .SingleOrDefaultAsync(user => user.Credential!.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.Credential)
            .Include(user => user.MfaMethods)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public void Add(User user)
    {
        dbContext.Users.Add(user);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateNormalizedEmail(exception))
        {
            // The pre-check in AuthenticationService gives the common case a
            // friendly response. The database unique index closes the race
            // between two concurrent registrations for the same email.
            throw new EmailAlreadyRegisteredException();
        }
    }

    private static bool IsDuplicateNormalizedEmail(DbUpdateException exception) =>
        exception.ToString().Contains(
            "IX_UserCredentials_NormalizedEmail",
            StringComparison.OrdinalIgnoreCase);
}

