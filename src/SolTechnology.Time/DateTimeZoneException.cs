namespace SolTechnology.Time;

/// <summary>
/// Thrown when a time zone cannot be resolved, a wall time does not exist or is ambiguous under the chosen policy,
/// or a value falls outside the range of <see cref="DateTime"/>.
/// </summary>
public class DateTimeZoneException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public DateTimeZoneException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public DateTimeZoneException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
