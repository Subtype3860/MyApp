using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Security;

namespace MyApp.Application.Services;

public sealed class AuthenticationService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenProvider tokenProvider) : IAuthenticationService
{
    public async Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedLogin = request.UserNameOrEmail.Trim();
        var user = await userRepository.FindByLoginAsync(
            normalizedLogin,
            cancellationToken);

        if (user is null ||
            !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return ServiceResult<AuthResponse>.Unauthorized();
        }

        var permissions = UserAccess.EffectivePermissions(user);

        return ServiceResult<AuthResponse>.Success(
            new AuthResponse(tokenProvider.Create(user), user.Role, permissions));
    }
}
