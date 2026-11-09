namespace NSA.Application.Exceptions;

/// <summary>A payload or business state that cannot succeed without correction.</summary>
public class PermanentMessageException(string message) : Exception(message);
