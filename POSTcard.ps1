param ([string]$filename)
# Define the URL and the data
$webhook = "<URL>"
Write-Output "POSTING FILE: $filename"

$filePath = ".\$filename"
Write-Output "POSTING FILEpath: $filePath"

$fileContent = Get-Content -Path $filePath
Write-Output $fileContent

# Make the POST request
$response = Invoke-RestMethod -Uri $webhook -Method Post -Body $fileContent -ContentType "application/json"

# Output the response
Write-Output $response

