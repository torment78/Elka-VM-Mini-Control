using System.Diagnostics;
using System.IO;
using System.Windows;
using Elka.VM.Mini.Control;
using Elka.VM.Mini.Control.Core;
using Elka.VM.Mini.Control.Services;

internal static partial class Program
{
    private static Process InstanceChild(string mode, string identity)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(mode); start.ArgumentList.Add(identity);
        return Process.Start(start)!;
    }

    private static int InstanceProbe(string[] args)
    {
        using var guard = new SingleInstance(args[1]);
        Console.WriteLine(guard.IsPrimary ? "PRIMARY" : "SECONDARY");
        Console.Out.Flush();
        if (args[0] == "--instance-hold")
        {
            Console.ReadLine();
            Environment.Exit(0); // Deliberately abandon the mutex, like a crashed app.
        }
        if (!guard.IsPrimary) guard.RequestActivation();
        return 0;
    }

    private static string FinishInstanceChild(Process child)
    {
        if (!child.WaitForExit(10000)) throw new InvalidOperationException("Instance check child timed out.");
        string output = child.StandardOutput.ReadToEnd().Trim();
        if (child.ExitCode != 0) throw new InvalidOperationException(child.StandardError.ReadToEnd());
        return output;
    }

    private static void SingleInstanceChecks()
    {
        string identity = @"Local\ElkaSoft.MiniControlChecks." + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstance(identity))
        {
            Check(primary.IsPrimary, "First process owns the version-independent single-instance lock");
            using var child = InstanceChild("--instance-probe", identity);
            Check(FinishInstanceChild(child) == "SECONDARY", "A duplicate process cannot become a second mixer owner");
            using var activated = new ManualResetEventSlim();
            primary.Listen(activated.Set);
            Check(activated.Wait(5000), "A second launch during startup is retained until the primary listens");
            activated.Reset();
            using var repeated = InstanceChild("--instance-probe", identity);
            Check(FinishInstanceChild(repeated) == "SECONDARY" && activated.Wait(5000), "Repeated launches signal the same owner without taking its lock");
        }
        using (var afterExit = new SingleInstance(identity))
            Check(afterExit.IsPrimary, "Clean exit releases the lock so another version can start");

        using (var crash = InstanceChild("--instance-hold", identity))
        {
            Check(crash.StandardOutput.ReadLine() == "PRIMARY", "Crash fixture owns an isolated instance lock");
            using var blocked = new SingleInstance(identity);
            Check(!blocked.IsPrimary, "The lock remains exclusive while the owner is alive");
            crash.StandardInput.WriteLine("crash"); crash.StandardInput.Flush();
            FinishInstanceChild(crash);
            using var recovered = new SingleInstance(identity);
            Check(recovered.IsPrimary, "An abandoned lock recovers after a crash without a stale lock file");
        }
        Check(SingleInstance.IsLegacyVersion("0.4.0-dev.2+abc") && SingleInstance.IsLegacyVersion("0.5.0-dev.1+abc")
            && !SingleInstance.IsLegacyVersion("0.5.0+abc") && !SingleInstance.IsLegacyVersion("0.5.1+abc"),
            "Upgrade detection distinguishes older builds from versions with single-instance support");
    }

    private static void SingleInstanceUiChecks(string output)
    {
        using var guard = new SingleInstance(@"Local\ElkaSoft.MiniControlChecks." + Guid.NewGuid().ToString("N"));
        var fake = new FakeRemote(); fake.Select(1);
        var store = new SettingsStore(Path.Combine(output, "single-instance-settings.json"));
        store.Save(new ControlSettings { StartInTray = true, CloseToTray = true });
        var window = new MainWindow(new MixerController(fake), store, new FaderTestStartup());
        using var tray = new TrayService(window, () => true);
        try
        {
            window.Start(hidden: true);
            int writes = fake.Scripts.Count;
            guard.Listen(() => window.Dispatcher.BeginInvoke(new Action(tray.Open)));
            guard.RequestActivation();
            PumpUntil(() => window.IsVisible);
            Check(window.IsVisible && fake.Scripts.Count == writes, "Duplicate activation restores a hidden window without mixer writes");
            window.WindowState = WindowState.Minimized;
            guard.RequestActivation();
            PumpUntil(() => window.WindowState == WindowState.Normal);
            Check(window.WindowState == WindowState.Normal, "Duplicate activation also restores a minimized window");
        }
        finally { tray.Exit(); }
    }
}
