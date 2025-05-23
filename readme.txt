The Logic App template workflow:
- If the trigger is touched with a query parameter source=ghiza, the workflow will template the slack/teams messages for payload from this func app,
	that is currently two KQL query outputs.
- If the query parameter is different or missing, the workflow will template the slack/teams messages for payload from Azure Alerts Action Group.

Cron expressions:
https://crontab.cronhub.io/
0 */1 * * * * - every minute
0 0 18 * * * - 6pm every day
0 0 10 * * 1 - At 10:00 AM, only on Monday

How to use with Azurite
https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite?tabs=visual-studio%2Cblob-storage#running-azurite-from-an-azure-functions-project

Azurite in Docker
docker pull mcr.microsoft.com/azure-storage/azurite
docker run -d -p 10000:10000 -p 10001:10001 -p 10002:10002 -v C:/Dev/azurite:/data mcr.microsoft.com/azure-storage/azurite



sources:
https://stackoverflow.com/questions/78408121/net-8-azure-function-configurefunctionswebapplication-and-synchronous-operati
alternate host config, explore:
https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide?tabs=windows#register-azure-clients
"FUNCTIONS_WORKER_GRPC_PORT" setting - look it up


to do:
- [DONE] post to teams/slack webhook directly, lose the logic app completely
- alerts don't post to slack - fix this next

cogitate:
- skip the logic app and have the func go to slack/teams directly


Stale Service Principals:
SPs in the Entra ID Directory that do not have an entry in the whole history of the AADServicePrincipalSignInLogs table. Considered stale.



how to test locally:

use azure function core tools 4.0.5907 (later versions are flaky)

Rename local.settings.example.json to local.settings.json and edit in your credentials. Do not commit the resulting local.settings.json to the repo.

option #1, with azure functions core tools:
(you might need to manually delete the obj folder first)
func start
then hit the endpoint the console spits out, say http://localhost:7071/api/HttpExample

option #2, dockerised:
This runs docker-compose using two containers: (1) the app, (2) Azurite. This means we can't use the shorthand connection string for Azurite ("UseDevelopmentStorage=true").
Replace it in local.settings.json with:
	AzureWebJobsStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;"
- make sure the 'azurite' DNS name in the connection string aligns with the the DNS name defined in the docker-compose script.
- run the LocalSettings-to-EnvFile.fsx script to generate variables.env based on local.settings.json
- run "docker-compose up" (optionally with -d) - the docker-compose.yml file references the variables.env

then hit the endpoint over the mapped port, say http://localhost:8080/api/HttpExample

Deployment
Azure extension for VS Code seems to be able to deploy this to Azure just fine.

adaptive cards:
https://adaptivecards.io/explorer/