# Elka VM Mini Control

A compact Windows desktop control for VoiceMeeter Potato, made by **Elka Soft**.
Two rows of eight buttons: orange SEL buttons above individual Apply buttons.
Bidirectional synchronization, saved per-bus destinations, global hotkeys, MIDI
learn and incoming VBAN Text. Uses the dark charcoal and teal palette from the existing
Elka VoiceMeeter FX Host app. No installer or separate API download.

Includes an Elka-style submix icon and a dark system-tray menu.

## Open and run

Open **Elka VM Mini Control.sln** in Visual Studio 2022 with the **.NET desktop
development** workload and .NET 8 SDK. Set `Elka.VM.Mini.Control` as the startup
project and press F5. The application targets Windows x64.

For a quick start, double-click **Run.cmd** in this folder. It builds and launches
the application. A Release executable is also produced at:

`src\Elka.VM.Mini.Control\bin\Release\net8.0-windows\Elka.VM.Mini.Control.exe`

The executable uses the .NET 8 Desktop Runtime. Keep its adjacent `.dll` and
`.json` files with it if you copy the build to another folder. VoiceMeeter Potato
must already be installed and running. This app automatically finds its installed
64-bit Remote API using the installation registry entry and standard VB folders;
it does not bundle or download the API, and has no API-path field.

## Startup and tray

Settings has three independent choices:

- **Start with Windows** launches the app when you sign in. Off by default.
- **Start in tray** starts with the window hidden, whether launched manually or
  by Windows. Off by default; this does not enable Windows startup.
- **Close to tray** makes the window's X hide it while the app keeps running.
  On by default. Turn this off if X should quit.

The notification icon is named **Elka VM Mini Control**. Right-click it for a
dark **Open / Exit** menu, or left-click to open the window. **Exit always quits**,
releases the API, MIDI, hotkeys and VBAN port, and removes the tray icon.
If the icon is in Windows' overflow area, look under the taskbar's hidden-icons
arrow and drag it onto the taskbar if desired. Windows controls that placement.

SEL synchronization, MIDI, hotkeys and VBAN continue while hidden. A hidden
startup initializes these inputs without briefly showing the window. Windows
shutdown/sign-out is allowed to exit normally.

Windows startup uses this app's entry in the current user's
`Software\Microsoft\Windows\CurrentVersion\Run` registry key and needs no
administrator access. Save after enabling or disabling the option. Keep the app
in a permanent folder; after moving it, run it from the new folder and save
Settings again to update the executable path. Disabling removes only this app's
entry. Windows' own Startup Apps settings can also disable automatic launching.

## SEL and Apply

1. Click **A1–A5** or **B1–B3** to select that bus's SEL. Click the active button
   again to turn it off. The app selects one source at a time.
2. Adjust the input submix in VoiceMeeter. Changes to SEL in VoiceMeeter are also
   reflected in this window, normally within 100 ms.
3. **Ctrl-left-click or Ctrl-right-click the Apply button beneath a bus** and
   tick its destination buses. Each of the eight Apply buttons saves its own
   independent checklist. Select all and Clear affect only that button's list.
4. Click that **Apply** button to copy its source bus's eight input levels to
   its saved destinations. For example, the Apply button beneath A2 always
   copies A2, regardless of the current SEL indicator. The app reads live levels
   when you press it and checks the destination levels after writing.

The source is excluded from its own destination list. Clicking an unconfigured
Apply button opens its checklist. Destination setup remains available when
VoiceMeeter is disconnected. Hover over Apply to see its full destination list
and input binding.

Apply copies `Strip[i].GainLayer[bus]` only. It does not copy input routing,
pan, mute, EQ, effects, bus master gain, or audio-device settings. No settings
are written to VoiceMeeter on startup. Copying is rejected while disconnected,
without saved destinations, or while a change is pending.
An unconfirmed copy reports an error rather than success.

## Hotkeys

Choose **Settings → Hotkeys**. **Right-click a button in either row** to assign
its shortcut. Ctrl-click also works on the SEL row; Ctrl-click on the Apply row
opens destinations instead. Press the key combination, then click **Save**. Use
**Clear** to remove a binding or **Cancel / Escape** to keep the current one.

Ctrl, Alt, Shift and Win can be combined, including double, triple and quadruple
modifiers. Shortcuts work while another app is focused. Windows-reserved
shortcuts and combinations already registered by another program are reported
as unavailable. Key repeat does not repeatedly toggle SEL. All shortcuts start
unassigned; assigning a shortcut does not press SEL.

## MIDI

Choose **Settings → MIDI input**, select a device, and save. **Right-click the
SEL or Apply button** you want to control to open **Learn MIDI**. Press the physical MIDI
button, then click **Save**. Learning does not activate the button.

Supported messages are **Note On/Off** and **Control Change**, on channels 1–16.
Use a momentary controller button: a nonzero value is a press, and note-off or
zero is a release. Holding a button triggers once; releasing rearms it. A MIDI
message can be assigned to only one button. Program Change, pitch bend, clock and
SysEx messages are ignored. If a MIDI device is removed, refresh and reselect it
in Settings. A device held exclusively by another program may not open.

Hotkeys and MIDI are selectable input modes; mouse control always remains
available. Both sets of assignments are kept when switching modes.

## Incoming VBAN Text

Open **Settings → VBAN Text…**, enable the receiver and configure its network
fields. Click **Use settings**, then **Save** in the main Settings window.
VBAN works alongside the selected hotkey/MIDI mode.

Every network field is editable and saved:

| Field | Initial value | Purpose |
| --- | --- | --- |
| Listen IP | `127.0.0.1` | Local interface to bind; use `0.0.0.0` for all interfaces or this PC's LAN IPv4 address. |
| UDP port | `6982` | Port the sender must target. Choose any available port from 1–65535. |
| Stream name | `Command1` | Must match the sender's VBAN stream name exactly; 1–16 ASCII characters. |
| Allowed sender IPv4 | Blank | Optional incoming IP filter; blank accepts any sender that can reach the listener. |

These are starting values, not fixed receiver settings. The receiver is initially
disabled. Port 6982 avoids the FX Host's default 6981; choose a different value
if another application uses it. For another computer, send to this PC's LAN IP
and allow this app's configured UDP port through Windows Firewall if required.

VBAN presses either row by bus name; destinations are configured **in the app**:

```text
VMC.SEL(A1);
VMC.SEL(A1)=1;
VMC.SEL(A1)=0;
VMC.SEL.Apply(A1);
VMC.SEL.Apply(A2);
VMC.SEL.Apply[B3];
```

- `VMC.SEL(A1);` toggles A1's top-row SEL button.
- `=1` / `=On` select it; `=0` / `=Off` deselect it if active; `=Toggle` toggles.
- `VMC.SEL.Apply(A2);` presses A2's bottom-row Apply button, copying **A2's**
  live levels to **A2's saved destinations**. No routing list belongs in the
  incoming command. It does not change which SEL is active.
- Both matching parentheses and square brackets work. An extra dot before the
  brackets is also accepted, for example `VMC.SEL.(A1);`.
- Multiple commands can share one packet, separated with semicolons. For example:
  `VMC.SEL(A2)=1;VMC.SEL.Apply(A2);`. Selection is confirmed before Apply runs.

For VoiceMeeter MacroButtons, configure an outgoing VBAN Text slot with the
same destination port and stream name, then use:

```text
SendText("vban1", VMC.SEL(A2););
SendText("vban1", VMC.SEL.Apply(A2););
```

Use press actions for toggles and Apply; leave release actions empty. The app
receives standard VBAN Text packets (ASCII, UTF-8 or UTF-16LE, text channel 0),
not raw UDP strings. The status line reports receiver errors and the last accepted
command. Wrong streams, disallowed sender IPs, unknown commands, and malformed
packets are ignored. Duplicate/out-of-order frames from the same sender are
ignored during an active stream; two seconds of inactivity allows a restarted
sender's counter to begin again. Commands are ignored while a settings or input
learning dialog is open. VBAN Text carries commands only; this app does not
receive or route VBAN audio.

## Saved preferences

Input mode, MIDI device, bindings for both rows, eight destination profiles,
startup/tray preferences and all VBAN receiver fields are saved
to `%LOCALAPPDATA%\ElkaSoft\ElkaVMMiniControl\settings.json`. Mixer levels are
always read from VoiceMeeter rather than saved by this app.
The original eight SEL bindings are retained on upgrade. The former single
destination checklist is migrated to each Apply profile, excluding its own source.

## Verification

```powershell
dotnet build "Elka VM Mini Control.sln" -c Release
dotnet run --project tests/Elka.VM.Mini.Control.Checks -c Release -- artifacts/checks
```

The dependency-free checks cover copying and error paths, external SEL changes,
disconnect/reconnect, MIDI presses/releases, binding persistence, Windows global
hotkey registration/conflicts, per-button profile isolation, settings migration,
and WPF controls. VBAN tests send actual loopback UDP packets using a custom
IP, port and stream into a simulated mixer, checking command order and filtering.
Tray checks cover hidden startup, background SEL/hotkey/VBAN operation, restoring
the window, close/exit behavior, and resource release. Startup registration checks
use an isolated temporary registry key; they do not enable Windows startup.
The checks render the main window, settings, hotkey, MIDI and VBAN dialogs into
`artifacts/checks`.

Optional **read-only** verification of your installed API:

```powershell
dotnet run --project tests/Elka.VM.Mini.Control.Checks -c Release -- --live-probe
```

This lists live SEL and gain-layer values plus MIDI device names, without
writing mixer parameters. A physical controller and real keyboard presses
should also be tried with your chosen bindings.

## API references

- [VB-Audio Remote API documentation](https://download.vb-audio.com/Download_CABLE/VoicemeeterRemoteAPI.pdf)
- [Windows RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Windows MIDI input](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinopen)
- [VB-Audio VBAN protocol specification](https://vb-audio.com/Voicemeeter/VBANProtocol_Specifications.pdf)
- [Windows notification icons](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon)
- [Windows startup registry entries](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)

The API session logs in once and survives VoiceMeeter restarts, then logs out on
exit. The app uses `Bus[i].Sel` for submix selection; `Bus[i].Monitor` controls a
different monitoring function and is not changed.
