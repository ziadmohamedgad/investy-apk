# Investy APK

Investy APK is the Android mobile version of Investy. It is a self-contained .NET MAUI Blazor app with local storage and EODHD price integration.

## Features

- Track portfolio assets and transactions on Android.
- Store data locally on the device.
- Save the EODHD API key securely with platform secure storage.
- Sync Egyptian stock prices through EODHD.
- Export data to an Excel file when Android storage permissions allow it.
- Dark/light mode, balance hiding, and mobile-first navigation.

## First Run

1. Create a free EODHD account at https://eodhd.com/.
2. Open Investy on the phone.
3. Enter your EODHD API key on the first screen.
4. The key is saved using `SecureStorage`; it is not stored in the repository.

## Development Setup

Prerequisites:

- .NET SDK 10 with MAUI workload
- Android SDK/emulator or a physical Android device

Build:

```powershell
dotnet build
```

Publish APK:

```powershell
dotnet publish -f net10.0-android -c Release
```

The friendly APK copy used during local development is:

```text
bin/Release/net10.0-android/publish/Investy.apk
```

APK files are ignored by Git and should be published as release artifacts instead of committed.

## Local Data

The mobile database lives inside the Android app sandbox on the phone. It is not committed to GitHub. A fresh install starts with an empty local database and asks the user for an EODHD key.

## Secrets

Do not commit:

- Real API keys.
- Android signing keys: `.keystore`, `.jks`, `.p12`.
- Exported Excel files.
- Local SQLite/database files.

## Suggested GitHub Setup

From this folder:

```powershell
git init
git add .
git status
git commit -m "Initial Investy APK commit"
git branch -M main
git remote add origin https://github.com/YOUR_USER/investy-apk.git
git push -u origin main
```

Before pushing, check `git status` carefully and confirm no APKs, signing keys, exported Excel files, local databases, or secrets are staged.
