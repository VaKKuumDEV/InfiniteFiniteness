namespace InfiniteFiniteness.Chat
{
    public class ChatTask(ChatTask.Types type, List<string> taskParams)
    {
        public enum Types
        {
            GENERATE_PROMPT,
            GENERATE_SCENE,
            GENERATE_SCENE_ACTION,
            GENERATE_SCENE_ANSWERS,
        };

        public int Id { get; } = new Random().Next(10000, 99999);
        public Types Type { get; } = type;
        public List<string> Params { get; } = taskParams;
        public bool Completed { get; set; } = false;
        public bool Busy { get; set; } = false;
        public List<string> Result { get; set; } = [];
        public string? SendMessage { get; set; } = null;
        public List<KeyValuePair<string, string>> LastContext { get; set; } = [];
    }
}
