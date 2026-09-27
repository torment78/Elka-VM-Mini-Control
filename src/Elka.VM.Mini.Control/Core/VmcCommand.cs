using System.Text.RegularExpressions;

namespace Elka.VM.Mini.Control.Core;

public enum SelAction { Toggle, On, Off }
public sealed record VmcCommand(bool Apply, int Bus, SelAction Action);

public static partial class VmcCommands
{
    public const string Examples = "VMC.SEL(A1);\nVMC.SEL(A1)=1;\nVMC.SEL(A1)=0;\nVMC.SEL.Apply(A1);\nVMC.SEL.Apply(A2);\nVMC.SEL.Apply[B3];";

    public static async Task<string?> ExecuteAsync(string text, MixerController mixer, Func<int, IEnumerable<int>> savedTargets, CancellationToken token = default)
    {
        var commands = Parse(text); // Validate every statement before the first write.
        string? result = null;
        foreach (var command in commands)
        {
            token.ThrowIfCancellationRequested();
            if (command.Apply) result = await mixer.ApplyFromAsync(command.Bus, savedTargets(command.Bus));
            else await mixer.SelectAsync(command.Bus, command.Action, token);
        }
        return result;
    }

    public static IReadOnlyList<VmcCommand> Parse(string text)
    {
        string[] statements = text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (statements.Length is 0 or > 16) throw new FormatException("Send 1–16 VMC commands per packet.");
        var commands = new List<VmcCommand>();
        foreach (string statement in statements)
        {
            var match = Syntax().Match(statement);
            if (!match.Success || (match.Groups["open"].Value == "(" ? match.Groups["close"].Value != ")" : match.Groups["close"].Value != "]"))
                throw new FormatException("Use VMC.SEL(A1); or VMC.SEL.Apply(A1); with matching brackets.");
            string args = match.Groups["args"].Value.Trim();
            string value = match.Groups["value"].Value.ToUpperInvariant();
            if (match.Groups["apply"].Success)
            {
                if (value.Length > 0) throw new FormatException("Apply uses VMC.SEL.Apply(A1); without an equals value.");
                commands.Add(new(true, ParseBus(args), SelAction.Toggle));
            }
            else
            {
                var action = value switch
                {
                    "" or "TOGGLE" => SelAction.Toggle,
                    "1" or "ON" => SelAction.On,
                    "0" or "OFF" => SelAction.Off,
                    _ => throw new FormatException("SEL accepts 1, 0, On, Off or Toggle.")
                };
                commands.Add(new(false, ParseBus(args), action));
            }
        }
        return commands;
    }
    private static int ParseBus(string text)
    {
        int bus = Array.FindIndex(MixerController.BusNames, name => name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
        return bus >= 0 ? bus : throw new FormatException("Bus names must be A1–A5 or B1–B3.");
    }
    // Parentheses and square brackets are accepted, including a dot before the opening bracket.
    [GeneratedRegex(@"^VMC\s*\.\s*SEL\s*(?:\.\s*(?<apply>Apply))?\s*\.?\s*(?<open>[\(\[])\s*(?<args>[^\(\)\[\]]*)\s*(?<close>[\)\]])\s*(?:=\s*(?<value>\w+))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Syntax();
}
