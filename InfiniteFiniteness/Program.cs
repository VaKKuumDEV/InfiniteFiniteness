using InfiniteFiniteness.Chat;
using InfiniteFiniteness.Models;
using LikhodedDynamics.Sber.GigaChatSDK;
using LikhodedDynamics.Sber.GigaChatSDK.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

List<string> avPrompts = ["Warhammer 40000", "Звездные войны", "Бесконечное лето", "Метро 2033", "Властелин колец", "Киновселенная Марвел", "Киновслеенная DC", "Дюна"];

GigaChat Chat = new("ZTgzNzcyZTQtZGIxMy00NDc0LTg0MjctZWViODIzYzdjYTczOjkxMzgwN2NmLWU1ZWMtNDJhMy04NWI2LWMzMmY0N2Y1MThhMg==", false, true, false);
TelegramBotClient botClient = new("7104703085:AAH4vOdMjB2ogXHmzhpM8ubo2mcLIpOf0LM");

using CancellationTokenSource cts = new();

ReceiverOptions receiverOptions = new()
{
    AllowedUpdates = []
};

Token token = await Chat.CreateTokenAsync();
ChatWorker chatWorker = new(Chat);
Thread workerThread = new(new ThreadStart(() =>
{
    while (true)
    {
        chatWorker.Tick();
        Thread.Sleep(1);
    }
}));
workerThread.Start();

botClient.StartReceiving(updateHandler: HandleUpdateAsync, pollingErrorHandler: HandlePollingErrorAsync, receiverOptions: receiverOptions, cancellationToken: cts.Token);

var me = await botClient.GetMeAsync();
Console.WriteLine($"Start listening for @{me.Username}");
Console.ReadLine();
cts.Cancel();

async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    long? chatId = null;
    string? message = null;
    int? messageId = null;
    MessageTypes? messageType = null;

    if (update.Message is { } textMessage)
    {
        if (textMessage.Text is not { } messageText) return;

        chatId = textMessage.Chat.Id;
        message = messageText;
        messageId = textMessage.MessageId;
        messageType = MessageTypes.FROM_TEXT;
    }
    else if (update.CallbackQuery is { } query)
    {
        if (query.Message is not { } queryMessage) return;
        if (query.Data is not { } action) return;

        chatId = queryMessage.Chat.Id;
        message = action;
        messageId = queryMessage.MessageId;
        messageType = MessageTypes.FROM_BUTTON;
    }

    if (chatId != null && message != null && messageId != null && messageType != null)
    {
        UserDialog dialog = new(chatId.Value);
        if (System.IO.File.Exists(UserDialog.GetFileName(chatId.Value))) dialog = UserDialog.LoadFromFile(UserDialog.GetFileName(chatId.Value));

        try
        {
            if (messageType.Value == MessageTypes.FROM_TEXT)
            {
                Console.WriteLine($"Received a '{message}' message in chat {chatId}.");
                if (message.StartsWith("/reset")) dialog.Clear();

                if (dialog.NextAction == UserDialog.UserActions.SET_PROMPT)
                {
                    List<List<InlineKeyboardButton>> buttons = [];
                    foreach (string prompt in avPrompts) buttons.Add([InlineKeyboardButton.WithCallbackData(prompt)]);
                    var ikm = new InlineKeyboardMarkup(buttons);

                    Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Выберите тему", replyMarkup: ikm, cancellationToken: cancellationToken);
                }
            }
            else if (messageType.Value == MessageTypes.FROM_BUTTON)
            {
                if (dialog.NextAction == UserDialog.UserActions.SET_PROMPT)
                {
                    dialog.Prompt = message;
                    dialog.Save();

                    try
                    {
                        await botClient.DeleteMessageAsync(chatId.Value, messageId.Value, cancellationToken: cancellationToken);
                    }
                    catch (Exception) { }

                    ChatTask chatTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_PROMPT, [message]));
                    if (chatTask.Result is not { } generatedPrompts) return;

                    Message botMessage = await botClient.SendTextMessageAsync(chatId: chatId, text: "Завязка сюжета:\n" + generatedPrompts.First(), cancellationToken: cancellationToken);

                    ChatTask chatTask2 = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE, [dialog.Prompt, generatedPrompts.First()]));
                    if (chatTask2.Result is not { } generatedPrompts2) return;

                    List<UserMessage> subMessages = [];
                    List<List<InlineKeyboardButton>> buttons = [];
                    for (int i = 0; i < generatedPrompts2.Count; i++)
                    {
                        Message situationBotMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Ситуация " + (i + 1) + ":\n" + generatedPrompts2[i], cancellationToken: cancellationToken);
                        subMessages.Add(new(situationBotMessage.MessageId, generatedPrompts2[i], UserMessage.SenderTypes.BOT));
                        buttons.Add([InlineKeyboardButton.WithCallbackData("Ситуация " + (i + 1), "situation-" + i)]);
                    }

                    var ikm = new InlineKeyboardMarkup(buttons);
                    Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Выберите начало сюжета из предложенных", replyMarkup: ikm, cancellationToken: cancellationToken);

                    dialog.Situation = generatedPrompts.First();
                    dialog.NextAction = UserDialog.UserActions.CHOOSE_SCENE;
                    dialog.Messages.Add(new(sentMessage.MessageId, generatedPrompts.First(), UserMessage.SenderTypes.BOT) { SubTexts = subMessages });
                    dialog.Save();
                }
                else if (dialog.NextAction == UserDialog.UserActions.CHOOSE_SCENE)
                {
                    if (!message.StartsWith("situation-")) return;
                    int sceneIndex = Convert.ToInt32(message.Split("-")[1]);
                    if (dialog.Messages.Count == 0) return;

                    UserMessage lastMessage = dialog.Messages.Last();
                    if (sceneIndex >= lastMessage.SubTexts.Count) return;

                    try
                    {
                        await botClient.DeleteMessageAsync(chatId.Value, lastMessage.Id, cancellationToken: cancellationToken);
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
                                await botClient.DeleteMessageAsync(chatId.Value, lastMessage.SubTexts[i].Id, cancellationToken: cancellationToken);
                            }
                            catch (Exception) { }
                        }
                    }

                    if (dialog.Prompt == null || dialog.Situation == null || dialog.Scenes.Count == 0) return;

                    ChatTask chatTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ACTION, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last()]));
                    if (chatTask.Result is not { } generatedPrompts) return;

                    Message botMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Произошло следующее действие:\n" + generatedPrompts.First(), cancellationToken: cancellationToken);

                    List<KeyValuePair<string, string>> context = [new("assistant", generatedPrompts.First())];

                    ChatTask chatTask2 = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ANSWERS, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last(), generatedPrompts.First()]) { LastContext = context });
                    if (chatTask2.Result is not { } generatedPrompts2) return;

                    List<UserMessage> subMessages = [];
                    List<List<InlineKeyboardButton>> buttons = [];
                    for (int i = 0; i < generatedPrompts2.Count; i++)
                    {
                        Message situationBotMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Действие " + (i + 1) + ":\n" + generatedPrompts2[i], cancellationToken: cancellationToken);
                        subMessages.Add(new(situationBotMessage.MessageId, generatedPrompts2[i], UserMessage.SenderTypes.BOT));
                        buttons.Add([InlineKeyboardButton.WithCallbackData("Действие " + (i + 1), "action-" + i)]);
                    }

                    var ikm = new InlineKeyboardMarkup(buttons);
                    Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Выберите из списка:", replyMarkup: ikm, cancellationToken: cancellationToken);

                    dialog.NextAction = UserDialog.UserActions.SEND_ACTION;
                    dialog.Messages.Clear();
                    dialog.Messages.Add(new(sentMessage.MessageId, generatedPrompts.First(), UserMessage.SenderTypes.BOT) { SubTexts = subMessages });
                    dialog.Save();
                }
                else if (dialog.NextAction == UserDialog.UserActions.SEND_ACTION)
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

                        Message newSceneMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Внезапно ситуация изменилась:\n" + generatedScenePrompts.First(), cancellationToken: cancellationToken);

                        dialog.Scenes.Add(generatedScenePrompts.First());
                        dialog.Messages.Clear();
                        dialog.UserActionsCount = 0;
                        dialog.Save();
                    }

                    try
                    {
                        await botClient.DeleteMessageAsync(chatId.Value, lastMessage.Id, cancellationToken: cancellationToken);
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
                                await botClient.DeleteMessageAsync(chatId.Value, lastMessage.SubTexts[i].Id, cancellationToken: cancellationToken);
                            }
                            catch (Exception) { }
                        }
                    }

                    List<KeyValuePair<string, string>> context = [];
                    foreach (UserMessage msg in dialog.Messages) context.Add(new(msg.SenderType == UserMessage.SenderTypes.USER ? "user" : "assistant", msg.Text));

                    ChatTask chatTask = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ACTION, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last()]) { LastContext = context });
                    if (chatTask.Result is not { } generatedPrompts) return;

                    Message botMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Произошло следующее действие:\n" + generatedPrompts.First(), cancellationToken: cancellationToken);

                    context.Add(new("assistant", generatedPrompts.First()));
                    ChatTask chatTask2 = await chatWorker.AddTask(new(ChatTask.Types.GENERATE_SCENE_ANSWERS, [dialog.Prompt, dialog.Situation, dialog.Scenes.Last(), generatedPrompts.First()]) { LastContext = context });
                    if (chatTask2.Result is not { } generatedPrompts2) return;

                    List<UserMessage> subMessages = [];
                    List<List<InlineKeyboardButton>> buttons = [];
                    for (int i = 0; i < generatedPrompts2.Count; i++)
                    {
                        Message situationBotMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Действие " + (i + 1) + ":\n" + generatedPrompts2[i], cancellationToken: cancellationToken);
                        subMessages.Add(new(situationBotMessage.MessageId, generatedPrompts2[i], UserMessage.SenderTypes.BOT));
                        buttons.Add([InlineKeyboardButton.WithCallbackData("Действие " + (i + 1), "action-" + i)]);
                    }

                    var ikm = new InlineKeyboardMarkup(buttons);
                    Message sentMessage = await botClient.SendTextMessageAsync(chatId: chatId.Value, text: "Выберите из списка:", replyMarkup: ikm, cancellationToken: cancellationToken);

                    dialog.NextAction = UserDialog.UserActions.SEND_ACTION;
                    dialog.UserActionsCount++;
                    dialog.Messages.Add(new(sentMessage.MessageId, generatedPrompts.First(), UserMessage.SenderTypes.BOT) { SubTexts = subMessages });
                    dialog.Save();
                }
            }
        }
        catch (Exception ex)
        {
            var ErrorMessage = ex switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => ex.ToString()
            };

            Console.WriteLine(ErrorMessage);
        }
    }
}

Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    var ErrorMessage = exception switch
    {
        ApiRequestException apiRequestException
            => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
        _ => exception.ToString()
    };

    Console.WriteLine(ErrorMessage);
    return Task.CompletedTask;
}

enum MessageTypes
{
    FROM_TEXT,
    FROM_BUTTON,
};
