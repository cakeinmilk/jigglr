# Jigglr

**Jigglr** is a lightweight, zero-dependency C# Windows application designed to prevent your workstation from falling asleep or locking while you step away. It operates silently in the system tray and synthesises microscopic human input to keep your status "Active" in applications like Microsoft Teams, Slack, and Discord.

## Features

- **Zen Mode**: Taps a phantom 'F15' key rather than moving the physical mouse, preserving text selections and remaining completely invisible to the user.
- **Global Hotkey Registration**: Configure a system-wide hotkey to toggle Jigglr passively from anywhere in the OS without opening the window.
- **Timed Auto-Disable**: Tell Jigglr to stop spoofing activity automatically at a specific time (e.g., 17:30) so your machine can legitimately go to sleep overnight.
- **Smart Auto-Pause**: Automatically suspends itself if you explicitly lock your screen (Win+L), allowing corporate policies to engage cleanly, then resumes its last state upon unlock.

## Compilation

Jigglr is written in bare C# against the standard .NET Framework targeting `csc.exe` so it can be natively compiled on almost any modern Windows machine without requiring Visual Studio.

To compile Jigglr into a single portable executable:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -target:winexe -win32icon:JigglrIcon.ico -out:Jigglr.exe Main.cs
```
