using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

/// <summary>Use cases for local account registration and sign-in.</summary>
public interface IAuthenticationService
{
    Task<AuthenticationResponse> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken);
    Task<AuthenticationResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthenticatedUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}

