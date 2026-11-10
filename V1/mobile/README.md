# Wildframe Studio mobile shell

This .NET MAUI app targets Android 5.0 (API 21) and Windows. It stores preferences and the activity list in MAUI app-private storage. The OAuth JSON picker asks the user first and copies only the selected file into the app's private directory; Android broad storage permission is not requested.

## Run the service and app

The mobile UI calls the existing ASP.NET API. On the Android emulator, `10.0.2.2:5077` maps to the development computer's loopback service. Start it on the computer with:

```powershell
dotnet run --project YouTubeAutomation/frontend/YouTubeAutomation.Web.csproj
```

For a physical device, configure a reachable service URL in Settings. Use a trusted network and HTTPS for any non-emulator connection. The local development server currently binds to loopback and is not reachable from a physical device without a separately secured deployment/configuration.

## Build Android

Install the Android SDK (platform tools, platform 36, build tools) and JDK 17+, then set `ANDROID_HOME` and `JAVA_HOME`. Build a debug APK with:

```powershell
dotnet build YouTubeAutomation/mobile/YouTubeAutomation.Mobile.csproj -f net10.0-android
```

Output is under `mobile/bin/Debug/net10.0-android/`. A release APK needs a signing key owned and protected by the app publisher; do not commit that key or its passwords.

## Current integration boundary

Importing the OAuth JSON validates and stores it locally. The existing API still performs Google sign-in using its own configured desktop credential file; the mobile import is not yet connected to a native Android OAuth flow. AI provider choices are saved as preferences, but the existing service currently implements Groq only. Insights show operations initiated during the local service session; exact API quota and channel analytics are not returned by the current backend.
