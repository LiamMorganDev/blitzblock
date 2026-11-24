# Developer handoff

## What ships

BlitzBlock is a single-file Windows x64 installer and launcher. The user installs Blitz separately. This repo and its release assets do not include Blitz program files or private app data.

| Where | What to change |
| --- | --- |
| src/BlitzLauncher.cs | Installer and maintenance operations, per-user settings, shortcut and Installed Apps registration, Windows process launch |
| src/SetupWizard.cs | The two-step setup and change/uninstall screens |
| src/BlitzBlockTaskbar.cs | Windows taskbar identity, shortcut identity, and image-only updates to Blitz's existing tray entry |
| src/BlitzBlockUpdate.cs | GitHub release selection, SHA-256 and version checks, and update handoff |
| src/open.ps1 | Start the bundled runtime and recover from an already-running unfiltered Blitz instance |
| src/blitz.cjs | Ad host rules, renderer styles, header label and version, reload checks |
| build.ps1 and tests/Check-Update.ps1 | Build the installer and test updater eligibility and checksums |

See [architecture and findings](HOW_IT_WORKS.md) and the [Blitz compatibility checklist](MAINTENANCE.md).

## Build and smoke test

From the repository root on Windows x64:

~~~powershell
powershell -NoProfile -File .\build.ps1
powershell -NoProfile -File .\tests\Check-Update.ps1 -Setup '.\build\BlitzBlock Setup.exe'
git diff --check
~~~

The build downloads a pinned official Node.js archive, validates its SHA-256, checks JavaScript and PowerShell syntax, and compiles the GUI EXE with the Windows .NET Framework compiler. Use build.ps1 -NodeArchive to reuse the verified archive. Everything under build/ is generated.

Run setup in a separate test destination. Check default and custom Blitz paths, a bad path, Start launch, optional desktop shortcut, installed version, and uninstall via Windows Installed Apps. Confirm uninstall leaves a deliberately placed unrelated file behind. Run normal Blitz separately when checking that a filter or icon change is limited to BlitzBlock sessions.

For filter smoke tests, set BLITZ_TEST_TRACE to a temporary file before launching the Start shortcut. The launcher can record checks for the header, hidden ad rail, and reload. It reloads the page during this test, so use the flag only in a deliberate QA session. Keep this file and raw screenshots local.

## Publish a release

1. Update AssemblyVersion in src/BlitzLauncher.cs and add a CHANGELOG.md entry. A GitHub tag of v1.2 normalizes to assembly version 1.2.0.0. A tag of v1.2.1 maps to 1.2.1.0.
2. Build and test the exact installer you will upload. Record the tested Blitz version and the installer SHA-256 in the release notes.
3. Commit and push the code, then publish a stable release from the tested commit. Preserve the exact asset name BlitzBlock.Setup.exe.

~~~powershell
Copy-Item -LiteralPath '.\build\BlitzBlock Setup.exe' -Destination '.\build\BlitzBlock.Setup.exe'
$commit = (git rev-parse HEAD).Trim()
gh release create vX.Y '.\build\BlitzBlock.Setup.exe' --target $commit --latest --notes-file '<notes-file>'
gh api repos/LiamMorganDev/blitzblock/releases/latest --jq '{tag: .tag_name, name: .assets[0].name, digest: .assets[0].digest}'
~~~

4. Compare GitHub's published digest with Get-FileHash. Quit the prior installed build completely and reopen BlitzBlock. Check Installed Apps and both version labels, then reload Blitz and confirm the filter and icons still work.
5. Keep prior releases and publish a higher version if a fix is needed. The updater skips older tags, drafts, prereleases, missing hashes, and invalid downloads. Users of the original v1 need a manual update once. If setup reports a lock, quit Blitz from the tray before retrying.

The release installer is unsigned. HTTPS and the GitHub asset digest verify the downloaded bytes against the published release. SmartScreen can still warn about publisher reputation.

## Diagnose a regression

- **Promos return.** Confirm a filtered launch rather than an already-running standalone Blitz process. Check the local renderer origin, route, promo asset path, and container before changing CSS. On reload, check that the stylesheet attaches again.
- **Purple window icon but red taskbar button.** Read the actual window and shortcut AppUserModelID. Both should be LiamMorganDev.BlitzBlock. The window relaunch command should point to the installed launcher, and its icon resource should point to BlitzBlock.ico. An old Blitz pin keeps its separate identity. Pin BlitzBlock from Start to use the new identity.
- **Tray stays red.** Blitz 3.0.29.401 was verified with the Electron_NotifyIconHostWindow class and notification ID 3. Confirm the window belongs to the configured Blitz process, then check the existing entry with Shell_NotifyIconGetRect before changing the ID. Use NIM_MODIFY with NIF_ICON only. The existing tooltip, menu, and callbacks belong to Blitz.
- **Versions look stale.** The displayed BlitzBlock value comes from the installed assembly via BLITZBLOCK_VERSION. Check the executable selected by the shortcut, not just the setup EXE in the build folder.
- **Auto-update skips a release.** Check the latest stable GitHub tag, exact asset name and digest, and assembly version in the uploaded EXE. The installed launcher deliberately opens the current build if the update cannot be verified.

The v1.2 release was tested by auto-updating an installed v1.1 from GitHub, reading the taskbar identity from the live window, and checking the version labels and promo filter after reload.

The current baseline is Blitz desktop **3.0.29.401** (stable **3.0.29**) on Windows x64. The tray path depends on Electron implementation details. Recheck it after vendor updates. Never publish account names, live match records, local paths, tokens, or raw diagnostic logs.
