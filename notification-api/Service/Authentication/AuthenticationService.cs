using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;

namespace NSA.Service;

/// <summary>Coordinates local account registration and credential verification.</summary>
public sealed class AuthenticationService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    AuthenticationSettings settings,
    TimeProvider timeProvider) : IAuthenticationService
{
    public async Task<AuthenticationResponse> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        ValidateProfile(request, now);
        ValidatePasswordPolicy(request.Password);

        var normalizedEmail = UserCredential.NormalizeEmail(request.Email);
        if (await userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            throw new EmailAlreadyRegisteredException();
        }

        var user = User.Create(
            request.FirstName,
            request.LastName,
            request.Address,
            request.DateOfBirth,
            request.Gender,
            request.ContactNumber,
            request.PhotoUrl,
            now);
        var credential = UserCredential.Create(user.Id, request.Email, passwordHasher.Hash(request.Password), now);
        user.SetCredential(credential);

        userRepository.Add(user);
        await userRepository.SaveChangesAsync(cancellationToken);

        return CreateAuthenticationResponse(user, credential);
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedEmail = UserCredential.NormalizeEmail(request.Email);
        var user = await userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);
        var credential = user?.Credential;

        if (user is null || credential is null || string.IsNullOrWhiteSpace(credential.PasswordHash))
        {
            passwordHasher.VerifyUnknownAccountPassword(request.Password);
            throw new AuthenticationFailedException();
        }

        var verification = passwordHasher.Verify(credential.PasswordHash, request.Password);
        if (credential.IsLockedOut(now))
        {
            throw new AuthenticationFailedException();
        }

        if (verification == PasswordVerificationOutcome.Failed)
        {
            credential.RecordFailedAccess(now, settings.MaxFailedAccessAttempts, settings.LockoutDuration);
            await userRepository.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException();
        }

        if (user.HasEnabledMfaMethod())
        {
            // No second-factor challenge is implemented yet. Refuse to issue a
            // bearer token until the dedicated MFA flow is completed.
            throw new MfaChallengeRequiredException();
        }

        if (verification == PasswordVerificationOutcome.SucceededRehashNeeded)
        {
            credential.ReplacePasswordHash(passwordHasher.Hash(request.Password), now);
        }

        credential.RecordSuccessfulLogin(now);
        await userRepository.SaveChangesAsync(cancellationToken);

        return CreateAuthenticationResponse(user, credential);
    }

    public async Task<AuthenticatedUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        var credential = user?.Credential;
        return user is null || credential is null ? null : ToDto(user, credential);
    }

    private AuthenticationResponse CreateAuthenticationResponse(User user, UserCredential credential)
    {
        var token = accessTokenIssuer.Issue(user, credential);
        return new AuthenticationResponse(token.AccessToken, token.ExpiresAtUtc, ToDto(user, credential));
    }

    private static AuthenticatedUserDto ToDto(User user, UserCredential credential) =>
        new(user.Id, user.FirstName, user.LastName, credential.Email, user.PhotoUrl, user.HasEnabledMfaMethod());

    private static void ValidatePasswordPolicy(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 12
            || password.Length > 128
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new RequestValidationException("Password must be 12-128 characters and include uppercase, lowercase, numeric, and symbol characters.");
        }
    }

    private static void ValidateProfile(SignUpRequest request, DateTimeOffset now)
    {
        // Model binding handles the usual missing-field cases, but retain this
        // boundary validation so whitespace-only names do not reach the domain
        // model as an unhandled ArgumentException (and become a 500 response).
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new RequestValidationException("FirstName and LastName are required.");
        }

        if (!Enum.IsDefined(request.Gender))
        {
            throw new RequestValidationException("Gender must be a defined value.");
        }

        if (request.DateOfBirth is { } dateOfBirth && dateOfBirth > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new RequestValidationException("DateOfBirth cannot be in the future.");
        }
    }
}
