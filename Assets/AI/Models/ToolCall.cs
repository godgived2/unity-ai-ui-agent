using System;
using System.Collections.Generic;

namespace AI.Models
{

    [Serializable]
    public class ToolCall
    {

        public string ToolName;

        public Dictionary<string, object> Arguments =
            new Dictionary<string, object>();

        public Guid Id =
            Guid.NewGuid();

        public DateTime CreatedAt =
            DateTime.Now;

        public ToolCallStatus Status =
            ToolCallStatus.Waiting;

        public string Result;

        public string Error;

    }



    public enum ToolCallStatus
    {
        Waiting,
        Running,
        Success,
        Failed
    }

}