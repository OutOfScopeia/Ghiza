# Define the URL and the data
$uri = "http://localhost:7071/api/AzureAlert"

$filePath = ".\cas-sample.json"
$fileContent = Get-Content -Path $filePath
Write-Output $fileContent

$body0 = @{
    key1 = "value1"
    key2 = "value2"
} | ConvertTo-Json

$body1 = $fileContent | ConvertTo-Json

# Make the POST request
$response = Invoke-RestMethod -Uri $uri -Method Post -Body $fileContent -ContentType "application/json"

# Output the response
Write-Output $response

