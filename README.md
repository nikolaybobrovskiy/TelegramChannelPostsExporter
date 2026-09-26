# Telegram Channel Posts Exporter

A .NET 10 command-line application that collects posts from a public Telegram channel and saves them as a JSON file. No API key required. Posts can be filtered by an inclusive UTC date range. Optionally, post images are downloaded and embedded in the JSON as Base64 data URLs.

## Requirements

- To build: .NET 10 SDK
- To run the published executable: Windows x64; a separate .NET runtime installation is not required

## Build

From the project root, run:

```powershell
dotnet publish -c Release
```

The publish settings create a trimmed, compressed, self-contained single-file executable at the project root, alongside `Program.cs`:

```text
TelegramChannelPostsExporter.exe
```

The executable targets Windows x64. `publish.cmd` runs the same publish command.

## Usage

```powershell
.\TelegramChannelPostsExporter.exe --channel <username> --output <file> [options]
```

Example:

```powershell
.\TelegramChannelPostsExporter.exe --channel headlines_for_traders --start "2026-09-24 00:00:00" --end "2026-09-26 12:00:00" --output posts.json --saveImages true
```

Run with `--help` to print usage information.

| Argument                        | Description                                                                                                   |
|---------------------------------|---------------------------------------------------------------------------------------------------------------|
| `--channel`, `--username`       | Public Telegram channel username. Required.                                                                   |
| `--output`, `--outfile`         | Path to the output JSON file. Required.                                                                       |
| `--start`, `--startDate`        | Inclusive start date in `yyyy-MM-dd HH:mm:ss` format (UTC). Defaults to two days before the current UTC time. |
| `--end`, `--endDate`            | Inclusive end date in `yyyy-MM-dd HH:mm:ss` format (UTC). Defaults to the current UTC time.                   |
| `--saveImages <true or false>`  | Download post images and embed them as Base64 data URLs. Defaults to `false`.                                 |
| `--help`, `-h`, `-?`, `/?`, `?` | Print usage information and exit.                                                                             |

The JSON output is an array of posts containing their ID, date, text, and optional image data URL.
