using System;
using System.Collections.Generic;

namespace AI.Models
{
    [Serializable]
    public class AgentTask
    {
        public string Id = Guid.NewGuid().ToString();

        public string Name;

        public string Description;

        // Hangi tool kullanýlacak
        public string Tool;

        // Tool parametreleri
        public Dictionary<string, object> Parameters = new();

        // Çalýþma durumu
        public AgentTaskStatus Status = AgentTaskStatus.Pending;

        // Sonuç
        public AgentResult Result;

        // Süre
        public DateTime StartedAt;

        public DateTime FinishedAt;

        // Tekrar deneme sayýsý
        public int RetryCount;

        // Bu task zorunlu mu?
        public bool Required = true;

        public override string ToString()
        {
            return $"{Name} ({Status})";
        }
    }

    public enum AgentTaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Skipped
    }
}