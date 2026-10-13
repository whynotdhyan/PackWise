namespace PackWise.Archiving;

public class ArchiveException : Exception
{
    public ArchiveException()
    {
    }

    public ArchiveException(string message)
        : base(message)
    {
    }

    public ArchiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>No enabled format handler recognised the file.</summary>
public sealed class ArchiveNotRecognizedException : ArchiveException
{
    public ArchiveNotRecognizedException()
    {
    }

    public ArchiveNotRecognizedException(string message)
        : base(message)
    {
    }

    public ArchiveNotRecognizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The file matched a handler that is disabled by the current format policy.</summary>
    public string? DisabledFormatName { get; init; }
}

/// <summary>A password was required and none (or a wrong one) was supplied.</summary>
public sealed class ArchivePasswordException : ArchiveException
{
    public ArchivePasswordException()
    {
    }

    public ArchivePasswordException(string message)
        : base(message)
    {
    }

    public ArchivePasswordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
