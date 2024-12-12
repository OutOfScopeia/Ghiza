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
docker build --tag randomid/azurefunctionsimage:v1.0.0 .
docker run -p 8080:80 -it randomid/azurefunctionsimage:v1.0.0
then hit the endpoint over the mapped port, say http://localhost:8080/api/HttpExample

Deployment
Azure extension for VS Code seems to be able to deploy this to Azure just fine.

adaptive cards:
https://adaptivecards.io/explorer/