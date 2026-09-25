# Security

## Reporting a problem
Please **don't open a public issue** for security problems. Use GitHub's private reporting instead: **Security › Report a vulnerability** on this repository. Say what you found and how to reproduce it. You'll get an answer as soon as possible.

## How MyDeX protects you
- **No telemetry.** The only network request is the optional, anonymous update check to GitHub. Like any web request, GitHub sees your IP address.
- **Only trusted links.** MyDeX opens and downloads only `https://github.com/` URLs of the MyDeX or scrcpy repositories. Release files are served from GitHub's download servers. A scrcpy update is installed only if its SHA-256 matches GitHub's published checksum.
- **Only the bundled tools run.** MyDeX starts `scrcpy.exe` and `adb.exe` from its own folder, or a folder you choose, by full path. It never looks them up on PATH or in the current folder.
- **No shell commands built from input.** Every command MyDeX sends to the phone is a fixed string. Programs are started with separate arguments, never through a shell. App and file names from the phone or PC are passed only as single arguments.
- **Wi-Fi phones are verified.** A remembered Wi-Fi address is reconnected automatically only if the device there has the same hardware serial as your phone. DeX never starts on its own on an unverified Wi-Fi device.
- **Shareable logs.** The log file masks phone serials, wireless-debugging names, IP addresses, the Windows user name and user-folder paths.
- **No admin rights.** The installer is per-user and always installs into its own folder. Uninstall removes only MyDeX's files, and asks before deleting settings.

## What MyDeX can't protect against
MyDeX is built on Android's **USB debugging** (adb) and [scrcpy](https://github.com/Genymobile/scrcpy):
- Any PC you allow can fully control the phone. Only allow PCs you trust, and turn USB debugging off when you don't need it.
- The **quick Wi-Fi switch** (`adb tcpip`) is **not encrypted**, and makes the phone accept adb connections on the local network until it restarts. Other PCs still need your approval on the phone, but anyone on the same network could watch the traffic. Prefer **encrypted wireless debugging** (Android 11+). MyDeX switches the phone back to USB-only on *Disconnect Wi-Fi phones*, and when it closes.
- Copy & paste is shared between PC and phone unless you turn it off.
