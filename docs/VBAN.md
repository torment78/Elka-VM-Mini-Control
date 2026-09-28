# VBAN Text commands

Configure the receiver through **Settings → VBAN Text…**. The sender must use
the selected UDP port and exact stream name. Send standard **VBAN Text packets**,
not raw UDP strings.

## SEL commands

~~~text
VMC.SEL(A1);
VMC.SEL(A1)=1;
VMC.SEL(A1)=On;
VMC.SEL(A1)=Toggle;
~~~

Each command selects A1 and keeps it selected. Substitute any bus from A1–A5
or B1–B3. **=0** or **=Off** on an active bus is rejected with guidance to Ctrl-click
its SEL in the app. Off on an inactive bus does nothing. VBAN SEL commands do
not enter master mode; MIDI and hotkeys have separate toggle behavior.

## Apply commands

~~~text
VMC.SEL.Apply(A2);
VMC.SEL.Apply[B3];
~~~

Apply copies the named bus's eight input submix levels to **its saved destination
list**, without changing which SEL is active. Configure the list by Ctrl-clicking
that bus's Apply button in the app. Do not include destinations in the command.

Both matching parentheses and square brackets are accepted. An extra dot before
the brackets is also supported, for example **VMC.SEL.(A1);**.

Separate multiple commands with semicolons:

~~~text
VMC.SEL(A2)=1;VMC.SEL.Apply(A2);
~~~

The selection is confirmed before Apply runs.

## VoiceMeeter MacroButtons

Configure an outgoing VBAN Text slot with the receiver's destination IP, UDP port
and stream name, then use the corresponding slot in SendText:

~~~text
SendText("vban1", VMC.SEL(A2););
SendText("vban1", VMC.SEL.Apply(A2););
~~~

Use press actions and leave release actions empty.

## Troubleshooting

- Confirm the receiver is enabled and that both Settings dialogs were saved.
- Check the destination IP, UDP port and exact stream name.
- If an allowed-sender address is configured, it must match the sending computer.
- Allow the configured UDP port through Windows Firewall when receiving over a LAN.
- Close settings and input-learning dialogs before sending commands.
- Check the app's status line for receiver errors and the last accepted command.

The receiver accepts ASCII, UTF-8 and UTF-16LE on VBAN Text channel 0. Wrong
streams, disallowed senders, malformed packets and unknown commands are ignored.
Duplicate or out-of-order frames are ignored during an active stream; after two
seconds without packets, a restarted sender can begin its frame count again.
