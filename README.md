# Firefox-Bookmarks-Cleanup
A C# console application for managing book chapter related bookmarks in Firefox. The app focuses on bookmarks where the title contains both a title and a chapter number, ensuring that only the most recent chapter is kept. It uses [`Serilog`](https://github.com/serilog/serilog) for detailed logging during the cleanup process.

## Requirements
- .NET 9.0 SDK

## Installation
1. Clone the `Firefox_Bookmarks_Cleanup` repository or download the latest release.
2. Build the project using Visual Studio or the .NET CLI.
3. Make a copy of the `appsettings-example.json` file and rename it to `appsettings.json`.
   - You can leave the `appsettings.json` file with its default values.
4. **Required Configuration**:
   - In the appsettings.json file, ensure that the `ConnectionStrings.BrowserDb` and `ConnectionStrings.Db `keys are populated with valid connection strings for your databases:
      - `ConnectionStrings.BrowserDb`: The connection string for your Firefox `places.sqlite` database. For more details, refer to the [Troubleshooting section](#Troubleshooting).
      - `ConnectionStrings.Db`: The connection string for your main database where the latest bookmark chapter data for each title is stored. If this is not provided, the database file `firefox-bookmarks.sqlite` will be automatically created in the current directory.
4. Download and install the [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) if you haven't already.

## Configurable `appsettings.json` Options
These options can be customized by editing the `appsettings.json` file.

| Key  | Default | Example | Description |
| ---- | ---- | ---- | ---- |
| `ConnectionStrings.BrowserDb` | `""` | `Data Source=path\to\places.sqlite` | The connection string for the Firefox `places.sqlite` database, which stores bookmarks. See [Firefox Profiles Doc](https://support.mozilla.org/en-US/kb/profiles-where-firefox-stores-user-data) for more info. |
| `ConnectionStrings.Db` | `""` | `Data Source=path\to\database.sqlite` | The connection string for your main database, where the latest bookmark chapter data for each title is stored. If not provided, the database file `firefox-bookmarks.sqlite` will be automatically created in the current directory. |
| `FolderNamesOrIds` | `[ "mobile" ]` | `[ "mobile", "6", "23" ]` | The bookmarks in these folder IDs (or names) will be processed. By default, it processes the bookmarks in the "mobile" folder. |
| `DryRun` | `false` | `true` | When set to `true`, the app will simulate the cleanup process without making any changes to the bookmarks. |
| `Exceptions.NonAsciiIgnoreList` | `[]` | `[ "'", "ø" ]` | A list of non-ASCII characters that can be ignored during processing. |
| `Exceptions.Replace` | `{}` | `{ "—": "-", "‘": "'", "•": "-" }` | A dictionary of characters to replace with their Unicode equivalents or substitutes (e.g., em-dashes replaced with hyphens). |
| `Serilog:MinimumLevel:Default` | `Debug` | `Information` | The minimum log level for Serilog. Available levels: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, or `Fatal`. See [`Serilog Minimum Level`](https://github.com/serilog/serilog/wiki/Configuration-Basics#minimum-level) for more info. |
| `Serilog:WriteTo:Args:path` | - | `Logs/log.txt` | The path to the log file. If the directories do not exist, they will be created. |
| `Serilog:WriteTo:Args:rollingInterval` | `Infinite` | `Day` | Defines how frequently the log file will be rolled over. Options include `Infinite`, `Day`, `Hour`, or `Minute`. See [`Serilog Rolling Interval`](https://github.com/serilog/serilog-sinks-file/blob/dev/src/Serilog.Sinks.File/RollingInterval.cs) for more info. |
| `Serilog:WriteTo:Args:retainedFileCountLimit` | `31` | `null` | The number of log files to retain before deleting older logs. |
| `Serilog:WriteTo:Args:shared` | `false` | `true` | When set to true, allows multiple processes to write to the same log file simultaneously. |
| `Serilog:WriteTo:Args:outputTemplate` | `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}` | `{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u5}] {Message:lj}{NewLine}{Exception}` | Template for the log entry format. See [`Serilog Formatting Output`](https://github.com/serilog/serilog/wiki/Formatting-Output) for more details. |

## Usage

To start the application, run the following command from your terminal (inside the project directory):
```bash
dotnet run
```
On the first run, the app will prompt you for any missing or required values from the appsettings.json file.

### Dry Run Mode
To simulate the app's operations without making any actual changes, you can enable Dry Run mode in one of two ways:

1. Via `appsettings.json`:
Set `DryRun` to `true` in the `appsettings.json`:
```json
{
  "DryRun": true
}
```

2. Via Command-Line Argument:
You can also enable Dry Run mode directly from the command line by passing the --dry-run flag:
```bash
dotnet run --dry-run
```
\* Note: The command-line argument takes priority over the appsettings.json configuration.

## Troubleshooting

### I can’t find my Firefox database (places.sqlite).
Ensure that you are using the correct Firefox profile directory. You can find your profile location by following the steps outlined in [Firefox Profiles Doc](https://support.mozilla.org/en-US/kb/profiles-where-firefox-stores-user-data).

### How do I change the log file location?
The log file location is specified in the `appsettings.json` file under the `Serilog:WriteTo:Args:path` setting. Update this path to specify a different log file location.

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing
If you'd like to contribute to this project, please fork the repository and submit a pull request with your changes.