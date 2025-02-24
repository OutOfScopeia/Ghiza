module Cfg

open System
open Azure.Identity
open Azure.Monitor.Query
open Microsoft.Graph

//let isInLocal = String.IsNullOrEmpty(Environment.GetEnvironmentVariable "WEBSITE_INSTANCE_ID") // if not running in the cloud
let lookbackMinutesSignIns = Environment.GetEnvironmentVariable "LOOKBACK_MINUTES_SIGNINS"
let lookbackMinutesSPCreations = Environment.GetEnvironmentVariable "LOOKBACK_MINUTES_SPCREATIONS"
let tenantId = Environment.GetEnvironmentVariable "TENANT_ID"
let clientId = Environment.GetEnvironmentVariable "CLIENT_ID"
let clientSecret = Environment.GetEnvironmentVariable "CLIENT_SECRET"
let workspaceId = Environment.GetEnvironmentVariable "WORKSPACE_ID"
let teamsAzureAlertsWebhook = Environment.GetEnvironmentVariable "TEAMS_MONITORING_CHANNEL_WEBHOOK"
let slackAzureAlertsWebhook = Environment.GetEnvironmentVariable "SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK"
let teamsSocialAlertsWebhook = Environment.GetEnvironmentVariable "TEAMS_SOCIALS_CHANNEL_WEBHOOK"
let slackSocialAlertsWebhook = Environment.GetEnvironmentVariable "SLACK_SOCIALS_CHANNEL_WEBHOOK"
let blueskyDid = Environment.GetEnvironmentVariable "BLUESKY_AT_IDENTIFIER"
let blueskyPublicApiBaseUrl = "https://public.api.bsky.app"

let credential = ClientSecretCredential(tenantId, clientId, clientSecret)
let logsQueryClient = LogsQueryClient(credential)
let graphClient = new GraphServiceClient(credential)
