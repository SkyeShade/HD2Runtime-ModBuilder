using System.Diagnostics;
using HD2RuntimeModBuilder.Updater;

// HD2Runtime ModBuilder updater. Started by the app from a verified, staged release (never from the installation folder):
//   HD2RuntimeModBuilder.Updater.exe --pid <id> --started <ticks> --install <dir> --staged <dir> --version <x.y.z> --result <file>
// It waits for the app to exit, replaces the application files, writes the result for the next launch and restarts the app.
UpdaterOptions options;
try { options = UpdateContract.Parse(args); }
catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { Console.Error.WriteLine(e.Message); return 2; }

var installer = new UpdateInstaller(psi => Process.Start(psi)?.Dispose(), WaitForExit);
return installer.Run(options).Success ? 0 : 1;

static bool WaitForExit(int pid, long startTicks, TimeSpan timeout)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        // A different process that reused the ID means the app has already exited.
        if (process.StartTime.ToUniversalTime().Ticks != startTicks) return true;
        return process.WaitForExit(timeout);
    }
    catch (ArgumentException) { return true; }
    catch (InvalidOperationException) { return true; }
}
