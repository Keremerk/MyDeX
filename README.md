# MyDeX

**Samsung DeX in a window on your Windows PC**, the way *DeX for PC* used to work, for Galaxy phones on One UI 7 and newer, where Samsung removed it.

DeX runs on its **own screen**, not as a mirror of your phone. You can work or play in the DeX window on the PC while you keep using your phone normally.

## Quick start
1. Download `MyDeX-Setup-<version>.exe` from [Releases](../../releases) and install it. No admin rights are needed.
2. On your phone, turn on **Developer options**: tap *Settings › About phone › Software information › Build number* 7 times. Then turn on **Developer options › USB debugging**.
3. Plug the phone into the PC with a USB **data** cable and tap **Allow** on the phone.
4. Click **▶ Start DeX**.

## What you can do
- **Start/stop DeX in one click**, or with **Ctrl+Alt+D** from anywhere.
- **Modes:** switch between *Gaming* (smooth, 120 FPS) and *Work* (sharper, 1440p), or save your own.
- **Apps and games in their own windows**, next to DeX. Pin your favourites to the Home tab, or pick from **🕘 Recently used on your phone** (your last 50 apps).
- **Your normal phone screen** in a window too.
- **Game controller:** an Xbox or PlayStation controller on the PC works as a real gamepad on the phone. **Mouse lock** for shooters.
- **Wireless:** switch to Wi-Fi in one click, or pair with no cable at all (Android 11+).
- **Drop files** on the window to send them to the phone. Drop an `.apk` on the DeX window to install it.
- **Record** DeX or app windows to MP4.
- **Battery and heat** shown on the PC, with a warning when the phone gets hot.
- **Auto-reconnect** if the cable or Wi-Fi drops. **Tray icon**, start with Windows, auto-start when the phone connects.
- **Update check** for MyDeX and scrcpy, with a checksum-verified one-click scrcpy update.

## How it works
1. MyDeX asks [scrcpy](https://github.com/Genymobile/scrcpy) to create an extra, invisible screen on the phone (`--new-display`).
2. One UI treats that screen like an external monitor, so **DeX starts on it**.
3. scrcpy shows that screen in a window on the PC and sends your mouse, keyboard, clipboard and sound.

As a fallback, MyDeX can use Android's *Simulate secondary displays* setting instead. That also shows a small floating copy of DeX on the phone.

## Privacy & safety
**What MyDeX sends to the internet:** only the optional update check. It's an anonymous request to GitHub, with no account, token or settings. Like any website visit, GitHub sees your IP address. You can turn it off in *Settings › Advanced*. MyDeX only follows links to the MyDeX and scrcpy pages on `github.com`. A scrcpy update is downloaded from GitHub's release servers and installed only if its SHA-256 matches the checksum GitHub publishes for it.

**What stays on your PC:**
- Settings in `%AppData%\MyDeX\settings.json`.
- A log in `%AppData%\MyDeX\mydex.log`. Phone serial numbers, wireless-debugging names, IP addresses, your Windows user name and user-folder paths are **masked**. It still lists the phone model and the names of apps you opened, so have a quick look before you share it.
- Recordings, only if you turn them on, in `Videos\MyDeX`.
- Uninstalling asks whether to delete the settings and log too.
- Nothing is collected, and there's no telemetry.

**Things to be aware of:**
- **USB debugging gives the connected PC full control of the phone.** Only tap *Allow* on PCs you trust, and turn USB debugging off when you don't need it (*Developer options*).
- **Wi-Fi:** use it only on a network you trust, never public Wi-Fi.
  - **Encrypted wireless debugging** (Android 11+, *Wireless* tab) is recommended.
  - The **quick switch is not encrypted:** someone on the same network could see the screen and what you type. It also leaves the phone listening until it restarts. MyDeX switches it back to USB-only when you click *Disconnect Wi-Fi phones*, and when MyDeX closes (you can change this in *Settings › Privacy & safety*).
  - MyDeX reconnects to a remembered Wi-Fi phone by itself only after checking it's really your phone (same hardware serial). It never starts DeX on its own on a Wi-Fi device it hasn't confirmed.
- **Copy & paste is shared** between the PC and the phone, passwords included. Turn it off in *Settings › Privacy & safety* if you prefer.
- Files you send go to `Download/MyDeX` on the phone, where other apps with storage access can read them.
- **Check your download:** every release lists SHA-256 checksums (`SHA256SUMS.txt`). Compare them with `Get-FileHash .\MyDeX-Setup-<version>.exe`. The installer isn't code-signed, so Windows SmartScreen may warn about it.

Found a security problem? See [SECURITY.md](SECURITY.md).

## Good to know
- **Sound:** Android captures the phone's sound as a whole, so it plays through one window (usually DeX). The other windows stay muted.
- **Black window in a game or video app?** Some apps block screen capture. No capture tool can get around that.
- **Mouse clicks act as single touches**, so games that need several fingers at once are hard to play.
- Tested on a Galaxy S24 Ultra with One UI 8.5 / Android 16.
- Troubleshooting: *Settings › Advanced › Show log*, or `%AppData%\MyDeX\mydex.log`.

## Build it yourself
```powershell
.\scripts\Get-Scrcpy.ps1          # downloads the official scrcpy (includes adb) into tools\scrcpy, checksum-verified
dotnet build src\MyDeX            # or open MyDeX.sln in Visual Studio
dotnet test tests\MyDeX.Tests     # unit tests
.\scripts\Publish.ps1             # runs the tests, then builds dist\MyDeX (portable) and dist\MyDeX-Setup-<version>.exe
```
Requires the .NET 9 SDK. The installer also needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

See [TODO.md](TODO.md) for what's planned and what's tested.

## Credits
Built on [scrcpy](https://github.com/Genymobile/scrcpy) (Apache 2.0) and adb. Inspired by
[DX Manager](https://github.com/maze-mei/DX-Manager) and [dex-launcher](https://github.com/idanmos/dex-launcher).
Not affiliated with Samsung. Samsung and Samsung DeX are trademarks of Samsung Electronics.

MIT License – see [LICENSE](LICENSE).
