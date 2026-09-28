# Development

## Build and run from source

Open **Elka SEL Mini Control.sln** in Visual Studio 2022 with the **.NET desktop
development** workload and .NET 8 SDK. Set **Elka.SEL.Mini.Control** as the startup
project and press F5. The application targets Windows x64.

Alternatively, run **Run.cmd** from the repository root. It builds Release and
launches the app. The source build requires the .NET 8 Desktop Runtime.

~~~powershell
dotnet build "Elka SEL Mini Control.sln" -c Release
~~~

The executable is produced at:

    src\Elka.SEL.Mini.Control\bin\Release\net8.0-windows\Elka.SEL.Mini.Control.exe

Keep its adjacent DLL and JSON files with it. Close a running source build before
rebuilding, or pass **--artifacts-path artifacts/build** to build in another folder.

## Checks

~~~powershell
dotnet run --project tests/Elka.SEL.Mini.Control.Checks -c Release -- artifacts/checks
~~~

The dependency-free checks cover submix copying, faders and readback, Direct
Input isolation, source changes, SEL safety, MIDI press/release behavior, global
hotkeys, settings migration and WPF controls. VBAN checks send loopback UDP
packets to a simulated mixer. Tray and single-instance checks cover hidden
startup, activation and resource cleanup.

Mixer writes use a simulated API. Startup checks use isolated temporary registry
keys. Screenshots of the app and dialogs are rendered into **artifacts/checks**.

For optional read-only verification of the installed API:

~~~powershell
dotnet run --project tests/Elka.SEL.Mini.Control.Checks -c Release -- --live-probe
~~~

The probe lists live SEL state, input labels, gain-layer values and MIDI device
names without writing mixer parameters.

## API and compatibility

The app discovers the installed 64-bit VoiceMeeter Remote API through the
installation registry entry and standard VB folders. It logs in once, retains
the session across VoiceMeeter restarts, and logs out on exit.

- Bus[i].Sel controls submix selection. Bus[i].Monitor is not changed.
- Apply and Direct Input write only Strip[i].GainLayer[bus].
- Input names come from Strip[i].Label through the Unicode string API.
- API writes are queued, so changes are verified by subsequent readback.
- Windows startup uses the app's entry in the current user's Run registry key.
- The single-instance identity is shared with the former Elka VM Mini Control
  name so version 0.5.0 and renamed versions cannot run together.

## References

- [VB-Audio Remote API](https://download.vb-audio.com/Download_CABLE/VoicemeeterRemoteAPI.pdf)
- [VBAN protocol specification](https://vb-audio.com/Voicemeeter/VBANProtocol_Specifications.pdf)
- [Windows RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Windows MIDI input](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinopen)
- [Windows notification icons](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon)
- [Windows startup registry entries](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
