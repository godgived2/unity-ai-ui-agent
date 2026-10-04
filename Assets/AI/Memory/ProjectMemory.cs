using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.Memory
{
    /// <summary>
    /// Unity proje hafýzasý.
    ///
    /// Agent'ýn proje bilgilerini
    /// uzun süre saklamasý için kullanýlýr.
    /// </summary>
    public class ProjectMemory
    {


        private readonly Dictionary<string, string> _data =
            new Dictionary<string, string>();



        private readonly List<ProjectMemoryEntry> _history =
            new List<ProjectMemoryEntry>();




        // =====================================
        // SET VALUE
        // =====================================

        public void Set(
            string key,
            string value)
        {

            if (string.IsNullOrWhiteSpace(key))
                return;



            _data[key] =
                value;



            _history.Add(
                new ProjectMemoryEntry
                {

                    Key = key,

                    Value = value,

                    Time = DateTime.Now

                }
            );

        }





        // =====================================
        // GET VALUE
        // =====================================

        public string Get(
            string key)
        {

            if (string.IsNullOrWhiteSpace(key))
                return null;



            _data.TryGetValue(
                key,
                out string value
            );



            return value;

        }





        // =====================================
        // CHECK
        // =====================================

        public bool Contains(
            string key)
        {

            return
                _data.ContainsKey(key);

        }





        // =====================================
        // REMOVE
        // =====================================

        public void Remove(
            string key)
        {

            if (string.IsNullOrWhiteSpace(key))
                return;



            _data.Remove(key);

        }





        // =====================================
        // ALL DATA
        // =====================================

        public Dictionary<string, string> GetAll()
        {

            return
                new Dictionary<string, string>(
                    _data
                );

        }





        // =====================================
        // BUILD CONTEXT
        // =====================================

        public string BuildContext()
        {

            return string.Join(
                "\n",
                _data.Select(
                    item =>
                    $"{item.Key}: {item.Value}"
                )
            );

        }





        // =====================================
        // CLEAR
        // =====================================

        public void Clear()
        {

            _data.Clear();

            _history.Clear();

        }




        public int Count =>
            _data.Count;


    }





    // =====================================
    // MEMORY ENTRY
    // =====================================

    [Serializable]
    public class ProjectMemoryEntry
    {

        public string Key;


        public string Value;


        public DateTime Time;

    }

}