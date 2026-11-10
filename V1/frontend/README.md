# YouTube Automation UI

Run the local web app from the repository root:

```powershell
dotnet run --project YouTubeAutomation/frontend/YouTubeAutomation.Web.csproj
```

From the `YouTubeAutomation` directory, the command is `dotnet run --project frontend/YouTubeAutomation.Web.csproj`.

Then open <http://127.0.0.1:5077>. The UI uses the existing YouTube discovery, downloader, metadata generator, and uploader services. Put the YouTube OAuth client secret and `.env` settings in this directory, as used by the console app.

The final action uploads with **Public** visibility. Review the selected video and metadata before publishing.
