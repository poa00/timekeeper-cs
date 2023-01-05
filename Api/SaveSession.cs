using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;
using Timekeeper.DataModel;
using Timekeeper.Model;

namespace Timekeeper
{
    public static class SaveSession
    {
        [FunctionName("SaveSession")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "post",
                Route = "session/{branchId}/{sessionId}")]
            HttpRequest req,
            string branchId,
            string sessionId,
            [Blob("sessions/{branchId}/{sessionId}.json", FileAccess.Write, Connection = "AzureStorage")]
            Stream sessionBlob,
            ILogger log)
        {
            log.LogInformation("-> SaveSession");

            var verificationResult = Verification.Verify(branchId, sessionId);

            if (verificationResult != null)
            {
                log.LogError(verificationResult);
                return new BadRequestObjectResult(verificationResult);
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            log.LogDebug(requestBody);

            if (string.IsNullOrEmpty(requestBody))
            {
                log.LogError("No body found");
                return new BadRequestObjectResult("No session found in body");
            }

            var session = JsonConvert.DeserializeObject<SessionBase>(requestBody);

            if (session.BranchId == null
                || session.BranchId.ToLower() != branchId.ToLower())
            {
                log.LogError("Branch IDs don't match");
                return new BadRequestObjectResult("Branch IDs don't match");
            }

            if (session.SessionId == null
                || session.SessionId.ToLower() != sessionId.ToLower())
            {
                log.LogError("Session IDs don't match");
                return new BadRequestObjectResult("Session IDs don't match");
            }

            // Reserialize to ensure that the formatting is correct (easier to read when debugging)
            var storageJson = JsonConvert.SerializeObject(session, Formatting.Indented);

            using (var writer = new StreamWriter(sessionBlob))
            {
                writer.Write(storageJson);
            }

            return new OkObjectResult("Saved to storage");
        }
    }
}