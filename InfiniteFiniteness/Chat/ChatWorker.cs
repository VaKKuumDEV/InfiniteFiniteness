using LikhodedDynamics.Sber.GigaChatSDK;
using LikhodedDynamics.Sber.GigaChatSDK.Models;
using System.Threading.Tasks;

namespace InfiniteFiniteness.Chat
{
    public class ChatWorker
    {
        public const int MAX_PARALLEL_SIZE = 1;

        private static ChatWorker? _instance = null;
        public static ChatWorker Instance { get => _instance ?? throw new NullReferenceException(); }

        private List<ChatTask> Tasks { get; } = [];
        private int InWork { get; set; } = 0;
        private GigaChat Chat { get; }

        public ChatWorker(GigaChat chat)
        {
            Chat = chat;
            _instance = this;
        }

        private int GetNextTask()
        {
            for (int i = 0; i < Tasks.Count; i++) if (!Tasks[i].Busy) return i;
            return -1;
        }

        public void Tick()
        {
            int nextTaskIndex;
            while (MAX_PARALLEL_SIZE - InWork > 0 && (nextTaskIndex = GetNextTask()) != -1)
            {
                InWork++;
                Tasks[nextTaskIndex].Busy = true;
                ChatTask task = Tasks[nextTaskIndex];
                Thread thread = new(new ThreadStart(() => ExecuteTaskAsync(task)));
                thread.Start();
            }
        }

        private async void ExecuteTaskAsync(ChatTask task)
        {
            int taskIndex = -1;
            string? message = null;
            string? systemMessage = null;
            int count = 1;

            if (task.Type == ChatTask.Types.GENERATE_PROMPT)
            {
                if (task.Params.Count == 0) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];

                systemMessage = "Мы с тобой играем в новеллу. Мы находимся во вселенной \"" + prompt + "\"";
                message = "Придумай завязку сюжета. Длина - не более пяти предложений";
            }
            else if (task.Type == ChatTask.Types.GENERATE_SCENE)
            {
                if (task.Params.Count < 2) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];
                string scene = task.Params[1];
                count = 4;

                systemMessage = "Мы с тобой играем в новеллу. Мы находимся во вселенной \"" + prompt + "\". Завязка сюжета: \"" + scene + "\"";
                message = "Придумай возможную ситуацию для данной завязки. Не повторяй с предыдущими сообщениями. Продолжай сюжет в рамках завязки. Длина - не более двух предложений";
            }
            else if (task.Type == ChatTask.Types.GENERATE_SCENE_ACTION)
            {
                if (task.Params.Count < 3) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];
                string situation = task.Params[1];
                string scene = task.Params[2];

                string actionType = "реплику от лица любого второстепенного персонажа";
                task.ResultType = ChatTask.ResultTypes.DIALOG;
                if (new Random().Next(1, 100) <= 50)
                {
                    actionType = "событие со стороны предметов окружения или от других второстепенных персонажей";
                    task.ResultType = ChatTask.ResultTypes.ACTION;
                }

                systemMessage = "Мы с тобой играем в новеллу. Мы находимся во вселенной \"" + prompt + "\". Завязка сюжета: \"" + situation + "\". Происходящее событие: \"" + scene + "\". Отвечай от имени игровых персонажей";
                message = "Придумай " + actionType + ". Длина - не более двух предложений. Не повторяй предыдущие ответы. Старайся учитывать предыдущий контекст и продолжать развитие сюжета";
            }
            else if (task.Type == ChatTask.Types.GENERATE_SCENE_ANSWERS)
            {
                if (task.Params.Count < 5) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];
                string situation = task.Params[1];
                string scene = task.Params[2];
                string type = task.Params[3];
                count = 4;

                string actionType = "мой ответ от первого лица на реплику от второстепенного персонажа";
                if (type == "action") actionType = "мое действие в ответ на возникнувшую ситуацию";

                systemMessage = "Мы с тобой играем в новеллу. Мы находимся во вселенной \"" + prompt + "\". Завязка сюжета: \"" + situation + "\". Происходящее событие: \"" + scene + "\"";
                message = "Придумай " + actionType + " со случайным окрасом контекста. Длина - не более двух предложений";
            }

            if (message != null && systemMessage != null)
            {
                List<MessageContent> messages = [new("system", systemMessage)];
                messages.AddRange(task.LastContext.ConvertAll(item => new MessageContent(item.Key, item.Value)));
                messages.Add(new("user", message));

                Response? response = await Chat.CompletionsAsync(new MessageQuery(messages, temperature: 1.2f, top_p: 0.7f, n: count));
                if (response != null)
                {
                    taskIndex = Tasks.FindIndex(item => item.Id == task.Id);
                    if (taskIndex == -1) return;

                    Tasks[taskIndex].SendMessage = message;
                    Tasks[taskIndex].Result = response.choices?.Where(item => item.message != null).Select(item => item.message.content).ToList();
                    Tasks[taskIndex].Completed = true;
                }
            }

            if (taskIndex == -1) return;
            Tasks[taskIndex].Completed = true;
            InWork--;
        }

        public async Task<ChatTask> AddTask(ChatTask task)
        {
            Tasks.Add(task);
            await Task.Run(() =>
            {
                while (true)
                {
                    int taskIndex = Tasks.FindIndex(new(listTask => listTask.Id == task.Id));
                    if (taskIndex == -1) break;

                    if (Tasks[taskIndex].Completed)
                    {
                        task = Tasks[taskIndex];
                        break;
                    }
                }
            });

            InWork--;
            Tasks.RemoveAll(item => item.Id == task.Id);
            return task;
        }
    }
}
