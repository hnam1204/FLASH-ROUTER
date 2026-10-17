namespace FlashAI.Cli;

public static class LauncherCommandParser
{
    public static ParsedLauncherCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new ParsedLauncherCommand(LauncherCommand.StartAndOpen, null);
        }

        if (args.Count > 1)
        {
            return new ParsedLauncherCommand(
                LauncherCommand.Help,
                "Unexpected arguments. Use 'flashrouter help' for usage.");
        }

        return args[0].Trim().ToLowerInvariant() switch
        {
            "open" => new ParsedLauncherCommand(LauncherCommand.Open, null),
            "status" => new ParsedLauncherCommand(LauncherCommand.Status, null),
            "stop" => new ParsedLauncherCommand(LauncherCommand.Stop, null),
            "restart" => new ParsedLauncherCommand(LauncherCommand.Restart, null),
            "logs" => new ParsedLauncherCommand(LauncherCommand.Logs, null),
            "version" => new ParsedLauncherCommand(LauncherCommand.Version, null),
            "help" or "--help" or "-h" => new ParsedLauncherCommand(LauncherCommand.Help, null),
            _ => new ParsedLauncherCommand(
                LauncherCommand.Help,
                $"Unknown command '{args[0]}'. Use 'flashrouter help' for usage.")
        };
    }
}
