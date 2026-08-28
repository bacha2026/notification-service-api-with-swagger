using NSA.Application.Contracts;
using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

/// <summary>Issues a short-lived access token for an authenticated user.</summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(User user, UserCredential credential);
}
