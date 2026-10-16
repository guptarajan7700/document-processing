namespace DocumentProcessing.Infrastructure.Exceptions;

public abstract class DocumentProcessingException : Exception
{
    protected DocumentProcessingException(string message) : base(message)
    {
    }

    protected DocumentProcessingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class DocumentValidationException : DocumentProcessingException
{
    public DocumentValidationException(string message) : base(message)
    {
    }

    public DocumentValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class DocumentNotFoundException : DocumentProcessingException
{
    public DocumentNotFoundException(Guid documentId)
        : base($"Document '{documentId}' was not found.")
    {
        DocumentId = documentId;
    }

    public Guid DocumentId { get; }
}

public sealed class InvalidWorkflowTransitionException : DocumentProcessingException
{
    public InvalidWorkflowTransitionException(string message) : base(message)
    {
    }
}

public sealed class DuplicateProcessingException : DocumentProcessingException
{
    public DuplicateProcessingException(Guid documentId)
        : base($"Document '{documentId}' is already being processed.")
    {
        DocumentId = documentId;
    }

    public Guid DocumentId { get; }
}

public class TransientToolException : DocumentProcessingException
{
    public TransientToolException(string message) : base(message)
    {
    }

    public TransientToolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class PermanentToolException : DocumentProcessingException
{
    public PermanentToolException(string message) : base(message)
    {
    }

    public PermanentToolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class OcrTimeoutException : TransientToolException
{
    public OcrTimeoutException(string message, TimeSpan timeout) : base(message)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

public sealed class WorkflowInterruptedException : DocumentProcessingException
{
    public WorkflowInterruptedException(string message) : base(message)
    {
    }
}

public sealed class CompensationFailedException : DocumentProcessingException
{
    public CompensationFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
