using MediatR;

namespace Application.Abstractions;

public interface ICommand : IRequest<Result> { }

public interface ICommand<TResponse> : IRequest<Result<TResponse>> where TResponse : notnull { }

public interface IQuery<TResponse> : IRequest<Result<TResponse>> where TResponse : notnull { }

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result> where TCommand : ICommand { }

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse> where TResponse : notnull { }

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse> where TResponse : notnull { }

internal static class ValidationExtensions
{
    /// <summary>
    /// Wysyła żądanie do hosta tylko wtedy, gdy walidacja nie zgłosiła błędów; w przeciwnym razie zwraca je
    /// jako jeden komunikat, bez połączenia z hostem.
    /// </summary>
    public static Task<Result<T>> IfValidAsync<T>(this IReadOnlyList<string> errors, Func<Task<Result<T>>> send) where T : notnull =>
        errors.Count > 0 ? Task.FromResult(Result.Failure<T>(string.Join(" ", errors))) : send();
}
