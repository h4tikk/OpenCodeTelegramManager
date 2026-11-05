namespace OpenCodeManager.Application;
public static class CallbackData
{
    public static bool TryParse(string? value, out string action, out string argument)
    {
        action = argument = "";
        if (value is null || value.Length > 64 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ':' or '-')))
            return false;
        var parts = value.Split(':');
        if (parts.Length != 2 || parts[0].Length is 0 or > 20 || parts[1].Length is 0 or > 40)
            return false;
        action = parts[0];
        argument = parts[1];
        return true;
    }
}
