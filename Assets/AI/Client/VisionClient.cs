using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AI.Client
{
    /// <summary>
    /// Vision AI baðlantý servisi.
    ///
    /// Unity ekran görüntüsü,
    /// asset görüntüsü veya kullanýcý görselleri
    /// için ileride multimodal modellerle kullanýlacak.
    /// </summary>
    public class VisionClient
    {


        private readonly string _model;

        private readonly string _url;



        // =====================================
        // CONSTRUCTOR
        // =====================================

        public VisionClient(
            string model = "llava",
            string url = "http://localhost:11434/api/generate")
        {

            _model = model;

            _url = url;

        }




        // =====================================
        // ANALYZE IMAGE PATH
        // =====================================

        public async Task<string> AnalyzeImage(
            string imagePath,
            string prompt = "Analyze this image")
        {

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return "Image path empty.";
            }



            if (!File.Exists(imagePath))
            {
                return
                    "Image not found: "
                    +
                    imagePath;
            }



            try
            {

                byte[] bytes =
                    File.ReadAllBytes(
                        imagePath
                    );



                string base64 =
                    Convert.ToBase64String(
                        bytes
                    );



                return await SendVisionRequest(
                    base64,
                    prompt
                );

            }
            catch (Exception ex)
            {

                Debug.LogError(
                    "Vision Error:"
                );


                Debug.LogError(
                    ex
                );


                return
                    "Vision Exception: "
                    +
                    ex.Message;

            }

        }





        // =====================================
        // SEND REQUEST
        // =====================================

        private async Task<string> SendVisionRequest(
            string imageBase64,
            string prompt)
        {

            /*
             * Buraya ileride:
             *
             * - Ollama llava
             * - GPT Vision
             * - Gemini Vision
             * - Local multimodal model
             *
             * baðlantýsý eklenecek.
             */


            await Task.CompletedTask;



            return
                "Vision analysis is not connected yet.";

        }



        // =====================================
        // CHECK IMAGE
        // =====================================

        public bool IsValidImage(
            string path)
        {

            if (string.IsNullOrEmpty(path))
                return false;


            return File.Exists(path);

        }


    }

}