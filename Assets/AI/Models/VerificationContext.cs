namespace AI.Models
{
    public class VerificationContext
    {
        public string OriginalGoal { get; set; }
        public string CurrentTask { get; set; }
        public string ExecutedTool { get; set; }
        public string Arguments { get; set; }
        public string ToolOutput { get; set; }
    }
}