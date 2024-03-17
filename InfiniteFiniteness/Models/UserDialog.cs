using InfiniteFiniteness.Util;
using Newtonsoft.Json;

namespace InfiniteFiniteness.Models
{
    public class UserDialog(long chatId, string? prompt = null)
    {
        public enum UserActions
        {
            NONE,
            SET_PROMPT,
            CHOOSE_SCENE,
            SEND_ACTION,
        };

        [JsonProperty("chat")] public long ChatId { get; set; } = chatId;
        [JsonProperty("prompt")] public string? Prompt { get; set; } = prompt;
        [JsonProperty("situation")] public string? Situation { get; set; } = null;
        [JsonProperty("scenes")] public List<string> Scenes { get; set; } = [];
        [JsonProperty("messages")] public List<UserMessage> Messages { get; set; } = [];
        [JsonProperty("action")] public UserActions NextAction { get; set; } = UserActions.SET_PROMPT;
        [JsonProperty("count")] public int UserActionsCount { get; set; } = 0;

        public void Save()
        {
            string fileName = GetFileName(ChatId);
            File.WriteAllText(fileName, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public void Clear()
        {
            Messages.Clear();
            Prompt = null;
            Situation = null;
            Scenes.Clear();
            UserActionsCount = 0;
            NextAction = UserActions.SET_PROMPT;

            Save();
        }

        public static string GetFileName(long chatId) => new FileInfo(Utils.GetDialogsFolder() + "/chat_" + chatId.ToString() + ".json").FullName;

        public static List<UserDialog> GetDialogs()
        {
            string dialogsFolder = Utils.GetDialogsFolder();
            List<UserDialog> dialogs = [];
            foreach(string fileName in Directory.EnumerateFiles(dialogsFolder))
            {
                try
                {
                    dialogs.Add(LoadFromFile(fileName));
                }
                catch (Exception) { }
            }

            return dialogs;
        }

        public static UserDialog LoadFromFile(string fileName)
        {
            UserDialog? dialog = JsonConvert.DeserializeObject<UserDialog>(File.ReadAllText(fileName));
            if (dialog != null) return dialog;

            throw new NullReferenceException();
        }
    }
}
