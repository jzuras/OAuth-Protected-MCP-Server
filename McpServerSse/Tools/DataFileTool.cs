using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McpServerSse.Tools;

[McpServerToolType, Description("I am a data file analyzer.")]
public class DataFileTool
{
    private static string? DataDirectoryPath { get; set; }
    private static string? CommandLineDataPath { get; set; }
    private static object LockObject { get; } = new();

    public static void SetDataDirectoryPath(string? dataPath)
    {
        CommandLineDataPath = dataPath;
        Console.WriteLine($"[MCP] Command line data path set to: {dataPath ?? "null"}");
    }

    private static string GetDataDirectory()
    {
        if (DataDirectoryPath is not null)
        {
            return DataDirectoryPath;
        }

        lock (LockObject)
        {
            if (DataDirectoryPath is not null)
            {
                return DataDirectoryPath;
            }

            DataDirectoryPath = FindDataDirectory();
            return DataDirectoryPath;
        }
    }

    private static string FindDataDirectory()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            Console.WriteLine($"[MCP] Current directory: {currentDir}");

            // If command line path was provided, try it first
            if (!string.IsNullOrEmpty(CommandLineDataPath))
            {
                Console.WriteLine($"[MCP] Trying command line data path: {CommandLineDataPath}");
                
                // Handle cross-platform home directory resolution
                var resolvedPath = ResolveDataPath(CommandLineDataPath);
                Console.WriteLine($"[MCP] Resolved path: {resolvedPath}");
                
                if (Directory.Exists(resolvedPath))
                {
                    Console.WriteLine($"[MCP] Found Data directory at command line path: {resolvedPath}");
                    return resolvedPath;
                }
                else
                {
                    Console.WriteLine($"[MCP] Command line data path does not exist: {resolvedPath}");
                }
            }
            
            // Fallback to original path discovery logic
            Console.WriteLine($"[MCP] Falling back to path discovery from current directory");
            var possiblePaths = new[]
            {
                Path.Combine(currentDir, "Data"),                                     // ./Data
                Path.Combine(currentDir, "..", "Data"),                               // ../Data
                Path.Combine(currentDir, "..", "..", "Data"),                         // ../../Data
                Path.Combine(currentDir, "..", "..", "..", "Data"),                   // ../../../Data
                Path.Combine(currentDir, "..", "..", "..", "..", "Data"),             // ../../../../Data
                Path.Combine(currentDir, "..", "..", "..", "..", "..", "Data"),       // ../../../../../Data
                Path.Combine(currentDir, "..", "..", "TUI", "Data"),                  // ../../TUI/Data
                Path.Combine(currentDir, "..", "..", "..", "TUI", "Data"),            // ../../../TUI/Data
                Path.Combine(currentDir, "..", "..", "..", "..", "TUI", "Data"),       // ../../../../TUI/Data
                Path.Combine(currentDir, "..", "..", "..", "..", "..", "TUI", "Data"), // ../../../../../TUI/Data
            };

            foreach (var path in possiblePaths)
            {
                var absolutePath = Path.GetFullPath(path);
                Console.WriteLine($"[MCP] Checking path: {path} -> {absolutePath}");
                if (Directory.Exists(path))
                {
                    Console.WriteLine($"[MCP] Found Data directory at: {path}");
                    return path;
                }
            }

            var commandLineMsg = !string.IsNullOrEmpty(CommandLineDataPath) 
                ? $" Command line path '{CommandLineDataPath}' resolved to '{ResolveDataPath(CommandLineDataPath)}' but does not exist."
                : " No command line path provided.";
            
            var errorMsg = $"Could not find Data directory. Current working directory: {currentDir}.{commandLineMsg} Tried fallback paths: {string.Join(", ", possiblePaths.Select(p => Path.GetFullPath(p)))}";
            Console.WriteLine($"[MCP] {errorMsg}");
            throw new McpException(errorMsg);
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MCP] Error finding Data directory: {ex.Message}");
            throw new McpException($"Error accessing filesystem: {ex.Message}");
        }
    }

    private static string ResolveDataPath(string dataPath)
    {
        try
        {
            // If it's already an absolute path, return as-is
            if (Path.IsPathRooted(dataPath))
            {
                return dataPath;
            }

            // Cross-platform home directory resolution
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(homeDirectory))
            {
                // Fallback for Linux/Unix systems
                homeDirectory = Environment.GetEnvironmentVariable("HOME") ?? Directory.GetCurrentDirectory();
            }

            Console.WriteLine($"[MCP] Home directory: {homeDirectory}");
            
            // Combine home directory with relative path
            var resolvedPath = Path.Combine(homeDirectory, dataPath);
            return Path.GetFullPath(resolvedPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MCP] Error resolving path '{dataPath}': {ex.Message}");
            throw new McpException($"Invalid data directory path: {dataPath}");
        }
    }

    [McpServerTool, Description("Returns inventory of available Enphase solar monitoring CSV files including date ranges, record counts, and data coverage. Use this to discover what historical solar energy production, consumption, and panel performance data is available for analysis.")]
    public static object ListCsvFiles([Description("Must be 'system', 'panel', or 'both'")] string fileType = "both")
    {
        try
        {
            var dataDir = GetDataDirectory();

            var pattern = fileType.ToLower() switch
            {
                "system" => "SystemData_*.csv",
                "panel" => "PanelData_*.csv", 
                "both" => "*.csv",
                _ => throw new McpException($"Invalid file_type: '{fileType}'. Must be 'system', 'panel', or 'both'")
            };

            var csvFiles = Directory.GetFiles(dataDir, pattern);

            // Process all CSV files found
            var fileMetadataList = new List<object>();

            foreach (var file in csvFiles)
            {
                var filename = Path.GetFileName(file);
                
                if (filename.Contains('_'))
                {
                    var fileInfo = new FileInfo(file);
                    var detectedFileType = filename.StartsWith("SystemData") ? "system" : "panel";
                    
                    // Parse timestamp from filename: SystemData_2025-07-19_07-49-29.csv
                    var match = Regex.Match(filename, @"(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})");
                    var created = fileInfo.CreationTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    
                    if (match.Success is true)
                    {
                        var dateStr = match.Groups[1].Value;
                        var timeStr = match.Groups[2].Value.Replace('-', ':');
                        if (DateTime.TryParseExact($"{dateStr} {timeStr}", "yyyy-MM-dd HH:mm:ss", 
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        {
                            created = parsed.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        }
                    }

                    // Count records quickly
                    var recordCount = 0;
                    var firstRecord = "unknown";
                    var lastRecord = "unknown";

                    try
                    {
                        var lines = File.ReadAllLines(file);
                        if (lines.Length > 1)
                        {
                            var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                            recordCount = dataLines.Length;
                            
                            if (dataLines.Length > 0)
                            {
                                var firstLine = dataLines[0].Split(',');
                                var lastLine = dataLines[dataLines.Length - 1].Split(',');
                                
                                if (firstLine.Length > 0 && DateTime.TryParseExact(firstLine[0], "yyyy-MM-dd HH:mm:ss", 
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstTime))
                                {
                                    firstRecord = firstTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                                }
                                
                                if (lastLine.Length > 0 && DateTime.TryParseExact(lastLine[0], "yyyy-MM-dd HH:mm:ss", 
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastTime))
                                {
                                    lastRecord = lastTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not read file {filename}: {ex.Message}");
                        // Continue with default metadata values
                    }

                    fileMetadataList.Add(new
                    {
                        filename = filename,
                        type = detectedFileType,
                        created = created,
                        first_record = firstRecord,
                        last_record = lastRecord,
                        record_count = recordCount
                    });
                }
            }

            return new
            {
                files = fileMetadataList.ToArray()
            };
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ListCsvFiles: {ex.Message}");
            throw new McpException($"Error listing CSV files: {ex.Message}");
        }
    }

    [McpServerTool, Description("Returns detailed metadata about a specific Enphase solar data CSV file including record count, date coverage, and sample data. Use this to understand the scope and structure of solar monitoring data in a particular file.")]
    public static object GetFileInfo([Description("Name of the CSV file to analyze (e.g., 'SystemData_2025-07-01_10-32-38.csv'). Use ListCsvFiles to discover available filenames.")] string filename)
    {
        try
        {
            var dataDir = GetDataDirectory();
            var filePath = Path.Combine(dataDir, filename);

            if (!File.Exists(filePath))
            {
                throw new McpException($"File '{filename}' not found in data directory. Use list_csv_files to see available files.");
            }

            var fileInfo = new FileInfo(filePath);
            var detectedFileType = filename.StartsWith("SystemData") ? "system" : 
                                   filename.StartsWith("PanelData") ? "panel" : "unknown";

            // Parse creation timestamp from filename
            var match = Regex.Match(filename, @"(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})");
            var created = fileInfo.CreationTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            
            if (match.Success is true)
            {
                var dateStr = match.Groups[1].Value;
                var timeStr = match.Groups[2].Value.Replace('-', ':');
                if (DateTime.TryParseExact($"{dateStr} {timeStr}", "yyyy-MM-dd HH:mm:ss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    created = parsed.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
            }

            // Read and parse CSV file
            var lines = File.ReadAllLines(filePath);
            if (lines.Length is 0)
            {
                throw new McpException($"File '{filename}' is empty.");
            }

            // Extract column headers
            var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            
            // Process data lines
            var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var recordCount = dataLines.Length;

            // Get date range from first and last records
            string startDate = "unknown";
            string endDate = "unknown";
            
            if (dataLines.Length > 0)
            {
                var firstLine = dataLines[0].Split(',');
                var lastLine = dataLines[dataLines.Length - 1].Split(',');
                
                if (firstLine.Length > 0 && DateTime.TryParseExact(firstLine[0].Trim(), "yyyy-MM-dd HH:mm:ss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstTime))
                {
                    startDate = firstTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                
                if (lastLine.Length > 0 && DateTime.TryParseExact(lastLine[0].Trim(), "yyyy-MM-dd HH:mm:ss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastTime))
                {
                    endDate = lastTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
            }

            // Create sample data (first 3 rows)
            var sampleData = new List<object>();
            var sampleRowCount = Math.Min(3, dataLines.Length);
            
            for (int i = 0; i < sampleRowCount; i++)
            {
                var fields = dataLines[i].Split(',').Select(f => f.Trim()).ToArray();
                if (fields.Length >= headers.Length)
                {
                    var row = new Dictionary<string, object>();
                    for (int j = 0; j < headers.Length; j++)
                    {
                        var value = fields[j];
                        
                        // Convert to appropriate type based on header
                        if (headers[j].ToLower().Contains("timestamp"))
                        {
                            row[headers[j]] = value; // Keep as string for display
                        }
                        else if (int.TryParse(value, out var intValue))
                        {
                            row[headers[j]] = intValue;
                        }
                        else if (double.TryParse(value, out var doubleValue))
                        {
                            row[headers[j]] = doubleValue;
                        }
                        else
                        {
                            row[headers[j]] = value; // Keep as string
                        }
                    }
                    sampleData.Add(row);
                }
            }

            return new
            {
                filename = filename,
                type = detectedFileType,
                size_bytes = fileInfo.Length,
                created = created,
                modified = fileInfo.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                record_count = recordCount,
                date_range = new
                {
                    start = startDate,
                    end = endDate
                },
                columns = headers,
                sample_data = sampleData.ToArray()
            };
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetFileInfo: {ex.Message}");
            throw new McpException($"Error reading file '{filename}': {ex.Message}");
        }
    }

    [McpServerTool, Description("Reads raw Enphase system-level energy data from CSV files including solar production, home consumption, net energy flow (grid import/export), and total panel output. Use this for analyzing overall energy performance and grid interaction patterns.")]
    public static object ReadSystemCsv([Description("Name of the SystemData CSV file to read (e.g., 'SystemData_2025-07-01_10-32-38.csv')")] string filename, [Description("Starting row number for pagination (0-based). Default is 0 to start from beginning.")] int startRow = 0, [Description("Maximum number of records to return (1-10000). Default is 1000. Use pagination for large files.")] int limit = 1000)
    {
        try
        {
            // Parameter validation
            if (string.IsNullOrEmpty(filename))
            {
                throw new McpException("Missing required parameter 'filename'.");
            }

            if (startRow < 0)
            {
                throw new McpException("Parameter 'startRow' must be 0 or greater.");
            }

            if (limit <= 0 || limit > 10000)
            {
                throw new McpException("Parameter 'limit' must be between 1 and 10000.");
            }

            var dataDir = GetDataDirectory();
            var filePath = Path.Combine(dataDir, filename);

            if (!File.Exists(filePath))
            {
                throw new McpException($"File '{filename}' not found in data directory. Use list_csv_files to see available files.");
            }

            // Validate it's a system data file
            if (!filename.StartsWith("SystemData"))
            {
                throw new McpException($"File '{filename}' is not a system data file. Use read_panel_csv for panel data files.");
            }

            // Read and parse CSV file
            var lines = File.ReadAllLines(filePath);
            if (lines.Length is 0)
            {
                throw new McpException($"File '{filename}' is empty.");
            }

            // Extract headers and validate structure
            var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            var expectedHeaders = new[] { "Timestamp", "Production_W", "Consumption_W", "Net_W", "PanelTotal_W" };
            
            if (headers.Length < expectedHeaders.Length)
            {
                throw new McpException($"File '{filename}' has invalid structure. Expected {expectedHeaders.Length} columns, found {headers.Length}.");
            }

            // Process data lines with pagination
            var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var totalRows = dataLines.Length;

            // Apply pagination
            var paginatedLines = dataLines.Skip(startRow).Take(limit).ToArray();
            var returnedRows = paginatedLines.Length;

            // Parse data rows
            var parsedData = new List<object>();
            
            foreach (var line in paginatedLines)
            {
                var fields = line.Split(',').Select(f => f.Trim()).ToArray();
                
                if (fields.Length >= expectedHeaders.Length)
                {
                    // Parse timestamp
                    var timestampStr = fields[0];
                    var timestamp = timestampStr; // Default to original string
                    
                    if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
                    {
                        timestamp = parsedTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    }

                    // Parse numeric values with validation
                    if (!int.TryParse(fields[1], out var productionW))
                    {
                        Console.WriteLine($"Warning: Invalid Production_W value '{fields[1]}' in {filename}");
                        productionW = 0;
                    }

                    if (!int.TryParse(fields[2], out var consumptionW))
                    {
                        Console.WriteLine($"Warning: Invalid Consumption_W value '{fields[2]}' in {filename}");
                        consumptionW = 0;
                    }

                    if (!int.TryParse(fields[3], out var netW))
                    {
                        Console.WriteLine($"Warning: Invalid Net_W value '{fields[3]}' in {filename}");
                        netW = 0;
                    }

                    if (!int.TryParse(fields[4], out var panelTotalW))
                    {
                        Console.WriteLine($"Warning: Invalid PanelTotal_W value '{fields[4]}' in {filename}");
                        panelTotalW = 0;
                    }

                    parsedData.Add(new
                    {
                        timestamp = timestamp,
                        production_w = productionW,
                        consumption_w = consumptionW,
                        net_w = netW,
                        panel_total_w = panelTotalW
                    });
                }
                else
                {
                    Console.WriteLine($"Warning: Invalid row format in {filename}: {line}");
                }
            }

            return new
            {
                filename = filename,
                total_rows = totalRows,
                returned_rows = returnedRows,
                data = parsedData.ToArray()
            };
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ReadSystemCsv: {ex.Message}");
            throw new McpException($"Error reading system CSV file '{filename}': {ex.Message}");
        }
    }

    [McpServerTool, Description("Reads raw individual Enphase solar panel performance data from CSV files including current watts, maximum watts, daily peak performance, and panel reporting status. Use this for analyzing individual panel efficiency and identifying underperforming panels.")]
    public static object ReadPanelCsv([Description("Name of the PanelData CSV file to read (e.g., 'PanelData_2025-07-01_10-32-38.csv')")] string filename, [Description("Starting row number for pagination (0-based). Default is 0 to start from beginning.")] int startRow = 0, [Description("Maximum number of records to return (1-10000). Default is 1000. Use pagination for large files.")] int limit = 1000)
    {
        try
        {
            // Parameter validation
            if (string.IsNullOrEmpty(filename))
            {
                throw new McpException("Missing required parameter 'filename'.");
            }

            if (startRow < 0)
            {
                throw new McpException("Parameter 'startRow' must be 0 or greater.");
            }

            if (limit <= 0 || limit > 10000)
            {
                throw new McpException("Parameter 'limit' must be between 1 and 10000.");
            }

            var dataDir = GetDataDirectory();
            var filePath = Path.Combine(dataDir, filename);

            if (!File.Exists(filePath))
            {
                throw new McpException($"File '{filename}' not found in data directory. Use list_csv_files to see available files.");
            }

            // Validate it's a panel data file
            if (!filename.StartsWith("PanelData"))
            {
                throw new McpException($"File '{filename}' is not a panel data file. Use read_system_csv for system data files.");
            }

            // Read and parse CSV file
            var lines = File.ReadAllLines(filePath);
            if (lines.Length is 0)
            {
                throw new McpException($"File '{filename}' is empty.");
            }

            // Extract headers and validate structure
            var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            var expectedHeaders = new[] { "Timestamp", "SerialNumber", "Watts", "MaxWatts", "DailyMaxWatts", "LastReportDate" };
            
            if (headers.Length < expectedHeaders.Length)
            {
                throw new McpException($"File '{filename}' has invalid structure. Expected {expectedHeaders.Length} columns, found {headers.Length}.");
            }

            // Process data lines with pagination
            var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var totalRows = dataLines.Length;

            // Apply pagination
            var paginatedLines = dataLines.Skip(startRow).Take(limit).ToArray();
            var returnedRows = paginatedLines.Length;

            // Parse data rows
            var parsedData = new List<object>();
            
            foreach (var line in paginatedLines)
            {
                var fields = line.Split(',').Select(f => f.Trim()).ToArray();
                
                if (fields.Length >= expectedHeaders.Length)
                {
                    // Parse timestamp
                    var timestampStr = fields[0];
                    var timestamp = timestampStr; // Default to original string
                    
                    if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
                    {
                        timestamp = parsedTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    }

                    // Parse serial number (string)
                    var serialNumber = fields[1];

                    // Parse numeric values with validation
                    if (!int.TryParse(fields[2], out var watts))
                    {
                        Console.WriteLine($"Warning: Invalid Watts value '{fields[2]}' for panel {serialNumber} in {filename}");
                        watts = 0;
                    }

                    if (!int.TryParse(fields[3], out var maxWatts))
                    {
                        Console.WriteLine($"Warning: Invalid MaxWatts value '{fields[3]}' for panel {serialNumber} in {filename}");
                        maxWatts = 0;
                    }

                    if (!int.TryParse(fields[4], out var dailyMaxWatts))
                    {
                        Console.WriteLine($"Warning: Invalid DailyMaxWatts value '{fields[4]}' for panel {serialNumber} in {filename}");
                        dailyMaxWatts = 0;
                    }

                    if (!long.TryParse(fields[5], out var lastReportDate))
                    {
                        Console.WriteLine($"Warning: Invalid LastReportDate value '{fields[5]}' for panel {serialNumber} in {filename}");
                        lastReportDate = 0;
                    }

                    parsedData.Add(new
                    {
                        timestamp = timestamp,
                        serial_number = serialNumber,
                        watts = watts,
                        max_watts = maxWatts,
                        daily_max_watts = dailyMaxWatts,
                        last_report_date = lastReportDate
                    });
                }
                else
                {
                    Console.WriteLine($"Warning: Invalid row format in {filename}: {line}");
                }
            }

            return new
            {
                filename = filename,
                total_rows = totalRows,
                returned_rows = returnedRows,
                data = parsedData.ToArray()
            };
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ReadPanelCsv: {ex.Message}");
            throw new McpException($"Error reading panel CSV file '{filename}': {ex.Message}");
        }
    }

    [McpServerTool, Description("Retrieves Enphase system-level solar energy data for a specific day across multiple CSV files. Returns production, consumption, net energy flow, and panel totals for analyzing daily energy patterns, self-sufficiency, and grid interaction. CRITICAL: This tool uses pagination due to token limits. When has_more_data=true, you MUST make additional calls with offset=next_offset to get complete data. Partial data analysis is INVALID for solar energy - you need full day data to find actual peaks (typically 11AM-2PM). For multi-day analysis, call this tool once per day. Daily data density: ~2,880 records. Default maxRecords is 500 to stay within token limits.")]
    public static object GetSystemDataByDate([Description("Date to retrieve data for in ISO format (e.g., '2025-07-20' or '2025-07-20T00:00:00Z'). Data will be filtered to this specific day.")] string date, [Description("Maximum records to return per call (1-500). Default is 500. Use pagination when has_more_data=true.")] int maxRecords = 500, [Description("Number of records to skip for pagination. Start with 0, then use next_offset from previous response.")] int offset = 0)
    {
        try
        {
            // Parameter validation
            if (string.IsNullOrEmpty(date))
            {
                throw new McpException("Missing required parameter 'date'.");
            }

            if (maxRecords <= 0 || maxRecords > 500)
            {
                throw new McpException("Parameter 'maxRecords' must be between 1 and 500 due to token limits.");
            }

            if (offset < 0)
            {
                throw new McpException("Parameter 'offset' must be 0 or greater.");
            }

            // Parse date
            if (!DateTime.TryParse(date, out var parsedDate))
            {
                throw new McpException($"Invalid date format: '{date}'. Expected ISO date format like '2025-07-20'.");
            }

            // Set start and end of day for filtering
            var parsedStartDate = parsedDate.Date;
            var parsedEndDate = parsedDate.Date.AddDays(1).AddTicks(-1); // End of day

            var dataDir = GetDataDirectory();
            
            // Find all SystemData CSV files
            var systemFiles = Directory.GetFiles(dataDir, "SystemData_*.csv");
            
            if (systemFiles.Length is 0)
            {
                return new
                {
                    date = date,
                    files_processed = new string[0],
                    total_records_in_range = 0,
                    returned_records = 0,
                    offset = offset,
                    has_more_data = false,
                    next_offset = (int?)null,
                    data = new object[0]
                };
            }

            var processedFiles = new List<string>();
            var aggregatedData = new List<object>();
            var totalRecordsProcessed = 0;
            var recordsSkipped = 0;
            var hasMoreData = false;

            // Sort files by filename to process chronologically
            Array.Sort(systemFiles);

            // Process each system data file
            foreach (var filePath in systemFiles)
            {
                var filename = Path.GetFileName(filePath);
                
                try
                {
                    // Quick check if file might contain relevant data based on filename
                    var match = Regex.Match(filename, @"(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})");
                    if (match.Success is true)
                    {
                        var dateStr = match.Groups[1].Value;
                        var timeStr = match.Groups[2].Value.Replace('-', ':');
                        
                        if (DateTime.TryParseExact($"{dateStr} {timeStr}", "yyyy-MM-dd HH:mm:ss", 
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                        {
                            // Skip files that are clearly outside the date range
                            if (fileDate.Date > parsedEndDate.Date || fileDate.Date < parsedStartDate.AddDays(-1).Date)
                            {
                                continue;
                            }
                        }
                    }

                    // Read and process file
                    var lines = File.ReadAllLines(filePath);
                    if (lines.Length <= 1) // Skip empty files or files with only headers
                    {
                        continue;
                    }

                    var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
                    var expectedHeaders = new[] { "Timestamp", "Production_W", "Consumption_W", "Net_W", "PanelTotal_W" };
                    
                    if (headers.Length < expectedHeaders.Length)
                    {
                        Console.WriteLine($"Warning: File {filename} has invalid structure, skipping.");
                        continue;
                    }

                    var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line));
                    var fileRecordCount = 0;
                    var fileHasRelevantData = false;

                    foreach (var line in dataLines)
                    {
                        // Stop collecting if we've reached maxRecords limit, but continue to check for more data
                        if (aggregatedData.Count >= maxRecords)
                        {
                            // Check if there are more valid records to determine hasMoreData
                            var checkFields = line.Split(',').Select(f => f.Trim()).ToArray();
                            if (checkFields.Length >= expectedHeaders.Length)
                            {
                                var timestampStr = checkFields[0];
                                if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordTime))
                                {
                                    if (recordTime >= parsedStartDate && recordTime <= parsedEndDate)
                                    {
                                        hasMoreData = true;
                                        break; // No need to check further records in this file
                                    }
                                }
                            }
                            continue;
                        }

                        var fields = line.Split(',').Select(f => f.Trim()).ToArray();
                        
                        if (fields.Length >= expectedHeaders.Length)
                        {
                            // Parse and validate timestamp
                            var timestampStr = fields[0];
                            
                            if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordTime))
                            {
                                // Check if record is within date range
                                if (recordTime >= parsedStartDate && recordTime <= parsedEndDate)
                                {
                                    fileHasRelevantData = true;
                                    fileRecordCount++;
                                    totalRecordsProcessed++;
                                    
                                    // Skip records until we reach the offset
                                    if (recordsSkipped < offset)
                                    {
                                        recordsSkipped++;
                                        continue;
                                    }
                                    
                                    // Parse numeric values with validation
                                    if (!int.TryParse(fields[1], out var productionW))
                                        productionW = 0;
                                    if (!int.TryParse(fields[2], out var consumptionW))
                                        consumptionW = 0;
                                    if (!int.TryParse(fields[3], out var netW))
                                        netW = 0;
                                    if (!int.TryParse(fields[4], out var panelTotalW))
                                        panelTotalW = 0;

                                    aggregatedData.Add(new
                                    {
                                        timestamp = recordTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                        production_w = productionW,
                                        consumption_w = consumptionW,
                                        net_w = netW,
                                        panel_total_w = panelTotalW
                                    });
                                }
                            }
                        }
                    }

                    if (fileHasRelevantData)
                    {
                        processedFiles.Add(filename);
                    }

                    // If we have more data already detected, no need to process more files
                    if (hasMoreData)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error processing file {filename}: {ex.Message}");
                    // Continue processing other files
                }
            }

            // Sort aggregated data by timestamp
            var sortedData = aggregatedData
                .OrderBy(d => ((dynamic)d).timestamp)
                .ToArray();

            return new
            {
                CRITICAL_WARNING = hasMoreData ? "⚠️ INCOMPLETE DATA - YOU MUST CONTINUE PAGINATION ⚠️" : null,
                date = parsedDate.ToString("yyyy-MM-dd"),
                files_processed = processedFiles.ToArray(),
                total_records_in_range = totalRecordsProcessed,
                returned_records = sortedData.Length,
                offset = offset,
                has_more_data = hasMoreData,
                next_offset = hasMoreData ? offset + sortedData.Length : (int?)null,
                pagination_status = hasMoreData ? "incomplete" : "complete",
                pagination_warning = hasMoreData ? "INCOMPLETE DATA: This response contains only partial data. You MUST call this tool again with offset=" + (offset + sortedData.Length) + " to get remaining data. Solar energy analysis requires complete daily data to find actual peaks." : null,
                data = hasMoreData ? sortedData.Select(d => new { 
                    _INCOMPLETE_DATA_WARNING = "This record is part of incomplete dataset",
                    timestamp = ((dynamic)d).timestamp,
                    production_w = ((dynamic)d).production_w,
                    consumption_w = ((dynamic)d).consumption_w,
                    net_w = ((dynamic)d).net_w,
                    panel_total_w = ((dynamic)d).panel_total_w
                }).ToArray() : sortedData
            };
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetSystemDataByDateRange: {ex.Message}");
            throw new McpException($"Error retrieving system data by date: {ex.Message}");
        }
    }

    [McpServerTool, Description("Retrieves individual Enphase solar panel performance data for a specific day across multiple CSV files. Returns per-panel watts, efficiency metrics, and daily maximums for analyzing daily panel performance, comparisons, and maintenance needs. CRITICAL: This tool uses pagination due to EXTREMELY dense data. Panel data contains ~63,000 records per day (22 panels × frequent readings). Default maxRecords is 100 to prevent token limit errors. For token efficiency use: aggregateMode='daily' (22 records: one per panel with daily peak/avg), aggregateMode='hourly' (528 records: 24 hours × 22 panels), or null for raw data. When has_more_data=true, you MUST make additional calls with offset=next_offset to get complete data. Solar analysis requires complete daily patterns including peak hours (11AM-2PM).")]
    public static object GetPanelDataByDate([Description("Date to retrieve data for in ISO format (e.g., '2025-07-20' or '2025-07-20T00:00:00Z'). Data will be filtered to this specific day.")] string date, [Description("Optional panel serial number to filter results (e.g., '202124054085'). Use GetPanelSerials to discover available panel IDs.")] string? panelSerial = null, [Description("Maximum records to return per call (1-100). Default is 100. Panel data is very dense - use aggregateMode for efficiency.")] int maxRecords = 100, [Description("Number of records to skip for pagination. Start with 0, then use next_offset from previous response.")] int offset = 0, [Description("Data aggregation mode: null/empty for raw data, 'hourly' for hourly summaries, 'daily' for daily summaries per panel. Use aggregation for token efficiency.")] string? aggregateMode = null)
    {
        try
        {
            // Parameter validation
            if (string.IsNullOrEmpty(date))
            {
                throw new McpException("Missing required parameter 'date'.");
            }

            if (maxRecords <= 0 || maxRecords > 100)
            {
                throw new McpException("Parameter 'maxRecords' must be between 1 and 100 due to token limits. Panel data is very dense (~63,000 records/day).");
            }

            if (offset < 0)
            {
                throw new McpException("Parameter 'offset' must be 0 or greater.");
            }

            // Validate aggregateMode parameter
            if (!string.IsNullOrEmpty(aggregateMode) && aggregateMode != "hourly" && aggregateMode != "daily")
            {
                throw new McpException("Parameter 'aggregateMode' must be 'hourly', 'daily', or null/empty for raw data.");
            }

            // Parse date
            if (!DateTime.TryParse(date, out var parsedDate))
            {
                throw new McpException($"Invalid date format: '{date}'. Expected ISO date format like '2025-07-20'.");
            }

            // Set start and end of day for filtering
            var parsedStartDate = parsedDate.Date;
            var parsedEndDate = parsedDate.Date.AddDays(1).AddTicks(-1); // End of day

            var dataDir = GetDataDirectory();
            
            // Find all PanelData CSV files
            var panelFiles = Directory.GetFiles(dataDir, "PanelData_*.csv");
            
            if (panelFiles.Length is 0)
            {
                return new
                {
                    date = date,
                    panel_filter = panelSerial,
                    files_processed = new string[0],
                    total_records_in_range = 0,
                    returned_records = 0,
                    offset = offset,
                    has_more_data = false,
                    next_offset = (int?)null,
                    data = new object[0]
                };
            }

            var processedFiles = new List<string>();
            var aggregatedData = new List<object>();
            var totalRecordsProcessed = 0;
            var recordsSkipped = 0;
            var hasMoreData = false;
            
            // For aggregation modes, collect all raw data first
            var rawDataForAggregation = new List<(DateTime timestamp, string serialNumber, int watts, int maxWatts, int dailyMaxWatts, long lastReportDate)>();
            var isHourlyMode = aggregateMode == "hourly";
            var isDailyMode = aggregateMode == "daily";
            var isAggregateMode = isHourlyMode || isDailyMode;

            // Sort files by filename to process chronologically
            Array.Sort(panelFiles);

            // Process each panel data file
            foreach (var filePath in panelFiles)
            {
                var filename = Path.GetFileName(filePath);
                
                try
                {
                    // Quick check if file might contain relevant data based on filename
                    var match = Regex.Match(filename, @"(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})");
                    if (match.Success is true)
                    {
                        var dateStr = match.Groups[1].Value;
                        var timeStr = match.Groups[2].Value.Replace('-', ':');
                        
                        if (DateTime.TryParseExact($"{dateStr} {timeStr}", "yyyy-MM-dd HH:mm:ss", 
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                        {
                            // Skip files that are clearly outside the date range
                            if (fileDate.Date > parsedEndDate.Date || fileDate.Date < parsedStartDate.AddDays(-1).Date)
                            {
                                continue;
                            }
                        }
                    }

                    // Read and process file
                    var lines = File.ReadAllLines(filePath);
                    if (lines.Length <= 1) // Skip empty files or files with only headers
                    {
                        continue;
                    }

                    var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
                    var expectedHeaders = new[] { "Timestamp", "SerialNumber", "Watts", "MaxWatts", "DailyMaxWatts", "LastReportDate" };
                    
                    if (headers.Length < expectedHeaders.Length)
                    {
                        Console.WriteLine($"Warning: File {filename} has invalid structure, skipping.");
                        continue;
                    }

                    var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line));
                    var fileRecordCount = 0;
                    var fileHasRelevantData = false;

                    foreach (var line in dataLines)
                    {
                        // Stop collecting if we've reached maxRecords limit (except in aggregate modes), but continue to check for more data
                        if (!isAggregateMode && aggregatedData.Count >= maxRecords)
                        {
                            // Check if there are more valid records to determine hasMoreData
                            var checkFields = line.Split(',').Select(f => f.Trim()).ToArray();
                            if (checkFields.Length >= expectedHeaders.Length)
                            {
                                var timestampStr = checkFields[0];
                                if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordTime))
                                {
                                    if (recordTime >= parsedStartDate && recordTime <= parsedEndDate)
                                    {
                                        // Check panel serial filter
                                        if (string.IsNullOrEmpty(panelSerial) || checkFields[1] == panelSerial)
                                        {
                                            hasMoreData = true;
                                            break; // No need to check further records in this file
                                        }
                                    }
                                }
                            }
                            continue;
                        }

                        var fields = line.Split(',').Select(f => f.Trim()).ToArray();
                        
                        if (fields.Length >= expectedHeaders.Length)
                        {
                            // Parse and validate timestamp
                            var timestampStr = fields[0];
                            
                            if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordTime))
                            {
                                // Check if record is within date range
                                if (recordTime >= parsedStartDate && recordTime <= parsedEndDate)
                                {
                                    var serialNumber = fields[1];
                                    
                                    // Check panel serial filter
                                    if (string.IsNullOrEmpty(panelSerial) || serialNumber == panelSerial)
                                    {
                                        fileHasRelevantData = true;
                                        fileRecordCount++;
                                        totalRecordsProcessed++;
                                        
                                        // Skip records until we reach the offset (except in aggregate modes where we need all data)
                                        if (!isAggregateMode && recordsSkipped < offset)
                                        {
                                            recordsSkipped++;
                                            continue;
                                        }
                                        
                                        // Parse numeric values with validation
                                        if (!int.TryParse(fields[2], out var watts))
                                            watts = 0;
                                        if (!int.TryParse(fields[3], out var maxWatts))
                                            maxWatts = 0;
                                        if (!int.TryParse(fields[4], out var dailyMaxWatts))
                                            dailyMaxWatts = 0;
                                        if (!long.TryParse(fields[5], out var lastReportDate))
                                            lastReportDate = 0;

                                        if (isAggregateMode)
                                        {
                                            // Collect raw data for aggregation
                                            rawDataForAggregation.Add((recordTime, serialNumber, watts, maxWatts, dailyMaxWatts, lastReportDate));
                                        }
                                        else
                                        {
                                            // Original raw data output
                                            aggregatedData.Add(new
                                            {
                                                timestamp = recordTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                                serial_number = serialNumber,
                                                watts = watts,
                                                max_watts = maxWatts,
                                                daily_max_watts = dailyMaxWatts,
                                                last_report_date = lastReportDate
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (fileHasRelevantData)
                    {
                        processedFiles.Add(filename);
                    }

                    // If we have more data already detected (and not in aggregate modes), no need to process more files
                    if (!isAggregateMode && hasMoreData)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error processing file {filename}: {ex.Message}");
                    // Continue processing other files
                }
            }

            // Process aggregation if requested
            if (isAggregateMode && rawDataForAggregation.Count > 0)
            {
                if (isHourlyMode)
                {
                    // Group by panel serial and hour, then aggregate
                    var hourlyGroups = rawDataForAggregation
                        .GroupBy(d => new { SerialNumber = d.serialNumber, Hour = new DateTime(d.timestamp.Year, d.timestamp.Month, d.timestamp.Day, d.timestamp.Hour, 0, 0) })
                        .OrderBy(g => g.Key.Hour)
                        .ThenBy(g => g.Key.SerialNumber);

                    foreach (var group in hourlyGroups)
                    {
                        var hourData = group.ToList();
                        var avgWatts = hourData.Average(d => d.watts);
                        var peakWatts = hourData.Max(d => d.watts);
                        var peakTime = hourData.First(d => d.watts == peakWatts).timestamp;
                        var totalEnergyWh = hourData.Sum(d => d.watts) / 4.0; // Approximate Wh assuming 15-min intervals
                        var readingCount = hourData.Count;
                        var maxWatts = hourData.Max(d => d.maxWatts);
                        var dailyMaxWatts = hourData.Max(d => d.dailyMaxWatts);

                        aggregatedData.Add(new
                        {
                            hour = group.Key.Hour.ToString("yyyy-MM-ddTHH:00:00Z"),
                            serial_number = group.Key.SerialNumber,
                            avg_watts = Math.Round(avgWatts, 1),
                            peak_watts = peakWatts,
                            peak_time = peakTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            total_energy_wh = Math.Round(totalEnergyWh, 1),
                            reading_count = readingCount,
                            max_watts = maxWatts,
                            daily_max_watts = dailyMaxWatts
                        });
                    }
                }
                else if (isDailyMode)
                {
                    // Group by panel serial only (daily summary), then aggregate
                    var dailyGroups = rawDataForAggregation
                        .GroupBy(d => d.serialNumber)
                        .OrderBy(g => g.Key);

                    foreach (var group in dailyGroups)
                    {
                        var dayData = group.ToList();
                        var avgWatts = dayData.Average(d => d.watts);
                        var peakWatts = dayData.Max(d => d.watts);
                        var peakTime = dayData.First(d => d.watts == peakWatts).timestamp;
                        var totalEnergyWh = dayData.Sum(d => d.watts) / 4.0; // Approximate Wh assuming 15-min intervals
                        var readingCount = dayData.Count;
                        var maxWatts = dayData.Max(d => d.maxWatts);
                        var dailyMaxWatts = dayData.Max(d => d.dailyMaxWatts);

                        aggregatedData.Add(new
                        {
                            date = parsedDate.ToString("yyyy-MM-dd"),
                            serial_number = group.Key,
                            avg_watts = Math.Round(avgWatts, 1),
                            peak_watts = peakWatts,
                            peak_time = peakTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            total_energy_wh = Math.Round(totalEnergyWh, 1),
                            reading_count = readingCount,
                            max_watts = maxWatts,
                            daily_max_watts = dailyMaxWatts
                        });
                    }
                }
            }

            // Sort aggregated data by timestamp then by serial number for consistent output
            var sortedData = aggregatedData
                .OrderBy(d => isHourlyMode ? ((dynamic)d).hour : (isDailyMode ? ((dynamic)d).date : ((dynamic)d).timestamp))
                .ThenBy(d => ((dynamic)d).serial_number)
                .ToArray();

            return new
            {
                CRITICAL_WARNING = (!isAggregateMode && hasMoreData) ? "⚠️ INCOMPLETE DATA - YOU MUST CONTINUE PAGINATION ⚠️" : null,
                date = parsedDate.ToString("yyyy-MM-dd"),
                panel_filter = panelSerial,
                files_processed = processedFiles.ToArray(),
                total_records_in_range = totalRecordsProcessed,
                returned_records = sortedData.Length,
                offset = isAggregateMode ? 0 : offset,
                has_more_data = isAggregateMode ? false : hasMoreData,
                next_offset = isAggregateMode ? (int?)null : (hasMoreData ? offset + sortedData.Length : (int?)null),
                pagination_status = isAggregateMode ? "complete" : (hasMoreData ? "incomplete" : "complete"),
                pagination_warning = isAggregateMode ? null : (hasMoreData ? "INCOMPLETE DATA: This response contains only partial panel data. You MUST call this tool again with offset=" + (offset + sortedData.Length) + " to get remaining data. Panel analysis requires complete daily data to identify performance patterns and peaks." : null),
                aggregate_mode = isHourlyMode ? "hourly" : (isDailyMode ? "daily" : "raw"),
                data = (!isAggregateMode && hasMoreData) ? sortedData.Select(d => new { 
                    _INCOMPLETE_DATA_WARNING = "This record is part of incomplete dataset",
                    timestamp = isHourlyMode ? ((dynamic)d).hour : (isDailyMode ? ((dynamic)d).date : ((dynamic)d).timestamp),
                    serial_number = ((dynamic)d).serial_number,
                    watts = isHourlyMode ? ((dynamic)d).avg_watts : (isDailyMode ? ((dynamic)d).avg_watts : ((dynamic)d).watts),
                    max_watts = ((dynamic)d).max_watts,
                    daily_max_watts = ((dynamic)d).daily_max_watts,
                    last_report_date = isDailyMode ? null : ((dynamic)d).last_report_date
                }).ToArray() : sortedData
            };
        }
        catch (McpException)
        {
            // Re-throw McpException as-is
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetPanelDataByDate: {ex.Message}");
            throw new McpException($"Error retrieving panel data by date: {ex.Message}");
        }
    }

    [McpServerTool, Description("Returns list of unique Enphase solar panel serial numbers found in the monitoring data. Use this to discover which panels exist in your solar system and are available for individual performance analysis and comparison.")]
    public static object GetPanelSerials([Description("Optional date range filter as object with 'start' and 'end' properties (ISO format). Leave null to search all available data.")] object? dateRange = null)
    {
        try
        {
            var dataDir = GetDataDirectory();
            
            // Parse optional date range parameter
            DateTime? startDate = null;
            DateTime? endDate = null;
            object? returnedDateRange = null;

            if (dateRange is not null)
            {
                try
                {
                    // Handle date range as dynamic object with start/end properties
                    var dateRangeDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        JsonSerializer.Serialize(dateRange));
                    
                    if (dateRangeDict is not null)
                    {
                        if (dateRangeDict.TryGetValue("start", out var startObj) && startObj is not null)
                        {
                            var startStr = startObj.ToString();
                            if (!string.IsNullOrEmpty(startStr) && 
                                DateTime.TryParse(startStr, out var parsedStart))
                            {
                                startDate = parsedStart;
                            }
                        }

                        if (dateRangeDict.TryGetValue("end", out var endObj) && endObj is not null)
                        {
                            var endStr = endObj.ToString();
                            if (!string.IsNullOrEmpty(endStr) && 
                                DateTime.TryParse(endStr, out var parsedEnd))
                            {
                                endDate = parsedEnd;
                            }
                        }
                    }

                    if (startDate.HasValue && endDate.HasValue)
                    {
                        returnedDateRange = new
                        {
                            start = startDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            end = endDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Invalid date range format: {ex.Message}");
                    // Continue without date filtering
                }
            }

            // Find all PanelData CSV files
            var panelFiles = Directory.GetFiles(dataDir, "PanelData_*.csv");
            
            if (panelFiles.Length is 0)
            {
                return new
                {
                    date_range = returnedDateRange,
                    panel_serials = new string[0],
                    panel_count = 0
                };
            }

            var uniqueSerials = new HashSet<string>();

            // Process each panel data file
            foreach (var filePath in panelFiles)
            {
                try
                {
                    // Check if file falls within date range if specified
                    if (startDate.HasValue || endDate.HasValue)
                    {
                        var filename = Path.GetFileName(filePath);
                        var match = Regex.Match(filename, @"(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})");
                        
                        if (match.Success is true)
                        {
                            var dateStr = match.Groups[1].Value;
                            var timeStr = match.Groups[2].Value.Replace('-', ':');
                            
                            if (DateTime.TryParseExact($"{dateStr} {timeStr}", "yyyy-MM-dd HH:mm:ss", 
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                            {
                                // Skip file if outside date range
                                if (startDate.HasValue && fileDate < startDate.Value)
                                    continue;
                                if (endDate.HasValue && fileDate > endDate.Value)
                                    continue;
                            }
                        }
                    }

                    // Read file and extract serial numbers
                    var lines = File.ReadAllLines(filePath);
                    if (lines.Length > 1) // Skip if only header or empty
                    {
                        var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line));
                        
                        foreach (var line in dataLines)
                        {
                            var fields = line.Split(',').Select(f => f.Trim()).ToArray();
                            
                            // SerialNumber is in column 1 (0-based indexing)
                            if (fields.Length > 1 && !string.IsNullOrEmpty(fields[1]))
                            {
                                uniqueSerials.Add(fields[1]);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error processing file {Path.GetFileName(filePath)}: {ex.Message}");
                    // Continue processing other files
                }
            }

            // Sort serial numbers for consistent output
            var sortedSerials = uniqueSerials.OrderBy(s => s).ToArray();

            return new
            {
                date_range = returnedDateRange,
                panel_serials = sortedSerials,
                panel_count = sortedSerials.Length
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetPanelSerials: {ex.Message}");
            throw new McpException($"Error discovering panel serial numbers: {ex.Message}");
        }
    }
}
