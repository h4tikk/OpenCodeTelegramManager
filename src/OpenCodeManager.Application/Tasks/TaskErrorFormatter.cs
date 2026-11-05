namespace OpenCodeManager.Application;
internal static class TaskErrorFormatter
{
    public static string SafeError(Exception e) => e switch
    {
        OpenCodeApiException apiError => apiError.Message,
        AttachmentPreparationException fileError => fileError.Message,
        AutonomyUnavailableException autonomyError => autonomyError.Message,
        _ => e.GetType().Name
    };
}
