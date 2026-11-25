# Releasing Desk Wheel

For maintainers: how Desk Wheel is signed, published and updated.

## How people get it

Windows and macOS only fully trust apps signed by someone they recognise. Desk Wheel uses the free way on each:

| | How people get it | Trusted? | Cost |
| --- | --- | --- | --- |
| **Windows** | **Microsoft Store** (main route) | Yes: Microsoft signs it. No Smart App Control or SmartScreen prompts; the Store installs updates | Free |
| Windows | Installer from the website | Blocked by Smart App Control unless signed with a code-signing certificate | Certificate ~US$130–300/yr (optional) |
| **macOS** | DMG from the website | macOS asks once (System Settings › Privacy & Security › Open Anyway); the DMG explains how | Free |
| macOS | DMG, notarized | Opens with no prompt | Apple Developer Program, US$99/yr (optional, later) |

## Microsoft Store (free, fully trusted)

1. **Register** at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/home) with a personal Microsoft account: free for individuals, with an ID-and-selfie check.
2. In **Partner Center**, the app **Desk Wheel** is reserved. Its identity is built into `build.ps1`, so nothing needs setting:

   | Product identity | Value |
   | --- | --- |
   | Package/Identity/Name | `irfanaslam.io.DeskWheel` |
   | Package/Identity/Publisher | `CN=E17FA43C-D005-44B0-BA35-1A38B8D60FD6` |
   | Package/Properties/PublisherDisplayName | `irfanaslam.io` |
   | Package family name | `irfanaslam.io.DeskWheel_69qks97mj3kmy` |
   | Store ID | `9PF534B6ZW4Z` |

3. Store page: [apps.microsoft.com/detail/9PF534B6ZW4Z](https://apps.microsoft.com/detail/9PF534B6ZW4Z) (live once the first submission is certified); in Windows, `ms-windows-store://pdp/?productid=9PF534B6ZW4Z` opens it in the Store app. To build for another identity, set `WHEEL_MSIX_NAME`, `WHEEL_MSIX_PUBLISHER` and `WHEEL_MSIX_PUBLISHER_NAME` (or the Actions variables `MSIX_NAME`, `MSIX_PUBLISHER`, `MSIX_PUBLISHER_NAME`).
4. Build: GitHub Actions produces `DeskWheel-<version>-win-x64.msix` and `-win-arm64.msix` (artifacts *DeskWheel-win-…-store*), or run `.\build.ps1 -Msix` on a PC with the Windows SDK (`winget install Microsoft.WindowsSDK.10.0.26100`).
5. In Partner Center, start a submission: upload both `.msix` files under *Packages*, then fill in the Store listing (description, screenshots, the icon from `platform/icons`), age rating and the privacy policy link (the app can send anonymous usage counts when allowed, see [Usage statistics](#usage-statistics-google-analytics) below). Microsoft certifies and signs it, usually within a few days.
6. For each new version: bump `<Version>`, build, and upload the new `.msix` files in a new submission. People get it automatically from the Store.

The Store version is the same app with a few Store-style differences: the Store installs updates (Desk Wheel's own updater is off), "start when I sign in" is a Windows startup task (Settings › Apps › Startup), and the Explorer *Send to* entry isn't offered. To try a package locally, turn on **Developer Mode** and run `Add-AppxPackage -Register dist\msix-win-x64\AppxManifest.xml`.

## Website downloads

`./build-mac.sh --dmg` makes the DMG; `.\build.ps1 -Installer` makes `dist\DeskWheel-<version>-win-x64-setup.exe` (Inno Setup: `winget install JRSoftware.InnoSetup`), which installs for the current user without an administrator prompt.

**Windows signing (optional, paid).** With a code-signing certificate, the website installer is trusted too. Set `WHEEL_SIGN_PFX` and `WHEEL_SIGN_PASSWORD`, or `WHEEL_SIGN_COMMAND` for a cloud signing service (see the top of `build.ps1`). Every `.exe` and `.dll` that ships is signed, plus the installer, Inno Setup's helper that runs from the temporary folder and the uninstaller.

**Mac notarization (optional, US$99/yr).** With an Apple Developer account:

```bash
# once: store your notarization login in the keychain
xcrun notarytool store-credentials wheel-notary --apple-id you@example.com --team-id TEAMID
# each release
export WHEEL_SIGN_ID="Developer ID Application: Your Name (TEAMID)"
export WHEEL_NOTARY_PROFILE=wheel-notary
./build-mac.sh --dmg        # → dist/DeskWheel-<version>.dmg, signed, notarized and stapled
```

Without it, the DMG includes "First time opening Desk Wheel.txt" with the one-time steps. Note that each update replaces an ad-hoc-signed app, so macOS may ask again for Screen Recording or Input Monitoring after updating; a Developer ID signature avoids that.

## Publishing an update (installs over the old version)

Installed copies check for updates twice a day at most (Settings › Updates, or "Check for updates…" in the menu) and install them over the current version: nothing to uninstall, and settings, history and the clipboard are kept.

- **Windows:** the new installer runs silently, closes Desk Wheel, upgrades it in place and starts it again.
- **Mac:** the new app is copied out of the DMG; after Desk Wheel quits, it's swapped in (the old one is put back if anything goes wrong) and opened.

Updates are safe by design: the update feed must be signed with Desk Wheel's private key (the public half is `platform/update-public-key.pem`, built into the app), and every download must match the SHA-256 in the feed. Anything else is ignored.

**One-time setup:**

1. Installed apps read `https://github.com/irfanaslam-io/desk-wheel/releases/latest/download/update.json`: each release is published in this repository, with no extra repo or token.
2. In this repo's **Settings › Secrets and variables › Actions**, add:
   - `UPDATE_SIGNING_KEY`: the private key (the whole `update-signing-key.pem`, including the BEGIN/END lines). Keep a backup somewhere safe: without it, future updates can't be signed.
   - `GA_API_SECRET` (optional, turns on usage statistics): see [Usage statistics](#usage-statistics-google-analytics) below.

**Each release:**

1. Set `<Version>` in `src/Wheel.App/Wheel.App.csproj` (for example `0.2.0`) and push it to `main`. Versions follow [semantic versioning](https://semver.org): the middle number for new features, the last for fixes, `1.0.0` once it's stable.
2. Create the release, either way:
   - **On GitHub:** *Releases › Draft a new release*, tag `v0.2.0` (*Create new tag on publish*, target `main`), title `Desk Wheel 0.2.0`, and write what's new. Publish.
   - **From a terminal:** `git tag -a v0.2.0 -m "What's new: …"` then `git push origin v0.2.0` (the tag message becomes what's new).
3. GitHub Actions checks that the tag matches `<Version>`, builds the Mac DMG and both Windows installers, writes and signs `update.json`, and attaches them to the release with a `SHA256SUMS.txt`. It then rewrites the release notes in the standard layout: **What's new** (your text), **Downloads** (one row per system), **Verify your download** and **Full changelog**. It takes about 20 minutes.

Installed copies pick the update up within about 12 hours. For the Microsoft Store, upload that build's `.msix` files in a new Partner Center submission.

To use a different address for the feed, build with `WHEEL_UPDATE_FEED=https://…/update.json`; set it to an empty value to switch in-app updates off.

## Usage statistics (Google Analytics)

What is and isn't collected is described in the [privacy policy](https://irfanaslam.io/desk-wheel/privacy) and the [README](../README.md#privacy).

Desk Wheel reports into irfanaslam.io's Google Analytics property (`G-9X34YKZZL5`, built in by default), next to the website, so there is no separate analytics account. To switch it on, open GA4 › Admin › Data streams › the irfanaslam.io stream › *Measurement Protocol API secrets*, create a secret named "Desk Wheel", and add it as the `GA_API_SECRET` Actions secret (or set `WHEEL_GA_API_SECRET` when building locally; `WHEEL_GA_MEASUREMENT_ID` / the `GA_MEASUREMENT_ID` secret can point it at another property). Without the secret, no statistics code runs and nobody is asked.

Every app event starts with `dw_` and carries `app_name = desk-wheel`, so the app and the website never mix in reports:

| Event | Parameters | When |
| --- | --- | --- |
| `dw_app_start` | | the app starts |
| `dw_screen` | `screen_name` (`settings-general`, `settings-updates`, `clipboard`, `history`, `editor`, `wheel-convert`, `wheel-tools`, `snip`, `pin`, `preview-card`…) | a Desk Wheel screen opens |
| `dw_tool_used` | `tool`, `files` | a conversion or tool runs |
| `dw_screenshot` | `kind` | a screenshot is taken |
| `dw_snip` | `action` | a snip is copied, saved, pinned… |
| `dw_update_started` | `to_version` | an in-app update starts |

Every event also has `app_version`, `os` and `arch`. In GA4 › Admin › Custom definitions, register `app_name`, `screen_name`, `tool` and `kind` as event-scoped custom dimensions; then in Explore filter on *Event name starts with `dw_`* (or `app_name = desk-wheel`). To keep app events out of the website's own reports, add a comparison or filter excluding `app_name = desk-wheel`.
