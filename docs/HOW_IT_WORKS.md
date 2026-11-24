# How BlitzBlock works

BlitzBlock runs against a user-installed Windows copy of Blitz. The setup checks for Blitz.exe, its Blitz product metadata, and resources/app.asar. It stores only the selected folder and per-user installation settings.

## Launch and filtering

1. The launcher reads the configured Blitz folder and starts Blitz with Chromium host-resolution rules for known external ad domains.
2. A private Chromium DevTools pipe connects Blitz to the local Node process. It uses inherited process handles instead of a remotely accessible debugging port.
3. The launcher registers CSS for Blitz desktop origins. It attaches on new execution contexts and keeps the stylesheet present if the page replaces its head content.
4. The CSS hides known ad components and promo media. It removes the corresponding grid column only when the known ad marker is present.

## Findings from Blitz 3.0.29

- Host rules reduced third-party ads. Blitz's own promo cards came from the same infrastructure as ordinary content, so network filtering alone left cards and empty layout space.
- The match page uses a right rail and a separate grid marker. The profile can reuse the promo card inside ordinary content.
- A normally launched instance can absorb the new launch. Opening BlitzBlock restarts a matching regular Blitz process if needed, so close or finish any active game first.
- The container and account UI are dynamic. Installing CSS on the initial load alone missed later render contexts and reloads.
- The app runs outside the Blitz install folder. Updates can replace Blitz safely from the launcher's perspective. A new app version can still change markup and require new selectors.

## Windows integration

Setup writes a per-user Installed Apps entry and Start shortcut. An optional desktop shortcut points to the installed launcher. The installed icon is produced locally from the Blitz executable icon by flipping and recoloring it. BlitzBlock tries to set the filtered Blitz window icon through Windows messaging. Taskbar grouping and pinned icons depend on Windows and Blitz. Blitz manages its own notification-area icon.

Uninstall targets known BlitzBlock files, its shortcuts and registry entries. Unrelated files in the chosen folder remain. The Blitz executable, game files, and Blitz user data are outside the uninstall scope.
