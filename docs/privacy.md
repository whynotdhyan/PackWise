# Privacy

PackWise is designed to work entirely on your PC.

## What PackWise never does

- It never requires an account, email address or sign-in.
- It never uploads or transmits file names, file contents, folder paths, hashes, passwords,
  duplicate-scan results, logs or usage data.
- It contains no telemetry, analytics, advertising or tracking code.
- It never checks for updates in the background.

The only network activity happens when you click a link in the **Help** menu or the **About**
dialog; those open in your default browser.

## What PackWise stores locally

Everything lives in `%LOCALAPPDATA%\Northstar Labs\PackWise`:

| Item | Contents | Control |
|---|---|---|
| `settings.json` | Preferences, window layout, recent archives, favourites, recent destinations | Settings › Restore defaults, File › Clear recent archives |
| `logs\` | One JSON Lines file per day: operation, path, outcome. Never passwords or contents. | Settings › Privacy: turn off, change retention, clear |
| `temp\` | Temporary copies of files opened from inside archives | Removed when PackWise closes; leftovers from a crash are removed on the next start |

## Passwords

Passwords you type are kept in memory so you aren't asked again for the same archive while
PackWise is open. They are never written to disk, logs or settings, and you can clear them at
any time in **Settings › Privacy › Forget passwords now**.

## Cloud files

The duplicate finder skips OneDrive (and similar) online-only placeholders by default. Reading
them would make Windows download every file, which PackWise avoids unless you turn the option off.
