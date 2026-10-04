using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Utils
{
    /// <summary>
    /// JSON iþlemleri için yardýmcý sýnýf.
    /// </summary>
    public static class JsonUtility
    {


        // =====================================
        // SERIALIZE
        // =====================================

        public static string Serialize<T>(
            T data,
            bool indented = true)
        {

            try
            {

                return JsonConvert.SerializeObject(
                    data,
                    indented
                    ?
                    Formatting.Indented
                    :
                    Formatting.None
                );

            }
            catch (Exception ex)
            {

                Debug.LogError(
                    "JSON Serialize Error: "
                    +
                    ex.Message
                );


                return null;

            }

        }





        // =====================================
        // DESERIALIZE
        // =====================================

        public static T Deserialize<T>(
            string json)
        {

            if (string.IsNullOrWhiteSpace(json))
                return default;



            try
            {

                return JsonConvert
                    .DeserializeObject<T>(
                        json
                    );

            }
            catch (Exception ex)
            {

                Debug.LogError(
                    "JSON Deserialize Error: "
                    +
                    ex.Message
                );


                return default;

            }

        }





        // =====================================
        // PARSE OBJECT
        // =====================================

        public static JObject ParseObject(
            string json)
        {

            if (string.IsNullOrWhiteSpace(json))
                return null;



            try
            {

                return JObject.Parse(
                    json
                );

            }
            catch
            {

                return null;

            }

        }





        // =====================================
        // VALIDATE
        // =====================================

        public static bool IsValid(
            string json)
        {

            if (string.IsNullOrWhiteSpace(json))
                return false;



            try
            {

                JToken.Parse(json);

                return true;

            }
            catch
            {

                return false;

            }

        }





        // =====================================
        // GET VALUE
        // =====================================

        public static string GetString(
            JObject obj,
            string key)
        {

            if (obj == null)
                return null;



            return obj[key]?
                .ToString();

        }

    }

}