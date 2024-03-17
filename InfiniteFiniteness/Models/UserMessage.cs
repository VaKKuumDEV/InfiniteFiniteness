using Newtonsoft.Json;

namespace InfiniteFiniteness.Models
{
    public class UserMessage(int id, string text, UserMessage.SenderTypes senderType)
    {
        public enum SenderTypes
        {
            USER,
            BOT,
        };

        [JsonProperty("id")] public int Id { get; set; } = id;
        [JsonProperty("text")] public string Text { get; set; } = text;
        [JsonProperty("sender")] public SenderTypes SenderType { get; set; } = senderType;
        [JsonProperty("time")] public DateTime Time { get; set; } = DateTime.Now;
        [JsonProperty("sub")] public List<UserMessage> SubTexts { get; set; } = [];
    }
}
