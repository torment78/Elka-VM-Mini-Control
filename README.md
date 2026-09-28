# Elka SEL Mini Control

**for VoiceMeeter · Made by Elka Soft**

![Elka SEL Mini Control for VoiceMeeter](docs/banners/horizontal-voicemeeter-under-1mb.jpg)

Control VoiceMeeter Potato's input submixes from a compact desktop window.
Select a bus, adjust its input levels, and copy or link those levels to other
buses. Mouse controls, global hotkeys, MIDI and incoming VBAN Text are supported.

## Get started

1. Install and start **VoiceMeeter Potato** on Windows x64.
2. Download the ZIP from the [dev release](https://github.com/torment78/Elka-SEL-Mini-Control/releases/tag/v0.5.2)
   and extract the entire archive into a permanent folder.
3. Run **Elka.SEL.Mini.Control.exe**.

The ZIP includes the .NET runtime. The app finds VoiceMeeter's installed Remote
API automatically; no separate API download or path setup is needed. This is a
portable release without an installer. Keep the extracted files together.

Only one copy runs per Windows desktop session. Launching it again restores the
running window. To switch versions, choose **Exit** from the current app's tray
menu first. Existing settings are carried over when upgrading.

## Find your controls

| Control | Where | What it does |
| --- | --- | --- |
| SEL A1–A5 / B1–B3 | Top button row | Selects the bus whose input submix you want to edit. |
| Apply | Beneath each SEL | Copies that bus's eight input levels to its saved destinations. |
| Direct Input | Lower right, above the Elka Soft credit | Automatically sends changed input levels to the active SEL's saved destinations. Blue means on; gray means off. |
| Input faders | Beneath the Apply row when Fader mode is enabled | Displays and adjusts the selected bus's eight input levels. |
| Settings | Upper right | Configures Fader mode, input controls, VBAN, startup and tray behavior. |

**Compact mode**

![Compact view with SEL, Apply and Direct Input](docs/images/compact.png)

**Fader mode** — enable **Settings → Fader mode → Save**.

![Fader view with named inputs and live levels](docs/images/fader-mode.png)

## Select and edit a submix

Click **A1–A5** or **B1–B3** in the SEL row. The active SEL lights orange.
Adjust its input levels in VoiceMeeter or with the app's input faders. Changes
made in either app appear in the other, including SEL changes.

A normal mouse click on the active SEL keeps it selected. Clicking another SEL
switches to that bus's mix.

### Master mode and SEL safety

**Ctrl-left-click the active SEL** to turn all SELs off and enter master mode.
A normal click on any SEL returns to that bus's submix. Ctrl-click on an inactive
SEL selects it.

**When every SEL is off, VoiceMeeter's own input fader can change levels across
all bus submixes.** Select a SEL before editing only one bus's mix. The app shows
master mode in its status area and pauses its input faders and Direct Input.

MIDI and hotkeys toggle the assigned SEL on/off without the mouse safeguard.
Pressing the active bus's assigned control can therefore enter master mode.
Changes made directly in VoiceMeeter or by another controller are also reflected
as received.

On startup or reconnection, an existing SEL is preserved. If none is selected,
the app chooses A1 initially or the last selected bus from the current session.

![Master mode with input faders disabled](docs/images/master-mode.png)

## Apply levels to other buses

Each Apply button has its own saved destination list.

1. **Ctrl-left-click or Ctrl-right-click Apply** beneath the source bus.
2. Tick the destination buses. **Select all** and **Clear** affect only this list.
3. Adjust the source bus's input mix.
4. Click its **Apply** button to copy all eight input levels to those destinations.

For example, **Apply beneath A2 always copies A2**, regardless of which SEL is
currently active. The source bus is excluded from its own destination list.
An unconfigured Apply button opens its checklist. Hover over Apply to see its
destinations and assigned input control.

Apply copies **input submix levels only**. Routing, pan, mute, EQ, effects, bus
master levels and audio-device settings stay unchanged. If the copy cannot be
confirmed, the app reports an error; check the destination levels before retrying.

## Direct Input

Use **Direct Input** at the lower right to link live input-level changes to
other buses. **Blue means enabled; gray means off.** The setting is saved.

1. Select a source SEL.
2. Ctrl-click that source's Apply button and choose its destinations.
3. Enable Direct Input and adjust the source's input levels in VoiceMeeter or
   in the app's fader panel.

For example, with **A2** selected and **A4, A5 and B1** checked beneath A2,
moving input 3 on A2 sets input 3 to the same level on those three destinations.
Other inputs stay as they are. Use Apply when you want to copy all eight levels.

Direct Input follows the currently selected bus and that bus's saved destination
list. **An empty destination list sends no copies.** Only input submix levels
are linked, and destination changes do not feed back into the source.

Enabling Direct Input, switching SEL or changing destinations does not immediately
copy the current mix; subsequent level changes are followed. Linking continues
while the app is in the tray. It pauses without a single selected SEL, during
Apply, and while settings or input-learning dialogs are open.

If an error pauses linking, resolve the reported problem, then turn Direct Input
off and on. Manual Apply remains available while Direct Input is off.

## Input faders

Enable **Settings → Fader mode → Save** to show the input faders beneath the
Apply row. Disable it to return to compact mode.

The eight faders correspond to **IN 1–IN 5, VAIO, AUX and VAIO 3**. Each shows
its current VoiceMeeter channel name and level. Unnamed channels use their input
label. Switching SEL displays that bus's levels without copying the previous mix.

- Drag a fader to adjust its input level, from **−60 to +12 dB**.
- Use the arrow keys for **0.1 dB** adjustments.
- Double-click a fader to reset it to **0 dB**.

With Direct Input enabled, the selected bus's saved destinations follow these
edits. Faders are disabled without a single selected SEL, while disconnected,
or while an operation is pending. If SEL changes during a drag, release the
fader and start a new drag on the intended bus.

## Hotkeys

Choose **Settings → Hotkeys → Save**. **Right-click a SEL or Apply button**,
press the desired key combination, then click **Save**. Use **Clear** to remove
a binding or **Cancel / Escape** to keep it unchanged.

Ctrl, Alt, Shift and Win can be combined. Shortcuts work while another app is
focused, and holding a key triggers only once. Windows-reserved combinations
or shortcuts already used by another program are reported as unavailable.

A SEL hotkey toggles its bus on/off; an Apply hotkey copies that bus's levels
to its saved destinations. The mouse Ctrl-click safeguard does not apply to
assigned hotkeys, including combinations containing Ctrl.

## MIDI

Choose **Settings → MIDI input**, select a device, and save. **Right-click a
SEL or Apply button** to open **Learn MIDI**. Press the controller button you
want to assign, then click **Save**. Learning does not activate the control.

Use momentary **Note On/Off** or **Control Change** buttons on MIDI channels
1–16. A nonzero value is a press; note-off or zero is a release. Holding a
button triggers once, and releasing it rearms the next press. Each MIDI message
can be assigned to one control.

A SEL MIDI button toggles its bus on/off without the mouse safeguard. An Apply
MIDI button copies its source bus to its saved destinations. If the device is
removed, refresh and reselect it in Settings. A device held exclusively by
another program may be unavailable.

Hotkeys and MIDI are selectable input modes. Both sets of assignments are kept
when switching modes, and mouse control remains available.

## Incoming VBAN Text

Open **Settings → VBAN Text…**, enable the receiver and configure its fields.
Click **Use settings**, then **Save** in the main Settings window. VBAN can run
alongside either hotkeys or MIDI.

| Setting | Default | Purpose |
| --- | --- | --- |
| Listen IP | 127.0.0.1 | Local interface. Use 0.0.0.0 for all interfaces or this PC's LAN IPv4 address for network control. |
| UDP port | 6982 | Port the sender targets. Change it if another app uses it. |
| Stream name | Command1 | Must match the sender exactly; 1–16 ASCII characters. |
| Allowed sender IPv4 | Blank | Optional sender filter. Blank accepts any sender that can reach the receiver. |

All fields are editable and saved. The receiver starts disabled. For another
computer, send to this PC's LAN address and allow the configured UDP port through
Windows Firewall if required.

Commands address either button row by bus name. Apply destinations are configured
in the app, so they do not need to be included in the command:

~~~text
VMC.SEL(A2);
VMC.SEL.Apply(A2);
~~~

SEL commands select the bus and keep it selected. Off commands for an active bus
are rejected with guidance to use Ctrl-click in the app. Apply uses the named
bus's saved destinations without changing the active SEL.

See the [VBAN command guide](docs/VBAN.md) for supported syntax and VoiceMeeter
MacroButtons examples. VBAN Text controls the app; it does not carry audio here.

## Startup and tray

Settings provides three independent options. Click **Save** after changing them.

| Option | Behavior | Default |
| --- | --- | --- |
| Start with Windows | Launches the app when you sign in. | Off |
| Start in tray | Starts with the window hidden. Does not enable Windows startup. | Off |
| Close to tray | The window's X hides it while controls keep running. Disable this if X should quit. | On |

Find **Elka SEL Mini Control** in the system tray. Left-click it to open the
window, or right-click for **Open / Exit**. **Exit fully quits the app.** If its
icon is hidden, look under the taskbar's hidden-icons arrow.

SEL synchronization, MIDI, hotkeys, VBAN and Direct Input continue while hidden.
Keep the app in a permanent folder. After moving it, launch it from the new
location and save Settings again to update its Windows startup path.

## Saved settings and upgrades

Bindings, Apply destinations, input mode, MIDI device, VBAN settings, Fader mode,
Direct Input and tray preferences are saved in:

    %LOCALAPPDATA%\ElkaSoft\ElkaSELMiniControl\settings.json

Mixer levels and channel names remain in VoiceMeeter.

When upgrading from **Elka VM Mini Control**, previous settings are imported
automatically if the new settings file does not exist. The old file is retained
as a backup. An existing Windows startup entry is updated to the new executable.
Existing VMC.SEL(...) and VMC.SEL.Apply(...) commands keep working.

## Development

For Visual Studio setup, building from source, checks and API references, see
the [developer guide](docs/DEVELOPMENT.md).
