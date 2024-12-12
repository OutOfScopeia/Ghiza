module CIT.Ghiza

open Azure.Identity
open Azure.Monitor.Query
open Azure.Monitor.Query.Models
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Graph
//open Microsoft.Azure.Functions.Worker.Extensions.Timer
//open Microsoft.AspNetCore.Server.Kestrel.Core
open System
open System.IO
open System.Net.Http
open System.Text
open Thoth.Json.Net
open AdaptiveCards
open Newtonsoft.Json

//let isInLocal = String.IsNullOrEmpty(Environment.GetEnvironmentVariable "WEBSITE_INSTANCE_ID") // if not running in the cloud
let lookbackMinutesSignIns = Environment.GetEnvironmentVariable "LOOKBACK_MINUTES_SIGNINS"
let lookbackMinutesSPCreations = Environment.GetEnvironmentVariable "LOOKBACK_MINUTES_SPCREATIONS"
let tenantId = Environment.GetEnvironmentVariable "TENANT_ID"
let clientId = Environment.GetEnvironmentVariable "CLIENT_ID"
let clientSecret = Environment.GetEnvironmentVariable "CLIENT_SECRET"
let workspaceId = Environment.GetEnvironmentVariable "WORKSPACE_ID"
let teamsWebhook = Environment.GetEnvironmentVariable "TEAMS_MONITORING_CHANNEL_WEBHOOK"
let slackWebhook = Environment.GetEnvironmentVariable "SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK"

let credential = ClientSecretCredential(tenantId, clientId, clientSecret)
let logsQueryClient = LogsQueryClient(credential)
let graphClient = new GraphServiceClient(credential)

let host =
    HostBuilder()
        .ConfigureFunctionsWebApplication()
        //.ConfigureFunctionsWorkerDefaults()
        .ConfigureServices(fun services -> 
            services.AddApplicationInsightsTelemetryWorkerService() |> ignore
            services.ConfigureFunctionsApplicationInsights() |> ignore
            //services.AddLogging() |> ignore
        )
        .Build()

host.RunAsync() |> Async.AwaitTask |> Async.RunSynchronously

type LogPoller = Dummy

type Formatters = {
    TeamsWebhook: LogsTable -> string -> string
    SlackWebhook: LogsTable -> string -> string
}

type QueryConfig = {
    Title: string
    Query: string
    Table: LogsTable option
    QueryTimeRange: TimeSpan
    Formatters: Formatters
    Logger: ILogger
    Message: string
}

//module HtmlUtils =
//    let getHtmlTable (tableBkgColor:string) (table: LogsTable) =
//        let getHeader str = $"<th>{str}</th>"
//        let getCell str = $"<td>{str}</td>" 
//        let columnNames = table.Columns |> Seq.map (fun col -> col.Name)
//        let columnHeadersHtml = columnNames |> Seq.map getHeader |> String.concat ""

//        let getRowHtml (row:LogsTableRow) =
//            let cellsHtml =
//                columnNames
//                |> Seq.map (fun column -> (row.Item column) |> string)
//                |> Seq.map getCell
//                |> String.concat ""
            
//            $"<tr>{cellsHtml}</tr>"

//        let rowsHtml = table.Rows |> Seq.map getRowHtml |> String.concat ""
//        let colgroup = $"""<colgroup><col span="2" style="background-color:{tableBkgColor}"></colgroup>"""
//        let colgroups = colgroup |> Seq.replicate (columnNames |> Seq.length) |> String.concat ""
//        $"""<p style="color:#EAAA00;"><table border="1" {colgroups}<tr>{columnHeadersHtml}</tr>{rowsHtml}</table></p>"""

module JsonUtils =
    // Slack UI "domain" - ghetto implementation (illegal template state is very much representable)
    type MarkDownStyle = {
        Bold: bool
        Italics: bool
        StrikeThrough: bool
    }
    with
        static member Plain        = { Bold = false; Italics = false; StrikeThrough = false }
        member x.MakeBold          = { x with Bold = true }
        member x.makeItalics       = { x with Italics = true }
        member x.MakeStrikeThrough = { x with StrikeThrough = true }

    type TextType =
        | Plain
        | Markdown of MarkDownStyle
        override x.ToString() = match x with Plain -> "plain_text" | Markdown style -> "mrkdwn"

    type Block =
        | Text of TextType * string
        | Header of Block
        | Section of Block list
        | Blocks of Block list
        /// No blocks, just plain text
        | JustText of string

    with
        override x.ToString() =
            let invalid = """{"message":"Invalid Block Kit construct."}""" 
            
            match x with 
            | Text (ttype, txt) ->
                let bold, italic, strike =
                    match ttype with
                    | Markdown style -> (if style.Bold then "*" else ""), (if style.Italics then "_" else ""), (if style.StrikeThrough then "~" else "")
                    | Plain -> "", "", ""

                $"""{{"type":"{ttype}","text":"{strike}{italic}{bold}{txt}{bold}{italic}{strike}"}}"""

            | Header block when block.IsText -> $"""{{"type":"header","text":{block}}}"""
            | Section (x::[]) when x.IsText  -> $"""{{"type":"section","text":{(x.ToString())}}}"""
            | Section fields                 -> $"""{{"type":"section","fields":[{(fields |> Seq.map string |> String.concat ",")}]}}"""
            | Blocks sections                -> $"""{{"blocks":[{(sections |> Seq.map string |> String.concat ",")}]}}"""
            | JustText txt                   -> $"""{{"text":"{txt}"}}"""
            | _ -> invalid

    let minify (json:string) = json |> JsonConvert.DeserializeObject |> fun jsn -> JsonConvert.SerializeObject(jsn, Formatting.None)
    
    let getTeamsAdaptiveCardFormat (lTable: LogsTable) (title:string) =
        let seqToList (seq: seq<'T>) : System.Collections.Generic.List<'T> =
            let fsharpList = Seq.toList seq
            new System.Collections.Generic.List<'T>(fsharpList)
            
        let convertTable (lTable:LogsTable) =
            let colNames = lTable.Columns |> Seq.map(fun col -> col.Name)

            let getAdaptiveCellRow (row:LogsTableRow) =
                let cells =
                    colNames
                    |> Seq.map (fun cname ->
                        let v = row.GetString cname
                        let txtBlock = new AdaptiveTextBlock(v)
                        txtBlock.Wrap <- true
                        let cell = AdaptiveTableCell()
                        // Type override as by default the type is "Container" and it breaks the card
                        cell.Type <- "TableCell"
                        cell.Items <- txtBlock :> AdaptiveElement |> Seq.singleton |> seqToList
                        cell
                        )

                let atRow = new AdaptiveTableRow()
                atRow.Cells <- (cells |> seqToList)
                atRow.Style <- AdaptiveContainerStyle.Accent
                atRow

            let getHeaderCell (cname:string) =
                let txtBlock = new AdaptiveTextBlock(cname) :> AdaptiveElement |> Seq.singleton |> seqToList
                let cell = AdaptiveTableCell()
                // Type override as by default the type is "Container" and it breaks the card
                cell.Type <- "TableCell"
                cell.Items <- txtBlock
                cell

            let atHeaderRow =
                let hRow = new AdaptiveTableRow()
                hRow.Cells <- (colNames |> Seq.map getHeaderCell |> seqToList) 
                hRow

            let atRows = lTable.Rows |> Seq.map getAdaptiveCellRow
            let allRows = seq { yield atHeaderRow; yield! atRows }

            let aTable = AdaptiveTable()
            let aTableColumns = AdaptiveTableColumnDefinition()
            aTableColumns.Width <- 1
            aTableColumns.HorizontalContentAlignment <- AdaptiveHorizontalContentAlignment.Left
            aTable.Rows <- (allRows |> seqToList)
            aTable.Columns <- (aTableColumns |> Seq.replicate (lTable.Columns.Count) |> seqToList)
            aTable.FirstRowAsHeaders <- true
            aTable.GridStyle <- AdaptiveContainerStyle.Accent

            aTable

        let tableElement = lTable |> convertTable

        let descElement = AdaptiveTextBlock(title)
        descElement.Color <- AdaptiveTextColor.Good
        descElement.Wrap <- true
        descElement.Weight <- AdaptiveTextWeight.Bolder

        // No lower than 1.5 for Table support
        let card = AdaptiveCards.AdaptiveCard("1.5")

        card.Body <- (([descElement; tableElement] : List<AdaptiveElement>) |> seqToList)

        let getMsg = fun card -> $"""{{"type":"message","attachments":[{{"contentType":"application/vnd.microsoft.card.adaptive","contentUrl":null,"content":{card}}}]}}"""
            
        card.ToJson() |> getMsg

    let getSlackTableAsCodeBlock (lTable: LogsTable) (title:string) =
        let colNames = lTable.Columns |> Seq.map(fun col -> col.Name) |> Seq.toList
        let colWidths =
            let cols = colNames |> Seq.map String.length
            let rows = lTable.Rows |> Seq.map (fun row -> colNames |> Seq.map (fun cname -> (row.GetString cname) |> String.length))
            
            [cols; yield! rows]
            |> Seq.transpose
            |> Seq.map Seq.max

        let codeBlock =
            let padCell (str:string) (colWidth:int) = str.PadRight colWidth

            let walledtableEdge =
                colWidths
                |> Seq.map (fun cw -> (String.replicate cw "-"))
                |> String.concat "-|-"
                |> fun s -> $"+-{s}-+"
            
            let getWalledRow (cells:string seq) =
                Seq.map2 (fun cname cwidth -> padCell cname cwidth) cells colWidths
                |> String.concat " | "
                |> fun s -> $"| {s} |"

            /// produces codeblock, example:
            /// +--------------------------------------|-------------|---------------------+
            /// | AppId                                | DisplayName | InitiatedBy         |
            /// +--------------------------------------|-------------|---------------------+
            /// | f45734c3-fea2-4ddf-5f90-7618cad9c0ba | test-sp1    | joe.bloggs@acme.com |
            /// +--------------------------------------|-------------|---------------------+
            let allRows =
                seq {
                    yield title
                    yield "```"
                    yield walledtableEdge
                    yield getWalledRow colNames
                    yield walledtableEdge
                    yield! lTable.Rows |> Seq.map (fun (row:LogsTableRow) -> colNames |> Seq.map (fun cname -> row.GetString(cname))) |> Seq.map getWalledRow
                    yield walledtableEdge
                    yield "```"
                }
                |> fun rows ->
                    let rowsAsOne = rows |> String.concat "\\n"
                    rowsAsOne

            allRows
        
        let cblock = codeBlock |> Block.JustText

        cblock |> string

    let getSlackBlocksFormat (lTable: LogsTable) (title:string) =

        let convertTable (lTable: LogsTable) =
            let colNames = lTable.Columns |> Seq.map(fun col -> col.Name) |> Seq.toList

            let convertRow (row:LogsTableRow) = colNames |> List.map (row.GetString >> (fun s -> (TextType.Markdown (MarkDownStyle.Plain), s) |> Block.Text)) |> Block.Section

            let titleHeader = (TextType.Plain, title) |> Block.Text |> Block.Header
            let tableHeader = colNames |> List.map (fun s -> (TextType.Markdown MarkDownStyle.Plain.MakeBold, s) |> Block.Text) |> Block.Section
            let rowSections = lTable.Rows |> Seq.map convertRow
            
            [
                yield titleHeader
                yield tableHeader
                yield! rowSections
            ]
            |> Block.Blocks
            |> string
            
        lTable |> convertTable

module Payload =

    type Essentials = {
        alertId: string
        alertRule: string
        severity: string
        signalType: string
        monitorCondition: string
        monitoringService: string
        alertTargetIDs: string list
        configurationItems: string list
        originAlertId: string
        firedDateTime: DateTime
        description: string
        alertRuleID: string option
        essentialsVersion: string
        alertContextVersion: string
        resolvedDateTime: DateTime option
        resourceType: string option
        resourceGroupName: string option
        intestigationLink: string option
    }

    with
        static member Decoder = 
            Decode.object (fun get ->
                {
                    alertId = get.Required.Field "alertId" Decode.string
                    alertRule = get.Required.Field "alertRule" Decode.string
                    severity = get.Required.Field "severity" Decode.string
                    signalType = get.Required.Field "signalType" Decode.string
                    monitorCondition = get.Required.Field "monitorCondition" Decode.string
                    monitoringService = get.Required.Field "monitoringService" Decode.string
                    alertTargetIDs = get.Required.Field "alertTargetIDs" (Decode.list Decode.string)
                    configurationItems = get.Required.Field "configurationItems" (Decode.list Decode.string)
                    originAlertId = get.Required.Field "originAlertId" Decode.string
                    firedDateTime = get.Required.Field "firedDateTime" Decode.datetimeUtc
                    description = get.Required.Field "description" Decode.string
                    essentialsVersion = get.Required.Field "essentialsVersion" Decode.string
                    alertContextVersion = get.Required.Field "alertContextVersion" Decode.string
                    alertRuleID = get.Optional.Field "alertRuleID" Decode.string
                    resolvedDateTime = get.Optional.Field "resolvedDateTime" Decode.datetimeUtc
                    resourceType = get.Optional.Field "resourceType" Decode.string
                    resourceGroupName = get.Optional.Field "resourceGroupName" Decode.string
                    intestigationLink = get.Optional.Field "intestigationLink" Decode.string
                }
            )

        static member FromCommonAlertSchema jsonString =
            let partialJson = Decode.field "data" (Decode.field "essentials" Essentials.Decoder)
            Decode.fromString partialJson jsonString

type FormatMsg = Payload.Essentials -> string

module SignIns =
    // SCRUTINISE THIS
    let time () = DateTime.Now
    
    let timeSpan () = TimeSpan.FromMinutes(float lookbackMinutesSignIns)

    // MIND THE DISTINCT
    let query () =
        $"""
        SigninLogs
        | where Location !in~ ('GB','')
        | project UserPrincipalName, Location
        | distinct *
        """
    let title() = $"{time()} | Non-UK Sign-ins detected in the last {timeSpan().TotalHours |> int} hours:"

    let tableBkgColor = "Navy"

    let config log = {
        Query = query()
        Title = title()
        Table = None
        QueryTimeRange = timeSpan()
        Formatters = {
            TeamsWebhook = JsonUtils.getTeamsAdaptiveCardFormat
            SlackWebhook = JsonUtils.getSlackTableAsCodeBlock
        }
        Logger = log
        Message = ""
    }

module ServicePrincipalCreations =
    // SCRUTINISE THIS
    let time () = DateTime.Now
    
    let timeSpan () = TimeSpan.FromMinutes(float lookbackMinutesSPCreations)
    
    let query () =
        $"""
        AuditLogs
        | where OperationName =~ 'Add application'
        | where Result =~ 'Success'
        | project TargetResources[0].displayName, InitiatedBy.user.userPrincipalName, TargetResources[0].id
        | project AppId=TargetResources_0_id, DisplayName=TargetResources_0_displayName, InitiatedBy=InitiatedBy_user_userPrincipalName
        """

    let title() = $"{time()} | Service Principals created in the last {timeSpan().TotalHours |> int} hours:"

    let tableBkgColor = "Indigo"

    let config log = {
        Query = query()
        Title = title()
        Table = None
        QueryTimeRange = timeSpan()
        Formatters = {
            TeamsWebhook = JsonUtils.getTeamsAdaptiveCardFormat
            SlackWebhook = JsonUtils.getSlackTableAsCodeBlock
        }
        Logger = log
        Message = ""
    }

module StaleServicePrincipals =
    // SCRUTINISE THIS

    let config (log:ILogger) =
        
        use _ = log.BeginScope("Config")

        let time () = DateTime.Now

        let getDirApps (log:ILogger) =
            let appsTask = graphClient.Applications.GetAsync()
            appsTask.Wait()
            printfn $"{appsTask.Status}"
            // All Service Principals in the Entra ID Directory
            log.LogInformation($"Dir Apps count: {appsTask.Result.Value |> Seq.length}")
            appsTask.Result.Value

        let dirAppsString () =
            log
            |> getDirApps
            // app.Id = Object ID, app.AppId = Application (client) ID.
            |> Seq.collect (fun app -> [ app.AppId; app.DisplayName ])
            |> Seq.map (fun s -> $"\"{s}\"")
            |> String.concat ","
    
        let query () =
            $"""
            let dirApps = datatable(AppId: string, ServicePrincipalName: string)
                [{dirAppsString()}]
            ;
            let activeApps =
                AADServicePrincipalSignInLogs
                | project AppId, ServicePrincipalName
                | distinct *
            ;
            dirApps
            | where AppId !in ((activeApps | distinct AppId))
            | order by ServicePrincipalName
            """

        let title () = $"{time()} | Service Principals not used in the the entire log history (90 days):"

        let tableBkgColor = "Purple"

        {
            Query = query()
            Title = title()
            Table = None
            QueryTimeRange = TimeSpan.MaxValue
            Formatters = {
                TeamsWebhook = JsonUtils.getTeamsAdaptiveCardFormat
                SlackWebhook = JsonUtils.getSlackTableAsCodeBlock
            }
            Logger = log
            Message = ""
        }

module Funcs =
    let getQueryResult (cfg:QueryConfig) =
        let response =
            logsQueryClient.QueryWorkspaceAsync(workspaceId, cfg.Query, QueryTimeRange(cfg.QueryTimeRange))
            |> Async.AwaitTask
            |> Async.RunSynchronously

        match response.Value with
        | null ->
            Error { cfg with Message = "Query reponse is null" }

        | lqr ->
            match lqr.Status with
            | LogsQueryResultStatus.Success when lqr.Table.Rows.Count > 0 ->
                Ok { cfg with Table = Some lqr.Table }

            | LogsQueryResultStatus.Success ->
                Error { cfg with Message = "Query executed OK but returned an empty table" }

            | LogsQueryResultStatus.Failure ->
                Error { cfg with Message = $"Query result failed with {lqr.Error.Code}: {lqr.Error.Message}" }
            
            | LogsQueryResultStatus.PartialFailure ->
                Error { cfg with Message = $"Query result partially failed with {lqr.Error.Code}: {lqr.Error.Message}" }
            
            | _ ->
                Error { cfg with Message = "Query result produced <unknown enum> status" }

    let time = DateTime.Now

    let postToWebhook (formatted:string) (hook:string) (log:ILogger) =
        use _ = log.BeginScope("Posting to webhook")

        let formatted = formatted |> JsonUtils.minify
        //IO.File.WriteAllText ("E:\Ghiza\slack-json-generated.json", formatted)
        //log.LogInformation ($"POSTING: {formatted}")
        async {
            use client = new HttpClient()
            let content = new StringContent(formatted, Encoding.UTF8, "application/json")
            let! response = client.PostAsync(hook, content) |> Async.AwaitTask
            
            match response.IsSuccessStatusCode with
            |true ->
                log.LogInformation ("Successfully posted message to webhook.")
                return Ok ()
            | false ->
                let errorMsg = $"Failed to post message over hook. Status code: {response.StatusCode}"
                log.LogError(errorMsg)
                return Error errorMsg
        }

    let postToAll (result:Result<QueryConfig,QueryConfig>) =
        match result with
        | Ok cfg ->
            let formattedTeams = cfg.Formatters.TeamsWebhook cfg.Table.Value cfg.Title
            let formattedSlack = cfg.Formatters.SlackWebhook cfg.Table.Value cfg.Title

            let results =
                [
                    // Teams
                    postToWebhook formattedTeams teamsWebhook cfg.Logger
                    // Slack
                    postToWebhook formattedSlack slackWebhook cfg.Logger
                ]
                |> Async.Parallel
                |> Async.RunSynchronously
                
            match results |> Array.exists(fun r -> r.IsError) with
            | true -> Error "Some POST operations failed."
            | false -> Ok "All POST operations succeeded."
        
        | Error data -> Error data.Message

    //let logOutcome (result:Result<QueryConfig,QueryConfig>) =
    //    match result with
    //    | Ok cfg ->
    //        cfg.Logger.LogInformation(sprintf $"SUCCESS: Post returned: {cfg.Message}")
    //    | Error cfg ->
    //        cfg.Logger.LogError(sprintf $"ERROR: {cfg.Message}")


    let runQuery (cfg:QueryConfig) =
        cfg
        |> getQueryResult
        |> postToAll

[<Function("SignIns")>]
// Cron expression sourced from app settings: daily at 6PM "0 0 18 * * *"
let runSignIns ([<TimerTrigger("%CRON_SIGNINS%")>] timer: TimerInfo, context: FunctionContext) =
    let log = context.GetLogger<LogPoller>()
    log.LogInformation($"F# Timer trigger function 'SignIns' fired at: {timer.ScheduleStatus.LastUpdated}")

    log
    |> SignIns.config
    |> Funcs.runQuery
    
[<Function("CreatedServicePrincipals")>]
// Cron expression sourced from app settings: daily at 6PM "0 18 * * *"
let runCreatedServicePrincipals ([<TimerTrigger("%CRON_CREATED_SERVICE_PRINCIPALS%")>] timer: TimerInfo, ctx: FunctionContext) =
    let log = ctx.GetLogger<LogPoller>()
    use _ = log.BeginScope($"{ctx.FunctionDefinition.Name}")
    log.LogInformation(sprintf $"F# Timer trigger function 'CreatedServicePrincipals' fired at: {timer.ScheduleStatus.LastUpdated}")

    log
    |> ServicePrincipalCreations.config
    |> Funcs.runQuery

[<Function("StaleServicePrincipals")>]
// Cron expression sourced from app settings: every Monday at 10AM "0 10 * * 1"
let runStaleServicePrincipals ([<TimerTrigger("%CRON_STALE_SERVICE_PRINCIPALS%")>] timer: TimerInfo, ctx: FunctionContext) =
    let log = ctx.GetLogger<LogPoller>()
    use _ = log.BeginScope($"{ctx.FunctionDefinition.Name}")
    log.LogInformation($"F# Timer trigger function 'StaleServicePrincipals' fired at: {timer.ScheduleStatus.LastUpdated}")

    log
    |> StaleServicePrincipals.config
    |> Funcs.runQuery

[<Function("AzureAlert")>]
let runAzureAlert ([<HttpTrigger>] req: HttpRequestData) (context: FunctionContext) =
    
    let log = context.GetLogger<LogPoller>()
    let logPrefix = sprintf "[%s]: %s" context.FunctionDefinition.Name

    let formatForTeams (e:Payload.Essentials) = $"ALERT: {e.firedDateTime} - {e.alertRule} - {e.description} - {e.monitorCondition}"
    
    let decodeIncomingPayload (req:HttpRequestData) =
        async {
            use reader = new StreamReader(req.Body)
            let! requestBody = reader.ReadToEndAsync() |> Async.AwaitTask
            
            let result =
                match requestBody |> Payload.Essentials.FromCommonAlertSchema with
                | Ok es ->
                    log.LogInformation("Successfully parsed incoming payload." |> logPrefix)
                    Ok es
                | Error e ->
                    Error e
            return result
        }

    let postToHook (formatter:FormatMsg) (r:Result<Payload.Essentials,string>) =
        async {
            match r with
            | Ok p ->
                let msgJson = $"{{\"text\":\"{p |> formatter}\"}}"
                use client = new HttpClient()
                let content = new StringContent(msgJson, Encoding.UTF8, "application/json")
                let! response = client.PostAsync(teamsWebhook, content) |> Async.AwaitTask
            
                match response.IsSuccessStatusCode with
                |true ->
                    log.LogInformation ("Successfully posted Teams message to webhook." |> logPrefix)
                    return Ok ()
                | false ->
                    let errorMsg = $"Failed to post message over hook. Status code: {response.StatusCode}"
                    log.LogError(errorMsg |> logPrefix)
                    return Error errorMsg

            | Error e ->
                return Error e
        }

    log.LogInformation(sprintf $"F# HttpTrigger function 'AzureAlert' fired at: {DateTime.Now}" |> logPrefix)

    let runAsync formatter req =
        async {
            let! decodedPayload = req |> decodeIncomingPayload
            let! postedMsg = postToHook formatter decodedPayload

            match postedMsg with
            | Ok _ -> ()
            | Error e -> log.LogError($"Run ended with error: {e}" |> logPrefix)
        }
    
    runAsync formatForTeams req
    |> Async.RunSynchronously
