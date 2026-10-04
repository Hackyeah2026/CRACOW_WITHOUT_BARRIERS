using Application.Abstractions;
using Domain.Accounts;

namespace Application.Accounts;

/// <summary>Zakłada konto i od razu loguje.</summary>
public sealed record RegisterUserCommand(UserCredentials Credentials) : ICommand<UserProfile>;

internal sealed class RegisterUserCommandHandler(IAccountClient client) : ICommandHandler<RegisterUserCommand, UserProfile>
{
    public Task<Result<UserProfile>> Handle(RegisterUserCommand command, CancellationToken ct) =>
        command.Credentials.Validate().IfValidAsync(() => client.RegisterAsync(command.Credentials, ct));
}

public sealed record LoginUserCommand(UserCredentials Credentials) : ICommand<UserProfile>;

internal sealed class LoginUserCommandHandler(IAccountClient client) : ICommandHandler<LoginUserCommand, UserProfile>
{
    public Task<Result<UserProfile>> Handle(LoginUserCommand command, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(command.Credentials.Login) || string.IsNullOrEmpty(command.Credentials.Password)
            ? Task.FromResult(Result.Failure<UserProfile>("Podaj login i hasło."))
            : client.LoginAsync(command.Credentials, ct);
}

public sealed record LogoutUserCommand : ICommand;

internal sealed class LogoutUserCommandHandler(IAccountClient client) : ICommandHandler<LogoutUserCommand>
{
    public async Task<Result> Handle(LogoutUserCommand command, CancellationToken ct)
    {
        await client.LogoutAsync(ct);
        return Result.Success();
    }
}

/// <summary>Zalogowany mieszkaniec; błąd, gdy nikt nie jest zalogowany.</summary>
public sealed record GetCurrentUserQuery : IQuery<UserProfile>;

internal sealed class GetCurrentUserQueryHandler(IAccountClient client) : IQueryHandler<GetCurrentUserQuery, UserProfile>
{
    public async Task<Result<UserProfile>> Handle(GetCurrentUserQuery query, CancellationToken ct) =>
        await client.GetCurrentAsync(ct) is { } user
            ? Result.Success(user)
            : Result.Failure<UserProfile>("Nie jesteś zalogowany.");
}
