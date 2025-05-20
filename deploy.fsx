#r "nuget: Farmer"

open Farmer
open Farmer.Arm
open Farmer.ContainerApp
open Farmer.Builders
open System
open System.IO
open System.Text.Json

[<AutoOpen>]
module Cfg =
    // probably not needed
    // "APPINSIGHTS_INSTRUMENTATIONKEY" ai.InstrumentationKey.Value
    // "APPLICATIONINSIGHTS_CONNECTION_STRING" "InstrumentationKey=838c35a7-8b49-42f4-bd77-e3296dc8aa38;IngestionEndpoint=https://uksouth-1.in.applicationinsights.azure.com/;LiveEndpoint=https://uksouth.livediagnostics.monitor.azure.com/;ApplicationId=cf1bf99d-e4ce-45bb-80e4-d81789d8c213"
    
    let FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"
    let FUNCTIONS_EXTENSION_VERSION = "~4"
    //let WEBSITES_PORT = "80"
    // env-agnostic
    let ACR_NAME = Environment.GetEnvironmentVariable "ACR_NAME"
    let ACR_LOGIN_SERVER = Environment.GetEnvironmentVariable "ACR_LOGIN_SERVER"
    let X_BEARER_TOKEN = Environment.GetEnvironmentVariable "X_BEARER_TOKEN"
    let WORKSPACE_ID = Environment.GetEnvironmentVariable "WORKSPACE_ID"
    let TENANT_ID = Environment.GetEnvironmentVariable "GHIZA_TENANT_ID"
    let CLIENT_ID = Environment.GetEnvironmentVariable "GHIZA_CLIENT_ID"
    let CLIENT_SECRET = Environment.GetEnvironmentVariable "GHIZA_CLIENT_SECRET"
    let BLUESKY_AT_IDENTIFIER = Environment.GetEnvironmentVariable "BLUESKY_AT_IDENTIFIER"
    let X_HANDLE = Environment.GetEnvironmentVariable "X_HANDLE"
    // env-specific
    let ENVIRONMENT = Environment.GetEnvironmentVariable "ENVIRONMENT"
    let CRON_CREATED_SERVICE_PRINCIPALS = Environment.GetEnvironmentVariable "CRON_CREATED_SERVICE_PRINCIPALS"
    let CRON_REPLIES_REPORT_BLUESKY = Environment.GetEnvironmentVariable "CRON_REPLIES_REPORT_BLUESKY"
    let CRON_REPLIES_REPORT_X = Environment.GetEnvironmentVariable "CRON_REPLIES_REPORT_X"
    let CRON_SIGNINS = Environment.GetEnvironmentVariable "CRON_SIGNINS"
    let CRON_STALE_SERVICE_PRINCIPALS = Environment.GetEnvironmentVariable "CRON_STALE_SERVICE_PRINCIPALS"
    let LOOKBACK_MINUTES_SIGNINS = Environment.GetEnvironmentVariable "LOOKBACK_MINUTES_SIGNINS"
    let LOOKBACK_MINUTES_SPCREATIONS = Environment.GetEnvironmentVariable "LOOKBACK_MINUTES_SPCREATIONS"
    let SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK = Environment.GetEnvironmentVariable "SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK"
    let SLACK_SOCIALS_CHANNEL_WEBHOOK = Environment.GetEnvironmentVariable "SLACK_SOCIALS_CHANNEL_WEBHOOK"
    let TEAMS_MONITORING_CHANNEL_WEBHOOK = Environment.GetEnvironmentVariable "TEAMS_MONITORING_CHANNEL_WEBHOOK"
    let TEAMS_SOCIALS_CHANNEL_WEBHOOK = Environment.GetEnvironmentVariable "TEAMS_SOCIALS_CHANNEL_WEBHOOK"

let solutionName = "ghiza"
let env = ENVIRONMENT.ToLower()
// let env =
//     let branch =
//         Environment.GetEnvironmentVariable "GITHUB_REF_NAME"
//         // (Environment.GetEnvironmentVariable "GITHUB_REF").Split "/" |> Array.last
//     match branch with
//     | "master"
//     | "main" -> "live"
//     | _ -> "test"

let sa: StorageAccountConfig = storageAccount {
    name $"{solutionName}{env}"
}

let law = logAnalytics {
    name $"{solutionName}-{env}-law"
}

let ai = appInsights {
    name $"{solutionName}-{env}-ai"
    log_analytics_workspace law
}

let deploymentStorage = arm {
    location Location.UKSouth
    add_resources [
        sa
        law
        ai
    ]
    outputs [
        "storageConnectionString", sa.Key
        "aiConnectionString", ai.ConnectionString
    ]
}

let storageConnectionString, aiConnectionString =
    deploymentStorage
    |> Deploy.execute $"{solutionName}-{env}" Deploy.NoParameters
    |> fun m -> m["storageConnectionString"], m["aiConnectionString"]
    
let container = container {
    name $"{solutionName}-{env}-container"
    private_docker_image ACR_LOGIN_SERVER "ghiza/ghiza" "latest"
    cpu_cores 0.5<VCores>
    memory 1.0<Gb>
}

let cApp = containerApp {
    name $"{solutionName}-{env}-app"
    active_revision_mode ActiveRevisionsMode.Single
    system_identity
    reference_registry_credentials [
        ResourceId.create (Arm.ContainerRegistry.registries, ResourceName.ResourceName ACR_NAME, "cit-shared")
    ]
    add_containers [ container ]
    replicas 1 1
    
    add_env_variables [
        // 'platform' settings
        //"FUNCTIONS_WORKER_RUNTIME", FUNCTIONS_WORKER_RUNTIME
        //"FUNCTIONS_EXTENSION_VERSION", FUNCTIONS_EXTENSION_VERSION
        "AzureWebJobsStorage", storageConnectionString
        "APPLICATIONINSIGHTS_CONNECTION_STRING", aiConnectionString
        // test/live - for deployment
        "ENVIRONMENT", ENVIRONMENT
        // app's settings
        "WORKSPACE_ID", WORKSPACE_ID
        "X_BEARER_TOKEN", X_BEARER_TOKEN
        "TENANT_ID", TENANT_ID
        "CLIENT_ID", CLIENT_ID
        "CLIENT_SECRET", CLIENT_SECRET
        "BLUESKY_AT_IDENTIFIER", BLUESKY_AT_IDENTIFIER
        "X_HANDLE", X_HANDLE
        "CRON_CREATED_SERVICE_PRINCIPALS", CRON_CREATED_SERVICE_PRINCIPALS
        "CRON_REPLIES_REPORT_BLUESKY", CRON_REPLIES_REPORT_BLUESKY
        "CRON_REPLIES_REPORT_X", CRON_REPLIES_REPORT_X
        "CRON_SIGNINS", CRON_SIGNINS
        "CRON_STALE_SERVICE_PRINCIPALS", CRON_STALE_SERVICE_PRINCIPALS
        "LOOKBACK_MINUTES_SIGNINS", LOOKBACK_MINUTES_SIGNINS
        "LOOKBACK_MINUTES_SPCREATIONS", LOOKBACK_MINUTES_SPCREATIONS
        "SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK", SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK
        "SLACK_SOCIALS_CHANNEL_WEBHOOK", SLACK_SOCIALS_CHANNEL_WEBHOOK
        "TEAMS_MONITORING_CHANNEL_WEBHOOK", TEAMS_MONITORING_CHANNEL_WEBHOOK
        "TEAMS_SOCIALS_CHANNEL_WEBHOOK", TEAMS_SOCIALS_CHANNEL_WEBHOOK
    ]
}

let cae = containerEnvironment {
    name $"{solutionName}-{env}-cae"
    app_insights_instance ai
    add_container cApp
}
let deployment = arm {
    location Location.UKSouth
    add_resource cae
}

deployment |> Deploy.execute $"{solutionName}-{env}" Deploy.NoParameters