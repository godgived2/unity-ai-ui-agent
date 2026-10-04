using System;
using System.Collections.Generic;

namespace AI.Models
{
    [Serializable]
    public class AgentPlan
    {
        // Plan Identity
        public string Id = Guid.NewGuid().ToString();

        public string Goal;

        public string UserPrompt;

        // Planning
        public List<AgentTask> Tasks = new();

        public int CurrentTaskIndex;

        // Status
        public AgentPlanStatus Status = AgentPlanStatus.Pending;

        // Timing
        public DateTime CreatedAt = DateTime.Now;

        public DateTime StartedAt;

        public DateTime FinishedAt;

        // Statistics
        public int TotalTasks => Tasks.Count;

        public int CompletedTasks =>
            Tasks.FindAll(t => t.Status == AgentTaskStatus.Completed).Count;

        public int FailedTasks =>
            Tasks.FindAll(t => t.Status == AgentTaskStatus.Failed).Count;

        public bool IsCompleted =>
            Status == AgentPlanStatus.Completed;

        public bool HasFailed =>
            Status == AgentPlanStatus.Failed;

        public AgentTask CurrentTask
        {
            get
            {
                if (CurrentTaskIndex < 0 || CurrentTaskIndex >= Tasks.Count)
                    return null;

                return Tasks[CurrentTaskIndex];
            }
        }

        public override string ToString()
        {
            return $"{Goal} ({CompletedTasks}/{TotalTasks})";
        }
    }

    public enum AgentPlanStatus
    {
        Pending,
        Planning,
        Executing,
        Verifying,
        Completed,
        Failed,
        Cancelled
    }
}