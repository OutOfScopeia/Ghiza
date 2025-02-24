module Domain

open Azure.Monitor.Query.Models
open System
open Microsoft.Extensions.Logging

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
