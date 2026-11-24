# Build and maintain BlitzBlock

## Build

Use Windows x64, Windows PowerShell or PowerShell 7, and the Windows .NET Framework compiler. In the repository root, run:

    powershell -NoProfile -File .\build.ps1

The script downloads a pinned official Node.js archive, verifies its SHA-256, checks the launcher scripts, and compiles **build/BlitzBlock Setup.exe**. Use -NodeArchive to supply the same archive from disk. Do not commit the build folder.

## When a Blitz update breaks filtering

1. Record the old and new Blitz versions. Restart with BlitzBlock and check the launcher is not reusing an unfiltered session.
2. Reproduce with the profile, match page, and any affected route. Compare before and after captures using the same viewport, route, and app state.
3. Identify the smallest promo container in the renderer. Confirm its class, promo asset path, or dedicated ad grid marker before editing the selectors in src/blitz.cjs. Keep ad rules scoped to a known desktop origin and avoid selectors that hide unrelated content or controls.
4. If network filtering needs an update, add only dedicated ad hosts. Shared Blitz hosts also serve content.
5. Rebuild and test a cold launch, navigation, and reload. Verify rank, match history, and layout around ads. If you have a game available, check overlays too.
6. Test install and uninstall in a throwaway location. Exercise a missing Blitz folder, a real custom folder, the desktop shortcut on and off, the Start shortcut, and Settings → Apps → Installed apps. Keep an unrelated file in the test install folder and confirm it survives uninstall.
7. Publish the tested setup EXE with a release note naming the Blitz versions you actually exercised. Include no Blitz binaries, local profiles, raw account screenshots, or logs with personal data.

Prefer fixing the local launcher. Keep the vendor app and updater enabled. If Blitz relaunches itself normally after an update, open BlitzBlock again.
