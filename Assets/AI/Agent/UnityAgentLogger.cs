using UnityEngine;


namespace AI.Agent
{


    public static class AgentLogger
    {


        public static void Info(string message)
        {

            Debug.Log(
                "[AGENT] "
                + message
            );

        }




        public static void Warning(string message)
        {

            Debug.LogWarning(
                "[AGENT WARNING] "
                + message
            );

        }




        public static void Error(string message)
        {

            Debug.LogError(
                "[AGENT ERROR] "
                + message
            );

        }


    }


}