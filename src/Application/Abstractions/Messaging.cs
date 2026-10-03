using MediatR;

namespace Application.Abstractions;

public interface IBaseCommand { }

public interface ICommand : IRequest<Result>, IBaseCommand { }

public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand where TResponse : notnull { }

public interface IQuery<TResponse> : IRequest<Result<TResponse>> where TResponse : notnull { }

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result> where TCommand : ICommand { }

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse> where TResponse : notnull { }

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse> where TResponse : notnull { }
