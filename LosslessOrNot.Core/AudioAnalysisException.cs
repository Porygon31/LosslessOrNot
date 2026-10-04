namespace LosslessOrNot.Core;

/// <summary>Raised when a file cannot be probed, decoded or analysed. The message is user-facing.</summary>
public sealed class AudioAnalysisException : Exception
{
    public AudioAnalysisException(string message)
        : base(message)
    {
    }

    public AudioAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
