using System;
using System.Collections.Generic;

namespace AI.Core
{
    /// <summary>
    /// AI sistemindeki servisleri merkezi olarak yönetir.
    /// 
    /// Agent, Context, Discovery, Executor gibi
    /// sistemlerin birbirine direkt baðýmlý olmasýný engeller.
    /// 
    /// Örnek:
    /// Agent -> AIServiceLocator -> UnityContextService
    /// </summary>
    public static class AIServiceLocator
    {

        private static readonly Dictionary<Type, object> services =
            new Dictionary<Type, object>();



        // ==============================
        // REGISTER SERVICE
        // ==============================

        public static void Register<T>(T service)
        {

            if (service == null)
                throw new ArgumentNullException(
                    nameof(service)
                );


            Type type = typeof(T);


            if (services.ContainsKey(type))
            {
                services[type] = service;
                return;
            }


            services.Add(
                type,
                service
            );


            AIConstants.Log(
                $"Service Registered : {type.Name}"
            );

        }



        // ==============================
        // GET SERVICE
        // ==============================

        public static T Get<T>()
        {

            Type type =
                typeof(T);



            if (services.TryGetValue(
                type,
                out object service))
            {
                return (T)service;
            }



            AIConstants.LogWarning(
                $"Service Not Found : {type.Name}"
            );


            return default;

        }



        // ==============================
        // CHECK SERVICE
        // ==============================

        public static bool Has<T>()
        {

            return services.ContainsKey(
                typeof(T)
            );

        }



        // ==============================
        // REMOVE SERVICE
        // ==============================

        public static void Remove<T>()
        {

            Type type =
                typeof(T);


            if (services.ContainsKey(type))
            {
                services.Remove(type);


                AIConstants.Log(
                    $"Service Removed : {type.Name}"
                );

            }

        }



        // ==============================
        // CLEAR ALL
        // ==============================

        public static void Clear()
        {

            services.Clear();


            AIConstants.Log(
                "All AI Services Cleared"
            );

        }



        // ==============================
        // DEBUG
        // ==============================

        public static List<string> GetRegisteredServices()
        {

            List<string> result =
                new List<string>();


            foreach (var service in services)
            {
                result.Add(
                    service.Key.Name
                );
            }


            return result;

        }

    }
}