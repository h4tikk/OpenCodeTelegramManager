namespace OpenCodeManager.Application;
public sealed class AutonomyUnavailableException(string message) : InvalidOperationException(message);
