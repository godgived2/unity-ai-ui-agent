using System;
using System.IO;
using UnityEngine;

namespace AI.Utils
{
    /// <summary>
    /// Dosya iþlemleri için yardýmcý servis.
    /// </summary>
    public static class FileUtility
    {


        // =====================================
        // EXISTS
        // =====================================

        public static bool Exists(
            string path)
        {

            if (string.IsNullOrWhiteSpace(path))
                return false;


            return File.Exists(path);

        }





        // =====================================
        // DIRECTORY
        // =====================================

        public static bool DirectoryExists(
            string path)
        {

            if (string.IsNullOrWhiteSpace(path))
                return false;


            return Directory.Exists(path);

        }




        public static void CreateDirectory(
            string path)
        {

            if (string.IsNullOrWhiteSpace(path))
                return;



            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

        }





        // =====================================
        // READ
        // =====================================

        public static string Read(
            string path)
        {

            if (!Exists(path))
                return null;



            try
            {

                return File.ReadAllText(path);

            }
            catch (Exception ex)
            {

                Debug.LogError(
                    "File read error: "
                    +
                    ex.Message
                );


                return null;

            }

        }





        // =====================================
        // WRITE
        // =====================================

        public static bool Write(
            string path,
            string content)
        {

            try
            {

                string directory =
                    Path.GetDirectoryName(path);



                if (!string.IsNullOrEmpty(directory))
                {
                    CreateDirectory(directory);
                }



                File.WriteAllText(
                    path,
                    content
                );


                return true;

            }
            catch (Exception ex)
            {

                Debug.LogError(
                    "File write error: "
                    +
                    ex.Message
                );


                return false;

            }

        }





        // =====================================
        // DELETE
        // =====================================

        public static bool Delete(
            string path)
        {

            if (!Exists(path))
                return false;



            try
            {

                File.Delete(path);

                return true;

            }
            catch
            {

                return false;

            }

        }





        // =====================================
        // PATH
        // =====================================

        public static string Combine(
            params string[] parts)
        {

            return
                Path.Combine(parts);

        }

    }

}