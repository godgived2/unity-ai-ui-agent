using System;
using System.Collections.Generic;

namespace AI.Core
{
    /// <summary>
    /// AI çalýþma oturumunu yönetir.
    /// 
    /// Kullanýcý konuþmasý, aktif görev,
    /// seçilen tool'lar ve agent durumu burada tutulur.
    /// 
    /// Agent sistemi bu sýnýf üzerinden
    /// mevcut durumu takip eder.
    /// </summary>
    public class AISession
    {

        // ==============================
        // SESSION ID
        // ==============================

        public string SessionId { get; private set; }



        // ==============================
        // USER DATA
        // ==============================

        public string UserPrompt { get; set; }



        // ==============================
        // AGENT STATE
        // ==============================

        public string CurrentState { get; private set; }
            = AIConstants.STATE_IDLE;



        // ==============================
        // CURRENT TASK
        // ==============================

        public string CurrentTask { get; set; }



        // ==============================
        // HISTORY
        // ==============================

        private readonly List<string> history =
            new List<string>();


        public IReadOnlyList<string> History =>
            history;



        // ==============================
        // TOOL HISTORY
        // ==============================

        private readonly List<string> executedTools =
            new List<string>();


        public IReadOnlyList<string> ExecutedTools =>
            executedTools;



        // ==============================
        // CONSTRUCTOR
        // ==============================

        public AISession()
        {

            SessionId =
                Guid.NewGuid()
                .ToString();


            AIConstants.Log(
                "New AI Session Created : "
                + SessionId
            );

        }



        // ==============================
        // STATE CONTROL
        // ==============================

        public void SetState(string state)
        {
            CurrentState = state;
        }



        public void StartPlanning()
        {
            CurrentState =
                AIConstants.STATE_PLANNING;
        }



        public void StartExecution()
        {
            CurrentState =
                AIConstants.STATE_EXECUTING;
        }



        public void StartVerification()
        {
            CurrentState =
                AIConstants.STATE_VERIFYING;
        }



        public void ResetState()
        {
            CurrentState =
                AIConstants.STATE_IDLE;
        }



        // ==============================
        // HISTORY
        // ==============================

        public void AddMessage(string message)
        {

            if (string.IsNullOrEmpty(message))
                return;


            history.Add(message);



            // maksimum geçmiþ sýnýrý

            if (history.Count >
               AIConstants.MAX_HISTORY_MESSAGES)
            {
                history.RemoveAt(0);
            }

        }



        // ==============================
        // TOOL TRACKING
        // ==============================

        public void AddExecutedTool(
            string toolName)
        {

            if (string.IsNullOrEmpty(toolName))
                return;


            executedTools.Add(
                toolName
            );

        }



        // ==============================
        // CLEAN SESSION
        // ==============================

        public void Clear()
        {

            UserPrompt = null;

            CurrentTask = null;


            history.Clear();

            executedTools.Clear();


            ResetState();

        }



        // ==============================
        // DEBUG
        // ==============================

        public string GetSummary()
        {

            return
                $"Session: {SessionId}\n" +
                $"State: {CurrentState}\n" +
                $"Messages: {history.Count}\n" +
                $"Tools Used: {executedTools.Count}";

        }

    }
}