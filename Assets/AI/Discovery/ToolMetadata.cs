using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;


namespace AI.Discovery
{

    /// <summary>
    /// Unity MCP Tool metadata modeli.
    ///
    /// MCP Discovery tarafýndan bulunan
    /// tool bilgisinin merkezi modelidir.
    ///
    /// ToolRegistry içerisinde tutulur.
    /// PromptBuilder runtime sýrasýnda
    /// bu modeli kullanarak AI context üretir.
    /// </summary>
    [Serializable]
    public class ToolMetadata
    {


        // =====================================
        // BASIC INFO
        // =====================================


        public string Name;


        public string Description;


        public string Category;




        // =====================================
        // TOOL FEATURES
        // =====================================


        public List<string> Capabilities =
            new List<string>();



        public List<string> Keywords =
            new List<string>();





        // =====================================
        // PARAMETERS
        // =====================================


        /// <summary>
        /// Genel parametre listesi.
        /// </summary>
        public List<string> Parameters =
            new List<string>();



        /// <summary>
        /// MCP schema içindeki zorunlu parametreler.
        /// </summary>
        public List<string> RequiredParams =
            new List<string>();






        // =====================================
        // MCP SCHEMA DATA
        // =====================================


        /// <summary>
        /// MCP server tarafýndan gönderilen
        /// gerçek JSON schema.
        /// </summary>
        public JObject InputSchema =
            new JObject();





        /// <summary>
        /// Action enum deðerleri.
        ///
        /// Örnek:
        /// create
        /// update
        /// delete
        /// </summary>
        public List<string> Actions =
            new List<string>();







        // =====================================
        // PRIORITY
        // =====================================


        public int Priority;







        // =====================================
        // ENABLE STATE
        // =====================================


        public bool Enabled = true;







        // =====================================
        // HELPERS
        // =====================================



        public bool HasCapability(
            string capability)
        {

            if (string.IsNullOrWhiteSpace(capability))
                return false;



            return Capabilities != null
                &&
                Capabilities.Contains(
                    capability,
                    StringComparer.OrdinalIgnoreCase);

        }







        public bool HasAction(
            string action)
        {

            if (string.IsNullOrWhiteSpace(action))
                return false;



            return Actions != null
                &&
                Actions.Contains(
                    action,
                    StringComparer.OrdinalIgnoreCase);

        }








        public bool HasParameter(
            string parameter)
        {

            if (string.IsNullOrWhiteSpace(parameter))
                return false;



            return Parameters != null
                &&
                Parameters.Contains(
                    parameter,
                    StringComparer.OrdinalIgnoreCase);

        }







        public bool Matches(
            string query)
        {


            if (string.IsNullOrWhiteSpace(query))
                return false;



            query =
                query.ToLowerInvariant();





            if (!string.IsNullOrEmpty(Name)
               &&
               Name.ToLowerInvariant()
               .Contains(query))
            {
                return true;
            }




            if (!string.IsNullOrEmpty(Description)
               &&
               Description.ToLowerInvariant()
               .Contains(query))
            {
                return true;
            }




            if (Category != null
               &&
               Category.ToLowerInvariant()
               .Contains(query))
            {
                return true;
            }




            if (Keywords != null
               &&
               Keywords.Any(
                   x =>
                   !string.IsNullOrEmpty(x)
                   &&
                   x.ToLowerInvariant()
                   .Contains(query)))
            {
                return true;
            }





            if (Capabilities != null
               &&
               Capabilities.Any(
                   x =>
                   !string.IsNullOrEmpty(x)
                   &&
                   x.ToLowerInvariant()
                   .Contains(query)))
            {
                return true;
            }




            return false;

        }









        public override string ToString()
        {

            return
                $"{Name} [{Category}]";

        }


    }

}