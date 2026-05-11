namespace Autotrade.Application.Exceptions;

/// <summary>
/// Base exception for expected application/business rule failures.
/// </summary>
public class BusinessException : Exception
{
    public string? ErrorCode { get; }

    public BusinessException(string message)
        : base(message)
    {
    }

    public BusinessException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public BusinessException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public BusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a requested entity cannot be found.
/// </summary>
public sealed class EntityNotFoundException : BusinessException
{
    public string EntityName { get; }

    public object Id { get; }

    public EntityNotFoundException(string entityName, object id)
        : base($"{entityName} was not found. Id: {id}", "ENTITY_NOT_FOUND")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(id);

        EntityName = entityName;
        Id = id;
    }
}

/// <summary>
/// Raised when an entity already exists for a unique field.
/// </summary>
public sealed class EntityAlreadyExistsException : BusinessException
{
    public string EntityName { get; }

    public string FieldName { get; }

    public object Value { get; }

    public EntityAlreadyExistsException(string entityName, string fieldName, object value)
        : base($"{entityName} already exists. {fieldName}: {value}", "ENTITY_ALREADY_EXISTS")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(value);

        EntityName = entityName;
        FieldName = fieldName;
        Value = value;
    }
}

/// <summary>
/// Raised when application input validation fails.
/// </summary>
public sealed class ValidationException : BusinessException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(string message)
        : base(message, "VALIDATION_ERROR")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Input validation failed.", "VALIDATION_ERROR")
    {
        ArgumentNullException.ThrowIfNull(errors);

        Errors = errors;
    }
}
