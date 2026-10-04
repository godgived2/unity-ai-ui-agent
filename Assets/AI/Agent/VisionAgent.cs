using System.Threading.Tasks;
using AI.Client;
using UnityEngine;

namespace AI.Agent
{
    /// <summary>
    /// Unity ekran görüntüsü veya görsel verileri
    /// analiz etmek için kullanılan Agent.
    /// VisionClient üzerinden çalışır.
    /// </summary>
    public class VisionAgent
    {


        private readonly VisionClient visionClient;



        // =====================================
        // CONSTRUCTOR
        // =====================================

        public VisionAgent()
        {

            visionClient =
                new VisionClient();

        }





        // =====================================
        // ANALYZE IMAGE
        // =====================================

        public async Task<string> Analyze(
            string imagePath,
            string instruction)
        {

            Debug.Log(
                "========== VISION AGENT =========="
            );



            if (string.IsNullOrEmpty(imagePath))
            {

                return
                    "Image path is empty.";

            }





            if (string.IsNullOrEmpty(instruction))
            {

                instruction =
                    "Analyze this Unity image.";

            }





            string result =
                await visionClient.AnalyzeImage(
                    imagePath,
                    instruction
                );





            Debug.Log(
                "========== VISION RESULT =========="
            );


            Debug.Log(result);





            return result;

        }


    }
}