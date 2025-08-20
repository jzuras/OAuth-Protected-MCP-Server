<#
.SYNOPSIS
    This script finds all CSV files matching the pattern "PanelData_*.csv" in a specified
    directory and replaces the original serial numbers with generic, anonymized ones like "Panel_1", "Panel_2", etc.

.DESCRIPTION
    The script performs the following steps for each file:
    1. Reads the CSV data.
    2. Finds all unique serial numbers within that file.
    3. Creates a mapping from each unique original serial number to a new generic one (e.g., "Panel_1").
    4. Iterates through every row and replaces the 'SerialNumber' value based on the created map.
    5. Manually builds new CSV content as strings to avoid using quotes.
    6. Saves the modified, quote-less data to a new file with the suffix "_modified.csv".

.PARAMETER DirectoryPath
    The full path to the folder containing the CSV files you want to process. This parameter is mandatory.

.EXAMPLE
    .\Update-SerialNumbers-NoQuotes.ps1 -DirectoryPath "C:\Users\YourUser\Documents\PanelData"

    This command will process all "PanelData_*.csv" files located in the specified folder and produce
    output files where neither the headers nor the data fields are enclosed in quotes.
#>
param (
    [Parameter(Mandatory = $true)]
    [string]$DirectoryPath
)

# Check if the provided directory exists
if (-not (Test-Path -Path $DirectoryPath -PathType Container)) {
    Write-Error "Error: The specified directory does not exist: $DirectoryPath"
    return # Stop the script
}

# Find all CSV files that match the specified naming pattern
$filesToProcess = Get-ChildItem -Path $DirectoryPath -Filter "PanelData_*.csv"

# Check if any files were found
if ($null -eq $filesToProcess) {
    Write-Warning "No files matching 'PanelData_*.csv' were found in the directory: $DirectoryPath"
    return # Stop the script
}

Write-Host "Found $($filesToProcess.Count) files to process." -ForegroundColor Green

# Loop through each file found
foreach ($file in $filesToProcess) {
    Write-Host "Processing file: $($file.Name)..." -ForegroundColor Yellow

    # Import the CSV data from the current file
    $csvData = Import-Csv -Path $file.FullName

    # Stop processing this file if it's empty
    if ($null -eq $csvData) {
        Write-Warning "  -> File '$($file.Name)' is empty or invalid. Skipping."
        continue # Move to the next file
    }

    # Create a mapping (hashtable) to store original serial numbers and their new fake counterparts
    $serialNumberMap = @{}
    $panelCounter = 1

    # Find all unique serial numbers in the current file
    $uniqueSerialNumbers = $csvData.SerialNumber | Select-Object -Unique

    # Populate the map: For each unique serial, assign it a new "Panel_X" name
    foreach ($originalSN in $uniqueSerialNumbers) {
        if (-not [string]::IsNullOrWhiteSpace($originalSN)) {
            $serialNumberMap[$originalSN] = "Panel_$($panelCounter)"
            $panelCounter++
        }
    }

    # Now, loop through the data again and replace the serial number in each row
    foreach ($row in $csvData) {
        $originalSN = $row.SerialNumber
        if ($serialNumberMap.ContainsKey($originalSN)) {
            $row.SerialNumber = $serialNumberMap[$originalSN]
        }
    }

    # --- Manually build the new CSV content to avoid quotes ---
    # This is necessary because Export-Csv in Windows PowerShell 5.1 (default on Win 11)
    # does not have a simple switch to disable quoting entirely.

    # 1. Get the headers from the first data object
    $headers = $csvData[0].PSObject.Properties.Name
    $headerLine = $headers -join ','

    # 2. Create an array to hold all lines of our new file, starting with the header
    $linesForNewFile = @($headerLine)

    # 3. Process each data row and convert it to a comma-separated string
    foreach ($row in $csvData) {
        # Create an ordered array of the values for the current row
        $values = foreach ($header in $headers) {
            $row.$header
        }
        # Join the values with a comma and add the resulting string to our lines array
        $linesForNewFile += ($values -join ',')
    }

    # Define the name for the new output file
    $newFileName = "$($file.BaseName)_modified.csv"
    $newFilePath = Join-Path -Path $file.DirectoryName -ChildPath $newFileName

    # 4. Write all the constructed lines to the new file, overwriting if it exists
    Set-Content -Path $newFilePath -Value $linesForNewFile

    Write-Host "  -> Successfully created quote-less modified file: $newFileName" -ForegroundColor Cyan
}

Write-Host "`nAll files have been processed." -ForegroundColor Green