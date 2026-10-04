using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.Memory
{
    /// <summary>
    /// Kullanýcý ve AI arasýndaki
    /// konuþma geçmiþini yönetir.
    /// </summary>
    public class ConversationMemory
    {


        private readonly List<ConversationMessage> _messages =
            new List<ConversationMessage>();




        // =====================================
        // ADD MESSAGE
        // =====================================

        public void AddUserMessage(
            string content)
        {

            Add(
                "user",
                content
            );

        }




        public void AddAssistantMessage(
            string content)
        {

            Add(
                "assistant",
                content
            );

        }




        public void AddSystemMessage(
            string content)
        {

            Add(
                "system",
                content
            );

        }





        private void Add(
            string role,
            string content)
        {

            if (string.IsNullOrWhiteSpace(content))
                return;



            _messages.Add(
                new ConversationMessage
                {
                    Role = role,

                    Content = content,

                    Time = DateTime.Now

                }
            );

        }





        // =====================================
        // GET HISTORY
        // =====================================

        public List<ConversationMessage> GetAll()
        {

            return
                new List<ConversationMessage>(
                    _messages
                );

        }





        public List<ConversationMessage> GetLast(
            int count)
        {

            if (count <= 0)
                return new List<ConversationMessage>();



            return _messages
                .TakeLast(count)
                .ToList();

        }





        // =====================================
        // BUILD TEXT
        // =====================================

        public string BuildContext(
            int maxMessages = 10)
        {

            var messages =
                GetLast(maxMessages);



            return string.Join(
                "\n",
                messages.Select(
                    msg =>
                    $"{msg.Role}: {msg.Content}"
                )
            );

        }





        // =====================================
        // CLEAR
        // =====================================

        public void Clear()
        {

            _messages.Clear();

        }




        public int Count =>
            _messages.Count;


    }





    // =====================================
    // MESSAGE MODEL
    // =====================================

    [Serializable]
    public class ConversationMessage
    {

        public string Role;


        public string Content;


        public DateTime Time;

    }

}