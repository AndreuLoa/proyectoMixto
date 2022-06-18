using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;

namespace proyectoMixto
{
    public static class Main
    {
        public class Face
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("faceid")]
            public string FaceId { get; set; }
            [JsonProperty("rectangle")]
            public string Rectangle { get; set; }
            [JsonProperty("attributes")]
            public string Attributes { get; set; }
            [JsonProperty("landmarks")]
            public string Landmarks { get; set; }

            public Face(DetectedFace detected)
            {
                this.Id = null;
                this.FaceId = detected?.FaceId.Value.ToString();
            }
        }
        public static Stream transFrom(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);
            MemoryStream memoryStream = new MemoryStream(bytes);
            return memoryStream;
        }

        public static string estado = "false";


        [FunctionName("postDB")]
        public static async Task<IActionResult> RunPostDB(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "postdb/")] HttpRequest req,
            [CosmosDB(
                databaseName: "faceDB",
                collectionName: "faceContainer",
                ConnectionStringSetting = "dbCosmos"
            )] IAsyncCollector<Face> datos, ILogger log)
        {
            //Post Image
            IActionResult exit = new BadRequestObjectResult("no info");
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string base64 = data.img;
            if (base64 != null && data.name != null)
            {
                //Autentificacion Face Client
                log.LogInformation("Processing Key");
                var key = Environment.GetEnvironmentVariable("Key");
                var endpoint = Environment.GetEnvironmentVariable("Endpoint");
                IFaceClient client = new FaceClient(new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint };

                IList<DetectedFace> detectedFaces;
                log.LogInformation("Processing ilist");
                detectedFaces = await client.Face.DetectWithStreamAsync(transFrom(base64),
                    returnFaceAttributes: new List<FaceAttributeType> {
                        FaceAttributeType.Accessories,
                        FaceAttributeType.Age,
                        FaceAttributeType.Blur,
                        FaceAttributeType.Emotion,
                        FaceAttributeType.Exposure,
                        FaceAttributeType.FacialHair,
                        FaceAttributeType.Glasses,
                        FaceAttributeType.Hair,
                        FaceAttributeType.HeadPose,
                        FaceAttributeType.Makeup,
                        FaceAttributeType.Noise,
                        FaceAttributeType.Occlusion,
                        FaceAttributeType.Smile,
                        FaceAttributeType.QualityForRecognition},
                    detectionModel: DetectionModel.Detection01,
                    recognitionModel: RecognitionModel.Recognition04,
                    returnRecognitionModel: true);
                Face face1 = new(detectedFaces[0]);
                face1.Name = data.name;
                await datos.AddAsync(face1);
                exit = new OkObjectResult(face1);
            }
            return exit;
        }

        [FunctionName("postCompare")]
        public static async Task<IActionResult> RunGetDB(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "compare/")] HttpRequest req,
            [CosmosDB(
                databaseName: "faceDB",
                collectionName: "faceContainer",
                ConnectionStringSetting = "dbCosmos"
            )] IEnumerable<Face> datos, ILogger log)
        {
            //autenticate
            log.LogInformation("autenticating...");
            var key = Environment.GetEnvironmentVariable("Key");
            var endpoint = Environment.GetEnvironmentVariable("Endpoint");
            IFaceClient client = new FaceClient(new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint };

            //Reading Image
            log.LogInformation("reading info...");
            IActionResult exit = new NotFoundObjectResult(null);
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string base64 = data?.img;

            log.LogInformation("analizing face...");
            IList<DetectedFace> detectedFaces;
            detectedFaces = await client.Face.DetectWithStreamAsync(transFrom(base64),
                    returnFaceAttributes: new List<FaceAttributeType> {
                        FaceAttributeType.Accessories,
                        FaceAttributeType.Age,
                        FaceAttributeType.Blur,
                        FaceAttributeType.Emotion,
                        FaceAttributeType.Exposure,
                        FaceAttributeType.FacialHair,
                        FaceAttributeType.Glasses,
                        FaceAttributeType.Hair,
                        FaceAttributeType.HeadPose,
                        FaceAttributeType.Makeup,
                        FaceAttributeType.Noise,
                        FaceAttributeType.Occlusion,
                        FaceAttributeType.Smile,
                        FaceAttributeType.QualityForRecognition},
                    recognitionModel: RecognitionModel.Recognition04,
                    returnRecognitionModel: true);

            log.LogInformation("loading faces...");
            IList<Guid?> targetFaceIds = new List<Guid?>();
            foreach (Face face in datos)
            {
                targetFaceIds.Add(Guid.Parse(face.FaceId));
            }

            log.LogInformation("comparing faces...");
            log.LogInformation(detectedFaces[0].FaceId.Value.ToString());
            exit = new OkObjectResult(detectedFaces[0]);

            try
            {
                IList<SimilarFace> similarResults = await client.Face.FindSimilarAsync(detectedFaces[0].FaceId.Value, null, null, targetFaceIds);
                var facename = "no face";
                log.LogInformation("looping...");
                foreach (SimilarFace sim in similarResults)
                {
                    foreach (Face face in datos)
                    {
                        if (face.FaceId == sim.FaceId.ToString())
                        {
                            facename = face.Name;
                            estado = "true";
                        }
                    }
                }
                log.LogInformation("finishing...");


                //Reading Post Image
                exit = new OkObjectResult(facename);
            }
            catch (Microsoft.Azure.CognitiveServices.Vision.Face.Models.APIErrorException appX)
            {
                log.LogInformation(appX.Message);
                log.LogInformation(appX.Body.Error.Code);
                log.LogInformation(appX.Body.Error.Message);
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.ToString());
            }
            return exit;

        }

        [FunctionName("Door")]
        public static async Task<HttpResponseMessage> RunDoor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "door/")]
        HttpRequest req, ILogger log)
        {
            var resp = new HttpResponseMessage();
            byte[] data = Encoding.ASCII.GetBytes(estado);
            resp.Content = new ByteArrayContent(data);
            resp.Content.Headers.ContentLength = data.Length;
            string exit = estado;
            if (estado.Equals("true"))
                estado = "false";
            return resp;
        }

        [FunctionName("Test")]
        public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "test/")]
        HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = String.Empty;
            using (StreamReader streamReader = new StreamReader(req.Body))
            {
                requestBody = await streamReader.ReadToEndAsync();
            }
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            return name != null
                ? (ActionResult)new OkObjectResult($"Hello, {name}")
                : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
        }
    }
}
