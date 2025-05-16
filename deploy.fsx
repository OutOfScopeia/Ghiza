#r "nuget: Farmer"

open Farmer
open Farmer.Arm
open Farmer.Builders
open System
open System.IO
open System.Text.Json

let product = "ghiza"
let env =
    let branch =
        (Environment.GetEnvironmentVariable "GITHUB_REF").Split "/"
        |> Array.last
        
    match branch with
    | "master"
    | "main" -> "live"
    | _ -> "test"

let acrName = Environment.GetEnvironmentVariable "AZURE_CONTAINER_REGISTRY_NAME"

//let containerEnvVars : seq<string*string> = Seq.empty

//let storageAccount = ResourceId.create(ResourceType ("StorageAccounts", "2024-01-01"), ResourceName "citmaintenance")

let law = logAnalytics {
    name $"{product}-{env}-law"
}

let ai = appInsights {
    name $"{product}-{env}-ai"
    log_analytics_workspace law
}
    
let container = container {
    name $"{product}-{env}-container"
    private_docker_image "citregistry.azurecr.io" "ghiza" "latest"
    cpu_cores 0.5<VCores>
    memory 1.0<Gb>
}

let cApp = containerApp {
    name $"{product}-{env}-app"
    system_identity

    reference_registry_credentials [
            ResourceId.create (Arm.ContainerRegistry.registries, ResourceName.ResourceName acrName, "cit-shared")
        ]

    add_containers [ container ]
    replicas 1 1 
    add_env_variables [
        // probably not needed
        // add_env_variable "APPINSIGHTS_INSTRUMENTATIONKEY" ai.InstrumentationKey.Value
        // add_env_variable "APPLICATIONINSIGHTS_CONNECTION_STRING" "InstrumentationKey=838c35a7-8b49-42f4-bd77-e3296dc8aa38;IngestionEndpoint=https://uksouth-1.in.applicationinsights.azure.com/;LiveEndpoint=https://uksouth.livediagnostics.monitor.azure.com/;ApplicationId=cf1bf99d-e4ce-45bb-80e4-d81789d8c213"
        // add_env_variable "AzureWebJobsStorage" (Environment.GetEnvironmentVariable "AZURE_STORAGE_CONNECTION_STRING")
        // add_env_variable "FUNCTIONS_WORKER_RUNTIME" "dotnet-isolated"
        // add_env_variable "SCM_DO_BUILD_DURING_DEPLOYMENT" "0"
        // add_env_variable "FUNCTIONS_EXTENSION_VERSION" "~4"
        // add_env_variable "WEBSITE_RUN_FROM_PACKAGE"     "https://citmaintenance.blob.core.windows.net/function-releases/20250417033429-cae111ec29a7f855d6bb8708fc0ecac5.zip?sv=2025-01-05&st=2025-04-17T03%3A29%3A34Z&se=2035-04-17T03%3A34%3A34Z&sr=b&sp=r&sig=UxuZeU6zwkKGkQ1SAMGd%2B8dwabFrYCG2lKrzTHYCtko%3D"
        // add_env_variable "WORKSPACE_ID"     "ebef7024-4ce7-431a-ae6e-045680b8f8e4"
        
        // env-agnostic
        "X_BEARER_TOKEN", Environment.GetEnvironmentVariable "X_BEARER_TOKEN"
        "TENANT_ID", Environment.GetEnvironmentVariable "GHIZA_TENANT_ID"
        "CLIENT_ID", Environment.GetEnvironmentVariable "GHIZA_CLIENT_ID"
        "CLIENT_SECRET", Environment.GetEnvironmentVariable "GHIZA_CLIENT_SECRET"
        "BLUESKY_AT_IDENTIFIER", "did:plc:gtprayz574c2sc4ek27mnlfy"
        "X_HANDLE", "compositionalit"
        // env-specific
        "CRON_CREATED_SERVICE_PRINCIPALS", "0 0 18 * * *"
        "CRON_REPLIES_REPORT_BLUESKY", "0 * * * *"
        "CRON_REPLIES_REPORT_X", "0 * * * *"
        "CRON_SIGNINS", "0 0 18 * * *"
        "CRON_STALE_SERVICE_PRINCIPALS", "0 0 10 * * 1"
        "LOOKBACK_MINUTES_SIGNINS", "1450"
        "LOOKBACK_MINUTES_SPCREATIONS", "1450"
        "SLACK_AZURE_ALERTS_CHANNEL_WEBHOOK", "https://hooks.slack.com/services/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"
        "SLACK_SOCIALS_CHANNEL_WEBHOOK", "https://hooks.slack.com/services/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"
        "TEAMS_MONITORING_CHANNEL_WEBHOOK", "https://compositionalit.webhook.office.com/webhookb2/8a59ebec-7413-432c-9023-f4eb8848a166@bb7f7453-15af-4ab0-9d45-cdb4a56293bc/IncomingWebhook/c02b2dfced054c7e9f129ad8d286faf9/85a19aa4-10bd-4e8b-bf08-357d993a93b5/V2GkOavnPecI0QkcgUOdsbMBkblpl4OwgUKBEVgQ51qPE1"
        "TEAMS_SOCIALS_CHANNEL_WEBHOOK", "https://prod-129.westeurope.logic.azure.com:443/workflows/9529e1f3fa5841958ce7a06e0d6c31d7/triggers/manual/paths/invoke?api-version=2016-06-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=Z4xpQ8HFitd9A_uMDE8Zyv0zBqXXSEqD5DFPBvSg2fs"
    ]
}

let cae = containerEnvironment {
    name $"{product}-{env}-cae"
    app_insights_instance ai
    add_container cApp
}

let deployment = arm {
    location Location.UKSouth
    add_resource cae
    add_resource law
    add_resource ai
    //add_resource cApp
}

deployment |> Deploy.execute $"{product}-{env}" Deploy.NoParameters