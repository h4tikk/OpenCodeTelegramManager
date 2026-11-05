using OpenCodeManager.Application;

namespace OpenCodeManager.Telegram;
public static class TelegramErrors
{
    public static string SafeError(Exception e) => e switch
    {
        OpenCodeApiException apiError => apiError.Message,
        UnauthorizedAccessException => "запрещённый пользователь или каталог",
        ArgumentException => "неверный ввод",
        InvalidDataException => "файл не прошёл проверку формата, размера или лимитов",
        InvalidOperationException => "состояние изменилось или операция запрещена; обновите список",
        _ => e.GetType().Name
    };
}
