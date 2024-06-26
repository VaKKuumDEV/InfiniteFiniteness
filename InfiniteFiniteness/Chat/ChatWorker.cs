﻿using LikhodedDynamics.Sber.GigaChatSDK;
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
            List<MessageContent> messages = [];
            int count = 1;

            if (task.Type == ChatTask.Types.GENERATE_PROMPT)
            {
                if (task.Params.Count == 0) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];

                systemMessage = "Мы с тобой играем в новеллу. Я играю от лица главного героя. Мы находимся во вселенной \"" + prompt + "\". Придумай завязку сюжета. Длина до 5 предложений без каких-либо предложений и комментариев";
            }
            else if (task.Type == ChatTask.Types.GENERATE_SCENE)
            {
                if (task.Params.Count < 2) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];
                string scene = task.Params[1];
                count = 4;

                string sitKey = "положительном";
                int rand = new Random().Next(1, 100);
                if (rand <= 25) sitKey = "отрицательном";
                else if (rand <= 50) sitKey = "нейтральном";

                systemMessage = "Мы с тобой играем в новеллу. Я играю от лица главного героя. Мы находимся во вселенной \"" + prompt + "\". Завязка сюжета: \"" + scene + "\"";
                message = "Создай сцену в " + sitKey + " ключе для данной завязки на основе моих ответов и действий. Продолжай сюжет в рамках завязки. Длина до 2 предложений без каких-либо предложений и комментариев";
            }
            else if (task.Type == ChatTask.Types.GENERATE_SCENE_ACTION)
            {
                if (task.Params.Count < 3) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];
                string situation = task.Params[1];
                string scene = task.Params[2];

                systemMessage = "Мы с тобой играем в новеллу. Я играю от лица главного героя. Мы находимся во вселенной \"" + prompt + "\". Завязка сюжета: \"" + situation + "\". Происходящее событие: \"" + scene + "\". Придумай реплику второстепенного персонажа или поворотное для сюжета действие. Длина до 2 предложений. Продолжи развитие сюжета без каких-либо пояснений и комментариев к своему сообщению";
            }
            else if (task.Type == ChatTask.Types.GENERATE_SCENE_ANSWERS)
            {
                if (task.Params.Count < 3) throw new ArgumentException("Prompt not sent");
                string prompt = task.Params[0];
                string situation = task.Params[1];
                string scene = task.Params[2];
                count = 4;

                systemMessage = "Мы с тобой играем в новеллу. Я играю от лица главного героя. Мы находимся во вселенной \"" + prompt + "\". Завязка сюжета: \"" + situation + "\". Происходящее событие: \"" + scene + "\"";
                message = "Придумай ответ или действие от первого лица. Длина до 2 предложений без каких-либо пояснений и комментариев";
            }

            if (systemMessage != null)
            {
                messages.Insert(0, new("system", systemMessage));
                messages.AddRange(task.LastContext.ConvertAll(item => new MessageContent(item.Key, item.Value)));
                if (message != null) messages.Add(new("user", message));

                Response? response = await Chat.CompletionsAsync(new MessageQuery(messages, temperature: 1.2f, top_p: 0.7f, n: count));
                if (response != null)
                {
                    taskIndex = Tasks.FindIndex(item => item.Id == task.Id);
                    if (taskIndex == -1) return;

                    Tasks[taskIndex].SendMessage = message;
                    Tasks[taskIndex].Result = response.choices?.Where(item => item.finish_reason != "blacklist").Select(item => item.message?.content ?? "").Select(item => item.Replace("Ситуация: ", "").Replace("Событие: ", "")).Where(item => item.Trim().Length > 0).Distinct().ToList() ?? [];
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
