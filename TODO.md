# MyDeX – TODO

Tick a box (`[x]`) when a feature is done **and** tested. ✅ = verified on the Galaxy S24 Ultra (One UI 8.5 / Android 16). 🧪 = covered by unit tests (`dotnet test tests\MyDeX.Tests`).

## 🧭 Easy to use (0.3)
- [x] **Home tab.** A status card says what's happening and what to do next, with one big Start/Stop button, *Mode* buttons (Gaming / Work / your own) and your apps as big buttons. ✅
- [x] **Apps tab.** The app list loads by itself, has a search box (Enter opens the first match), *★ Add to Home*, and an *Open windows* list with Close / Close all. ✅ 🧪
- [x] **Show my phone screen too.** Your normal phone screen opens in its own window, next to DeX. ✅ 🧪
- [x] **Drop files anywhere** on the MyDeX window to send them to the phone.
- [x] **Settings** grouped into sections (Picture, Sound & game controller, Recording, Starting & stopping, Advanced). The log is behind *Show log* and is also saved in `%AppData%\MyDeX\mydex.log`.
- [x] **One window plays the phone's sound.** Android captures the phone's sound as a whole, so extra windows are muted and their sound comes out of the DeX window. ✅ 🧪
  - [ ] Test that a game's sound is heard through the DeX window

## 🎮 Gaming
- [x] **1. Game controller support** (`--gamepad=uhid`). *Controller goes to:* the DeX window / app windows / off. 🧪 The controller is never sent to two places.
  - [ ] Test with an Xbox or PlayStation controller in a game that supports controllers
- [x] **2. Apps and games in their own window.** ✅ DeX does *not* take over their screen.
- [x] **3. Mouse capture for shooters** (`--mouse=uhid`, Left Alt frees it). 🧪
  - [ ] Test in a shooter
- [x] **4. Record gameplay** to `Videos\MyDeX`. ✅ The MP4 is finalized correctly. 🧪
- [x] **5. Phone heat and battery** in the window, status bar and tray, with a warning (default 42 °C). 🧪

## ⚡ Everyday use
- [x] **6. Modes (profiles).** Gaming, Work and your own; switch on Home, in Settings or from the tray. 🧪
- [x] **7. Keep apps open when DeX stops.** ✅ 🧪
- [x] **8. Ctrl+Alt+D** starts/stops DeX from anywhere. ✅ Tested by you.
- [x] **9. Auto-reconnect.** ✅ Works after a simulated drop (`adb reconnect`): DeX is back 1 s after the phone is.
  - [x] 0.3: waits 2 s after the phone reappears (a re-plugged phone isn't usable right away), and retries if the first restart fails.
  - [ ] **Test by pulling the real USB cable.** It didn't come back in 0.2; `mydex.log` now shows what happens.
- [x] **10. Fit my screen.** 🧪

## 🛠️ App polish
- [x] **11. Update check** for MyDeX and scrcpy, with a checksum-verified scrcpy update. 🧪
  - [ ] Publish MyDeX releases on GitHub so the MyDeX part has something to find
- [x] **12. Installer** (`scripts\Publish.ps1` → `dist\MyDeX-Setup-<version>.exe`). ✅ Install, upgrade and uninstall all tested.

## 🌍 Going public
- [x] Test data cleaned: fake phone serial; battery sample without the battery serial, first-use date or charging times
- [x] Fresh history: one clean commit signed with the GitHub no-reply address (the old history stays in a private archive repo)
- [x] Safety pass for users: clipboard option, encrypted Wi-Fi recommended, quick Wi-Fi switched off on exit, Wi-Fi phones verified by hardware serial, masked log file, GitHub-only links, no PATH lookups, safer uninstall, checksums (`dist\SHA256SUMS.txt`)
- [ ] Switch the repo to public on GitHub
- [ ] Publish a release with the installer and `SHA256SUMS.txt`

## 🐞 Known limitations / ideas
- Mouse clicks act as single touches, so multi-touch games are hard to play. Keyboard-to-touch key mapping is not in scrcpy.
- Some games and streaming apps block screen capture (black window). No capture tool can get around that.
- Each extra window is its own screen on the phone; very heavy games in several windows at once can heat the phone.
- Choosing your own keyboard shortcut (fixed to Ctrl+Alt+D for now)
- Recording can't be started or stopped in the middle of a session (a scrcpy limitation).
- App icons in the Apps list (scrcpy doesn't provide them)
