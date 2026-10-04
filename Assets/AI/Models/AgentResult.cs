using System;
using System.Collections.Generic;

namespace AI.Models
{
    public enum ExecutionStatus
    {
        None,
        Pending,
        Planning,
        Executing,
        Verifying,
        Completed,
        Failed,
        Error
    }


    [Serializable]
    public class AgentResult
    {
        // Baþarý durumu
        public bool Success;


        // Kullanýcýya / Agent'e mesaj
        public string Message;


        // Tool veya Agent çýktýsý
        public object Data;


        // Hata bilgisi
        public string Error;


        // Agent çalýþma durumu
        public ExecutionStatus Status;


        // LLM güven skoru
        public float Confidence;


        // Önerilen sonraki aksiyon
        public string NextAction;


        // Ek bilgiler
        public Dictionary<string, object> Metadata = new();


        // Çalýþma süresi
        public double ExecutionTime;


        // Oluþturulma zamaný
        public DateTime Timestamp = DateTime.Now;



        public static AgentResult Ok(
            string message,
            object data = null)
        {
            return new AgentResult
            {
                Success = true,
                Status = ExecutionStatus.Completed,
                Confidence = 1.0f,
                Message = message,
                Data = data
            };
        }



        public static AgentResult Fail(
            string error)
        {
            return new AgentResult
            {
                Success = false,
                Status = ExecutionStatus.Error,
                Confidence = 0.0f,
                Error = error,
                Message = error
            };
        }



        public override string ToString()
        {
            return Success
                ? $"Success: {Message} | Status: {Status} | Confidence: {Confidence}"
                : $"Failed: {Error} | Status: {Status}";
        }
    }
}