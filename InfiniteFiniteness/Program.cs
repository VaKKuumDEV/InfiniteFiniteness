using InfiniteFiniteness.Chat;
using InfiniteFiniteness.Models;
using InfiniteFiniteness.Util;
using LikhodedDynamics.Sber.GigaChatSDK;
using LikhodedDynamics.Sber.GigaChatSDK.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

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
                if (message.StartsWith("/reset") || message.StartsWith("/start"))
                {
                    dialog.Clear();
                    await BotMethods.InitDialog(botClient, chatId.Value, cancellationToken);
                }
                else if (dialog.NextAction == UserDialog.UserActions.SET_PROMPT && dialog.Prompt == null && message.Trim().Length > 0) await BotMethods.SetDialogPrompt(botClient, chatWorker, dialog, message, chatId.Value, messageId.Value, cancellationToken);
            }
            else if (messageType.Value == MessageTypes.FROM_BUTTON)
            {
                if (dialog.NextAction == UserDialog.UserActions.SET_PROMPT) await BotMethods.SetDialogPrompt(botClient, chatWorker, dialog, message, chatId.Value, messageId.Value, cancellationToken);
                else if (dialog.NextAction == UserDialog.UserActions.CHOOSE_SCENE) await BotMethods.ChooseScene(botClient, chatWorker, dialog, message, chatId.Value, cancellationToken);
                else if (dialog.NextAction == UserDialog.UserActions.SEND_ACTION) await BotMethods.SendAction(botClient, chatWorker, dialog, message, chatId.Value, cancellationToken);
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
