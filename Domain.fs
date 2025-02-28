﻿module Domain

open Azure.Monitor.Query.Models
open Cfg
open Microsoft.Extensions.Logging
open Newtonsoft.Json
open System

type LogPoller = Dummy

type JsonString = Json of string

type Author = {
        Id: string
        Name: string
        Handle: string
        ProfileImageUrl: string
    }

type Reply = {
    RootPostUrl: string
    ReplyPostUrl: string
    CreatedAtUtc: DateTime
    Text: string
    Author: Author
}

type LogQueryFormatters = {
    LogsTeamsWebhook: LogsTable -> string -> string
    LogsSlackWebhook: LogsTable -> string -> string
}

//type SocialsFormatters = {
//    SocialsTeamsWebhook: JsonString -> string -> string
//    SocialsSlackWebhook: JsonString -> string -> string
//}

type LogQueryConfig = {
    Title: string
    Query: string
    Table: LogsTable option
    QueryTimeRange: TimeSpan
    Formatters: LogQueryFormatters
    Logger: ILogger
    Message: string
}

//type SocialsQueryConfig = {
//    Title: string
//    QueryTimeRange: TimeSpan
//    Formatters: SocialsFormatters
//    Logger: ILogger
//    Message: string
//}