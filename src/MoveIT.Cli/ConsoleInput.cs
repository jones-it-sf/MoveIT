using System.Text;

namespace MoveIT.Cli;

internal static class ConsoleInput
{
    public static bool IsInteractive => Environment.UserInteractive && !Console.IsInputRedirected;

    public static bool Confirm(string prompt)
    {
        Console.Write($"{prompt} Type YES to continue: ");
        return string.Equals(Console.ReadLine()?.Trim(), "YES", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ReadNewPassword(string accountName)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var password = ReadHidden($"Password for new local account '{accountName}': ");
            if (password.Length == 0)
            {
                Console.WriteLine("The password cannot be empty.");
                continue;
            }

            if (ReadHidden("Confirm password: ") == password)
            {
                return password;
            }
            Console.WriteLine("The passwords do not match.");
        }
        return null;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return builder.ToString();
    }
}
