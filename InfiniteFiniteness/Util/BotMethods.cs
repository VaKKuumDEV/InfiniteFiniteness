using InfiniteFiniteness.Chat;
using InfiniteFiniteness.Models;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;

namespace InfiniteFiniteness.Util
{
    public static class BotMethods
    {
        public static readonly string[] PROMPTS = ["Warhammer 40000", "Звездные войны", "Бесконечное лето", "Метро 2033", "Властелин колец", "Киновселенная Марвел", "Киновслеенная DC", "Дюна"];

        public static async Task InitDialog(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            List<List<InlineKeyboardButton>> buttons = [];
            foreach (string prompt in PROMPTS) buttons.Add([InlineKeyboardButton.WithCallbackData(prompt)]);
            var ikm = new InlineKeyboardMarkup(buttons);
            _ = await botClient.SendTextMessageAsync(chatId: chatId, text: "Выберите тему или введите свою", replyMarkup: ikm, cancellationToken: cancellationToken);
        }

        public static async Task SetDialogPrompt(ITelegramBotClient botClient, ChatWorker chatWorker, UserDialog dialog, string message, long chatId, int messageId, CancellationToken cancellationToken)
        {
            dialog.Prompt = message;
            dialog.Save();
            try
            {
                await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken: cancellationToken);
            }
            catch (Exception) { }

            ChatTask chatTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_PROMPT, [message]));
            if (chatTask.Result is not { } generatedPrompts) return;
            _ = await botClient.SendTextMessageAsync(chatId: chatId, text: "Завязка сюжета:\n" + generatedPrompts.First(), cancellationToken: cancellationToken);

            ChatTask chatTask2 = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE, [dialog.Prompt, generatedPrompts.First()]));
            if (chatTask2.Result is not { } generatedPrompts2) return;

            List<UserMessage> subMessages = [];
            List<List<InlineKeyboardButton>> buttons = [];
            for (int i = 0; i < generatedPrompts2.Count; i++)
            {
                Message situationBotMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Ситуация " + (i + 1) + ":\n" + generatedPrompts2[i], cancellationToken: cancellationToken);
                subMessages.Add(new(situationBotMessage.MessageId, generatedPrompts2[i], UserMessage.SenderTypes.BOT));
                buttons.Add([InlineKeyboardButton.WithCallbackData("Ситуация " + (i + 1), "situation-" + i)]);
            }
            var ikm = new InlineKeyboardMarkup(buttons);
            Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Выберите начало сюжета из предложенных", replyMarkup: ikm, cancellationToken: cancellationToken);

            dialog.Situation = generatedPrompts.First();
            dialog.NextAction = UserDialog.UserActions.CHOOSE_SCENE;
            dialog.Messages.Add(new(sentMessage.MessageId, generatedPrompts.First(), UserMessage.SenderTypes.BOT) { SubTexts = subMessages });
            dialog.Save();
        }

        public static async Task ChooseScene(ITelegramBotClient botClient, ChatWorker chatWorker, UserDialog dialog, string message, long chatId, CancellationToken cancellationToken)
        {
            if (!message.StartsWith("situation-")) return;
            int sceneIndex = Convert.ToInt32(message.Split("-")[1]);
            if (dialog.Messages.Count == 0) return;

            UserMessage lastMessage = dialog.Messages.Last();
            if (sceneIndex >= lastMessage.SubTexts.Count) return;

            try
            {
                await botClient.DeleteMessageAsync(chatId, lastMessage.Id, cancellationToken: cancellationToken);
            }
            catch (Exception) { }

            for (int i = 0; i < lastMessage.SubTexts.Count; i++)
            {
                if (i == sceneIndex)
                {
                    string scene = lastMessage.SubTexts[i].Text;
                    dialog.Messages.Add(new(lastMessage.SubTexts[i].Id, scene, UserMessage.SenderTypes.USER));
                    dialog.Scenes.Add(scene);
                    dialog.Save();
                }
                else
                {
                    try
                    {
                        await botClient.DeleteMessageAsync(chatId, lastMessage.SubTexts[i].Id, cancellationToken: cancellationToken);
                    }
                    catch (Exception) { }
                }
            }

            if (dialog.Prompt == null || dialog.Situation == null || dialog.Scenes.Count == 0) return;

            ChatTask chatTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ACTION, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last()]));
            if (chatTask.Result is not { } generatedPrompts) return;
            _ = await botClient.SendTextMessageAsync(chatId: chatId, text: "Произошло следующее действие:\n" + generatedPrompts.First(), cancellationToken: cancellationToken);

            List<KeyValuePair<string, string>> context = [new("assistant", generatedPrompts.First())];

            ChatTask chatTask2 = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ANSWERS, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last(), generatedPrompts.First()]) { LastContext = context });
            if (chatTask2.Result is not { } generatedPrompts2) return;

            List<UserMessage> subMessages = [];
            List<List<InlineKeyboardButton>> buttons = [];
            for (int i = 0; i < generatedPrompts2.Count; i++)
            {
                Message situationBotMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Действие " + (i + 1) + ":\n" + generatedPrompts2[i], cancellationToken: cancellationToken);
                subMessages.Add(new(situationBotMessage.MessageId, generatedPrompts2[i], UserMessage.SenderTypes.BOT));
                buttons.Add([InlineKeyboardButton.WithCallbackData("Действие " + (i + 1), "action-" + i)]);
            }

            var ikm = new InlineKeyboardMarkup(buttons);
            Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Выберите из списка:", replyMarkup: ikm, cancellationToken: cancellationToken);

            dialog.NextAction = UserDialog.UserActions.SEND_ACTION;
            dialog.Messages.Clear();
            dialog.Messages.Add(new(sentMessage.MessageId, generatedPrompts.First(), UserMessage.SenderTypes.BOT) { SubTexts = subMessages });
            dialog.Save();
        }

        public static async Task SendAction(ITelegramBotClient botClient, ChatWorker chatWorker, UserDialog dialog, string message, long chatId, CancellationToken cancellationToken)
        {
            int actionsNeedToUpdate = 5;

            if (!message.StartsWith("action-") && !message.StartsWith("dialog-")) return;
            int sceneIndex = Convert.ToInt32(message.Split("-")[1]);
            string userActionType = message.Split("-")[0];
            if (dialog.Messages.Count == 0) return;

            UserMessage lastMessage = dialog.Messages.Last();
            if (sceneIndex >= lastMessage.SubTexts.Count) return;
            if (dialog.Prompt == null || dialog.Situation == null || dialog.Scenes.Count == 0) return;

            int probability = (int)Math.Round((double)dialog.UserActionsCount / actionsNeedToUpdate * 100.0);
            if (new Random().Next(1, 100) <= probability)
            {
                List<KeyValuePair<string, string>> scenes = [];
                foreach (string scene in dialog.Scenes) scenes.Add(new("assistant", scene));
                scenes.AddRange(dialog.Messages.ConvertAll(item => new KeyValuePair<string, string>(item.SenderType == UserMessage.SenderTypes.BOT ? "assistant" : "user", item.Text)));

                ChatTask changeSceneTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE, [dialog.Prompt, dialog.Situation]) { LastContext = scenes });
                if (changeSceneTask.Result is not { } generatedScenePrompts) return;

                Message newSceneMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Внезапно ситуация изменилась:\n" + generatedScenePrompts.First(), cancellationToken: cancellationToken);

                dialog.Scenes.Add(generatedScenePrompts.First());
                dialog.Messages.Clear();
                dialog.UserActionsCount = 0;
                dialog.Save();
            }

            try
            {
                await botClient.DeleteMessageAsync(chatId, lastMessage.Id, cancellationToken: cancellationToken);
            }
            catch (Exception) { }

            for (int i = 0; i < lastMessage.SubTexts.Count; i++)
            {
                if (i == sceneIndex)
                {
                    string action = lastMessage.SubTexts[i].Text;
                    dialog.Messages.Add(new(lastMessage.SubTexts[i].Id, action, UserMessage.SenderTypes.USER));
                    dialog.Save();
                }
                else
                {
                    try
                    {
                        await botClient.DeleteMessageAsync(chatId, lastMessage.SubTexts[i].Id, cancellationToken: cancellationToken);
                    }
                    catch (Exception) { }
                }
            }

            List<KeyValuePair<string, string>> context = [];
            foreach (UserMessage msg in dialog.Messages) context.Add(new(msg.SenderType == UserMessage.SenderTypes.USER ? "user" : "assistant", msg.Text));

            ChatTask chatTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ACTION, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last()]) { LastContext = context });
            if (chatTask.Result is not { } generatedPrompts) return;

            Message botMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Произошло следующее действие:\n" + generatedPrompts.First(), cancellationToken: cancellationToken);

            context.Add(new("assistant", generatedPrompts.First()));
            ChatTask chatTask2 = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ANSWERS, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last(), generatedPrompts.First()]) { LastContext = context });
            if (chatTask2.Result is not { } generatedPrompts2) return;

            List<UserMessage> subMessages = [];
            List<List<InlineKeyboardButton>> buttons = [];
            for (int i = 0; i < generatedPrompts2.Count; i++)
            {
                Message situationBotMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Действие " + (i + 1) + ":\n" + generatedPrompts2[i], cancellationToken: cancellationToken);
                subMessages.Add(new(situationBotMessage.MessageId, generatedPrompts2[i], UserMessage.SenderTypes.BOT));
                buttons.Add([InlineKeyboardButton.WithCallbackData("Действие " + (i + 1), "action-" + i)]);
            }

            var ikm = new InlineKeyboardMarkup(buttons);
            Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Выберите из списка:", replyMarkup: ikm, cancellationToken: cancellationToken);

            dialog.NextAction = UserDialog.UserActions.SEND_ACTION;
            dialog.UserActionsCount++;
            dialog.Messages.Add(new(sentMessage.MessageId, generatedPrompts.First(), UserMessage.SenderTypes.BOT) { SubTexts = subMessages });
            dialog.Save();
        }
    }
}
