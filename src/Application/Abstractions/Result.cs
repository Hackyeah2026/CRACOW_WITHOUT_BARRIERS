namespace Application.Abstractions;

public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
    public static Result<T> Success<T>(T value) where T : notnull => new(value, true, null);
    public static Result<T> Failure<T>(string error) where T : notnull => new(default, false, error);
}

public sealed class Result<T> : Result where T : notnull
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, string? error) : base(isSuccess, error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Brak wartości: {Error}");
}
