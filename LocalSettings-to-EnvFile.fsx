open System.IO
open System.Text.Json

// Define the file paths
let jsonFilePath = ".\src\Ghiza.FunctionApp\local.settings.json"
let envFilePath = "variables.env"

// Read JSON file
let readJsonFile path =
    if File.Exists(path) then
        File.ReadAllText(path)
    else
        failwith $"File not found: {path}"

// Parse JSON and extract environment variables
let parseJson (jsonContent:string) =
    let jsonDoc = JsonDocument.Parse(jsonContent)
    let root = jsonDoc.RootElement
    let mutable jelem = new JsonElement()
    if root.TryGetProperty("Values", &jelem) then
        root.GetProperty("Values").EnumerateObject()
        |> Seq.map (fun kv -> $"{kv.Name}=\"{kv.Value.GetString()}\"")
        |> Seq.toList
    else
        failwith "No 'Values' section found in JSON"

// Write to .env file
let writeEnvFile path envVars =
    File.WriteAllLines(path, envVars)
    printfn "Successfully wrote environment variables to %s" path

// Execute script
let jsonContent = readJsonFile jsonFilePath
let envVars = parseJson jsonContent

envVars |> Array.ofList |> writeEnvFile envFilePath