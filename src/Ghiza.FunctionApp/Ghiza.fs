module Ghiza

open Azure.Monitor.Query
open Azure.Monitor.Query.Models
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
//open Microsoft.Azure.Functions.Worker.Extensions.Timer
//open Microsoft.AspNetCore.Server.Kestrel.Core
open System
open System.IO
open System.Net.Http
open System.Text
open Thoth.Json.Net
open AdaptiveCards
open Newtonsoft.Json
open Cfg
open Domain
open Slack

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
    let minify (json:string) =
        json |> JsonConvert.DeserializeObject |> fun jsn -> JsonConvert.SerializeObject(jsn, Formatting.None)
    
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

    let getTeamsAdaptiveCardFormat2 (title:string) (replies: Reply seq) =
        let getMarkDownLink (text:string) (url:string) = $"[{text}]({url})"

        let seqToList (seq: seq<'T>) : Collections.Generic.List<'T> =
            let fsharpList = Seq.toList seq
            new Collections.Generic.List<'T>(fsharpList)

        let getTextBlock (str:string) =
            let txtBlock = new AdaptiveTextBlock(str)
            txtBlock.Wrap <- true
            txtBlock

        // combines avatar icon and handle link
        let getContainer (reply:Reply) =
            let image = AdaptiveImage()
            image.AltText <- $"{reply.Author.Name}"
            image.UrlString <- reply.Author.ProfileImageUrl
            image.Size <- AdaptiveImageSize.Small

            let txtBlock = getTextBlock (getMarkDownLink reply.Author.Handle reply.RootPostUrl)
            
            let container = AdaptiveContainer()
            container.Items <- [ image :> AdaptiveElement; txtBlock ] |> seqToList

            container
            
        let convertTable (replies: Reply seq) =
            let colNames = [ "Handle"; "Text" ]

            let getAdaptiveTableRow (reply: Reply) =

                let cells =
                    let e1 = reply |> getContainer
                    let e2 = (reply.TextTruncated 31) |> getTextBlock

                    let c1 = AdaptiveTableCell()
                    let c2 = AdaptiveTableCell()

                    c1.Type <- "TableCell"
                    c2.Type <- "TableCell"

                    c1.Items <- e1 :> AdaptiveElement |> Seq.singleton |> seqToList
                    c2.Items <- e2 :> AdaptiveElement |> Seq.singleton |> seqToList



                    [ c1; c2 ] |> seqToList

                    //|> Seq.map (fun str ->
                    //    let txtBlock = new AdaptiveTextBlock(str)
                    //    txtBlock.Wrap <- true
                    //    let cell = AdaptiveTableCell()
                    //    let container = AdaptiveContainer()
                    //    container.Items <- 
                    //    // Type override as by default the type is "Container" and it breaks the card
                    //    cell.Type <- "TableCell"
                    //    cell.Items <- container :> AdaptiveElement |> Seq.singleton |> seqToList
                    //    cell
                    //    )

                let atRow = new AdaptiveTableRow()
                atRow.Cells <- cells
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

            let atRows = replies |> Seq.map getAdaptiveTableRow
            let allRows = seq { yield atHeaderRow; yield! atRows }

            let aTableColumns = AdaptiveTableColumnDefinition()
            aTableColumns.Width <- 1
            aTableColumns.HorizontalContentAlignment <- AdaptiveHorizontalContentAlignment.Left
            
            let aTable = AdaptiveTable()
            aTable.Rows <- (allRows |> seqToList)
            aTable.Columns <- (aTableColumns |> Seq.replicate 2 |> seqToList)
            aTable.FirstRowAsHeaders <- true
            aTable.GridStyle <- AdaptiveContainerStyle.Accent

            aTable

        let tableElement = replies |> convertTable

        let descElement = AdaptiveTextBlock(title)
        descElement.Color <- AdaptiveTextColor.Good
        descElement.Wrap <- true
        descElement.Weight <- AdaptiveTextWeight.Bolder

        // No lower than 1.5 for Table support
        let card = AdaptiveCards.AdaptiveCard("1.5")

        card.Body <- (([ descElement; tableElement ] : List<AdaptiveElement>) |> seqToList)

        let getMsg = fun card -> $"""{{"type":"message","attachments":[{{"contentType":"application/vnd.microsoft.card.adaptive","contentUrl":null,"content":{card}}}]}}"""
            
        let c = card.ToJson() |> getMsg

        c

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

            // Bluesky link to replies. both forms work, but while the handle can change, did is robust.
            // https://bsky.app/profile/{handle}/post/3lhtdyygsjk26
            // https://bsky.app/profile/{did-identifier}/post/3lhtdyygsjk26
            
            // examples: 
            // https://bsky.app/profile/speakezai.bsky.social/post/3lhtdyygsjk26
            // https://bsky.app/profile/did:plc:igaby4nr77lndu3d3ws2muo3/post/3lhtdyygsjk26

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
            LogsTeamsWebhook = JsonUtils.getTeamsAdaptiveCardFormat
            LogsSlackWebhook = JsonUtils.getSlackTableAsCodeBlock
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
            LogsTeamsWebhook = JsonUtils.getTeamsAdaptiveCardFormat
            LogsSlackWebhook = JsonUtils.getSlackTableAsCodeBlock
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
                LogsTeamsWebhook = JsonUtils.getTeamsAdaptiveCardFormat
                LogsSlackWebhook = JsonUtils.getSlackTableAsCodeBlock
            }
            Logger = log
            Message = ""
        }


module Funcs =
    let getQueryResult (cfg:LogQueryConfig) =
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

    let postToWebhook (log:ILogger) (hook:string) (formatted:string) =
        use _ = log.BeginScope("Posting to webhook")
        //IO.File.WriteAllText ("Z:/Ghiza/slack-json-generated-preminify.json", formatted)
        let formatted = formatted |> JsonUtils.minify
        //IO.File.WriteAllText ("Z:/Ghiza/slack-json-generated-postminify.json", formatted)
        //log.LogInformation ($"POSTING: {formatted}")
        async {
            use client = new HttpClient()
            let content = new StringContent(formatted, Encoding.UTF8, "application/json")
            let! response = client.PostAsync(hook, content) |> Async.AwaitTask
            
            match response.IsSuccessStatusCode with
            | true ->
                log.LogInformation ("Successfully posted message to webhook.")
                return Ok ()
            | false ->
                let errorMsg = $"Failed to post message over hook. Status code: {response.StatusCode}"
                log.LogError(errorMsg)
                return Error errorMsg
        }


    let postToAll (result:Result<LogQueryConfig,LogQueryConfig>) =
        match result with
        | Ok cfg ->
            let formattedTeams = cfg.Formatters.LogsTeamsWebhook cfg.Table.Value cfg.Title
            let formattedSlack = cfg.Formatters.LogsSlackWebhook cfg.Table.Value cfg.Title

            let results =
                [
                    // Teams
                    postToWebhook cfg.Logger teamsAzureAlertsWebhook formattedTeams
                    // Slack
                    postToWebhook cfg.Logger slackAzureAlertsWebhook formattedSlack
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
    
    let runQuery (cfg:LogQueryConfig) =
        cfg
        |> getQueryResult
        |> postToAll

module SocialsReport =
    let runRepliesReport (timer:TimerInfo) (ctx:FunctionContext) =
        let log = ctx.GetLogger<LogPoller>()
        use _ = log.BeginScope($"{ctx.FunctionDefinition.Name}")
        log.LogInformation($"F# Timer trigger function '{ctx.FunctionDefinition.Name}' fired at: {timer.ScheduleStatus.LastUpdated}")

        let lastInvocation = if isRunningLocally then DateTime.UtcNow.AddDays(-5.) else timer.ScheduleStatus.Last

        let postToAll (payloadTeams: string) =
            let results =
                [
                    // Teams
                    Funcs.postToWebhook log teamsSocialAlertsWebhook payloadTeams
                    // Slack
                    //Funcs.postToWebhook log slackSocialAlertsWebhook payloadSlack
                ]
                |> Async.Parallel
                |> Async.RunSynchronously
                
            match results |> Array.exists(fun r -> r.IsError) with
            | true -> Error "Some POST operations failed."
            | false -> Ok "All POST operations succeeded."

        //let payloadTeams, payloadSlack =
        //    match ctx.FunctionDefinition.Name with
        //    | "RepliesReport_Bluesky" ->
        //        timer.ScheduleStatus.Last |> Bluesky.getNewReplies |> Result.map JsonUtils.getTeamsAdaptiveCardFormat2,
        //        timer.ScheduleStatus.Last |> Bluesky.getNewReplies |> Result.map Bluesky.getSlackTableAsBlocks

        //    | "RepliesReport_X" ->
        //        timer.ScheduleStatus.Last |> X.getNewReplies |> Result.map JsonUtils.getTeamsAdaptiveCardFormat2,
        //        timer.ScheduleStatus.Last |> X.getNewReplies |> Result.map X.getSlackTableAsCodeBlock

        //    | _ ->
        //        failwith "Failed to determine function name from context."

        let payloadTeams, payloadSlack =
            let titleTemplate = "New replies on"
            match ctx.FunctionDefinition.Name with
            | "RepliesReport_Bluesky" ->
                let getNewReplies = ctx |> Bluesky.getNewReplies
                let title = $"{titleTemplate} Bluesky"
                timer.ScheduleStatus.Last |> getNewReplies |> Result.map (JsonUtils.getTeamsAdaptiveCardFormat2 title),
                timer.ScheduleStatus.Last |> getNewReplies |> Result.map Bluesky.getSlackTableAsBlocks

            | "RepliesReport_X" ->
                let title = $"{titleTemplate} X"
                //let getNewReplies = ctx |> X.getNewReplies
                timer.ScheduleStatus.Last |> X.getNewReplies |> Result.map (JsonUtils.getTeamsAdaptiveCardFormat2 title),
                timer.ScheduleStatus.Last |> X.getNewReplies |> Result.map X.getSlackTableAsCodeBlock

            | _ ->
                failwith "Failed to determine function name from context."

        payloadTeams |> Result.bind postToAll |> Result.mapError (fun e -> log.LogError e)

[<Function("RepliesReport_Bluesky")>]
let runRepliesReportBluesky ([<TimerTrigger("%CRON_REPLIES_REPORT_BLUESKY%")>] timer: TimerInfo, ctx: FunctionContext) =
    SocialsReport.runRepliesReport timer ctx

//[<Function("RepliesReport_X")>]
//let runRepliesReportX ([<TimerTrigger("%CRON_REPLIES_REPORT_X%")>] timer: TimerInfo, ctx: FunctionContext) =
//    SocialsReport.runRepliesReport timer ctx

[<Function("SignIns")>]
// Cron expression sourced from app settings: daily at 6PM "0 0 18 * * *"
let runSignIns ([<TimerTrigger("%CRON_SIGNINS%")>] timer: TimerInfo, ctx: FunctionContext) =
    let log = ctx.GetLogger<LogPoller>()
    log.LogInformation($"F# Timer trigger function '{ctx.FunctionDefinition.Name}' fired at: {timer.ScheduleStatus.LastUpdated}")

    log
    |> SignIns.config
    |> Funcs.runQuery
    
[<Function("CreatedServicePrincipals")>]
// Cron expression sourced from app settings: daily at 6PM "0 18 * * *"
let runCreatedServicePrincipals ([<TimerTrigger("%CRON_CREATED_SERVICE_PRINCIPALS%")>] timer: TimerInfo, ctx: FunctionContext) =
    let log = ctx.GetLogger<LogPoller>()
    use _ = log.BeginScope($"{ctx.FunctionDefinition.Name}")
    log.LogInformation(sprintf $"F# Timer trigger function '{ctx.FunctionDefinition.Name}' fired at: {timer.ScheduleStatus.LastUpdated}")

    log
    |> ServicePrincipalCreations.config
    |> Funcs.runQuery

[<Function("StaleServicePrincipals")>]
// Cron expression sourced from app settings: every Monday at 10AM "0 10 * * 1"
let runStaleServicePrincipals ([<TimerTrigger("%CRON_STALE_SERVICE_PRINCIPALS%")>] timer: TimerInfo, ctx: FunctionContext) =
    let log = ctx.GetLogger<LogPoller>()
    use _ = log.BeginScope($"{ctx.FunctionDefinition.Name}")
    log.LogInformation($"F# Timer trigger function '{ctx.FunctionDefinition.Name}' fired at: {timer.ScheduleStatus.LastUpdated}")

    log
    |> StaleServicePrincipals.config
    |> Funcs.runQuery

[<Function("AzureAlert")>]
let runAzureAlert ([<HttpTrigger>] req: HttpRequestData) (ctx: FunctionContext) =
    
    let log = ctx.GetLogger<LogPoller>()
    let logPrefix = sprintf "[%s]: %s" ctx.FunctionDefinition.Name

    let getRequestBody (req:HttpRequestData) =
        async {
            try
                use reader = new StreamReader(req.Body)
                let! body = reader.ReadToEndAsync() |> Async.AwaitTask
                return (Ok body)
            with
            | ex ->
                log.LogError($"{ex.Message}" |> logPrefix)
                return Error ex.Message
        }

    let getIncomingPayload (body:string) =
        async {
            let result =
                match body |> Payload.Essentials.FromCommonAlertSchema with
                | Ok es ->
                    log.LogInformation("Successfully parsed incoming payload." |> logPrefix)
                    Ok es
                | Error e ->
                    Error e
            
            return result
        }

    let getOutgoingPayloadTeams (e:Payload.Essentials) =
        let payload = $"ALERT: {e.firedDateTime} - {e.alertRule} - {e.description} - {e.monitorCondition}"
        $"{{\"text\":\"{payload}\"}}"
    

    log.LogInformation(sprintf $"F# HttpTrigger function '{ctx.FunctionDefinition.Name}' fired at: {DateTime.Now}" |> logPrefix)

    let runAsync (formatter:FormatMsg) (req:HttpRequestData) =
        // REFACTOR THIS WITH A CLEARER BRAIN
        // ACCOMODATE MORE ALERT TYPES - SOME OF THE TEST PAYLOADS ARE NOT POSTING
        async {
            match! req |> getRequestBody with
            | Ok body ->
                match! body |> getIncomingPayload with
                | Ok essentials ->
                    let outPayload = essentials |> formatter
                    let postedMsgTeams = Funcs.postToWebhook log teamsAzureAlertsWebhook outPayload
                    let postedMsgSlack = Funcs.postToWebhook log slackAzureAlertsWebhook outPayload

                    let! posts = Async.Parallel [ postedMsgTeams; postedMsgSlack]

                    let r =
                        match posts |> Array.forall (fun r -> r.IsOk) with
                        | true -> Ok ()
                        | false -> Error "fr"
                    return r

                | Error e -> return Error e
            | Error e -> return Error e
        }
     
    runAsync getOutgoingPayloadTeams req
    |> Async.RunSynchronously