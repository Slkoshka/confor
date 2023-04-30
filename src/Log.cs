using Pastel;

class Log
{
    public static bool IsVerbose { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    public static void Warning(string text) => Console.WriteLine($" ⚠️ {text}".Pastel(ConsoleColor.Yellow));
    public static void Debug(string text) { if (IsVerbose) { Console.WriteLine($" ℹ️ {text}".Pastel(ConsoleColor.Gray)); } }
    public static void Info(string text) => Console.WriteLine($" ✨ {text}".Pastel(ConsoleColor.White));
}
