using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.IO;
using System.Text.Json;

class Program
{
    private static readonly string BotToken = "7552750004:AAH0dUdyqw4fA-NK0vKwmIP2u1n09wZYEy0"; // Укажите ваш токен бота
    private static readonly TelegramBotClient BotClient = new TelegramBotClient(BotToken);

    // Список Telegram ID админов
    private static readonly List<long> AdminIds = new() { 969972393 }; // Укажите ID админа

    // Список розыгрышей
    private static readonly Dictionary<string, (string Description, string? CoverFileId)> Raffles = new(); // Название -> (Описание, ID Обложки)

    // Хранилище состояний пользователей
    private static readonly Dictionary<long, string> UserStates = new();

    private static readonly string HistoryFilePath = "raffle_history.json";

    // Константы состояний
    private const string StateCreatingRaffle = "creating_raffle";
    private const string StateEditingRaffle = "editing_raffle";
    private const string StateEditingRaffleName = "editing_raffle_name";
    private const string StateEditingRafflePhoto = "editing_raffle_photo";
    private const string StateEditingRaffleDescription = "editing_raffle_description";
    private const string StateDeletingRaffle = "deleting_raffle";
    // Хранилище участников розыгрышей
    private static readonly Dictionary<string, List<long>> Participants = new();
    private static void SaveDataToFile()
    {
        var raffleData = new RaffleDataModel
        {
            Raffles = Raffles.ToDictionary(
                kvp => kvp.Key,
                kvp => new RaffleDetails
                {
                    Description = kvp.Value.Description,
                    CoverFileId = kvp.Value.CoverFileId
                }),
            Participants = Participants
        };

        var json = JsonSerializer.Serialize(raffleData, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(HistoryFilePath, json);
    }


    private static void LoadDataFromFile()
    {
        if (System.IO.File.Exists(HistoryFilePath))
        {
            try
            {
                var json = System.IO.File.ReadAllText(HistoryFilePath);

                // Проверяем, не пустой ли файл
                if (string.IsNullOrWhiteSpace(json))
                {
                    Console.WriteLine("Файл пуст, создаем новый.");
                    InitializeEmptyHistoryFile();
                    return;
                }

                var raffleData = JsonSerializer.Deserialize<RaffleDataModel>(json);

                if (raffleData != null)
                {
                    // Загружаем розыгрыши
                    foreach (var raffle in raffleData.Raffles)
                    {
                        Raffles[raffle.Key] = (raffle.Value.Description, raffle.Value.CoverFileId);
                    }

                    // Загружаем участников
                    foreach (var participant in raffleData.Participants)
                    {
                        Participants[participant.Key] = participant.Value;
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Ошибка чтения JSON: {ex.Message}");
                InitializeEmptyHistoryFile();
            }
        }
        else
        {
            InitializeEmptyHistoryFile();
        }
    }

    // Метод для создания пустого файла истории
    private static void InitializeEmptyHistoryFile()
    {
        var emptyData = new RaffleDataModel
        {
            Raffles = new Dictionary<string, RaffleDetails>(),
            Participants = new Dictionary<string, List<long>>()
        };

        var json = JsonSerializer.Serialize(emptyData, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(HistoryFilePath, json);
        Console.WriteLine("Создан новый файл истории.");
    }






    static async Task Main(string[] args)
    {
        Console.WriteLine("Бот запущен...");

        LoadDataFromFile();

        var cts = new CancellationTokenSource();

        // Настройки для приема сообщений
        BotClient.StartReceiving(
     HandleUpdateAsync,
     HandleErrorAsync,
     new ReceiverOptions
     {
         AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery } // Поддержка callback data
     },
     cancellationToken: cts.Token
 );
        // Ожидаем завершение работы бота
        await Task.Delay(-1);

        // Сохраняем данные перед завершением работы
        SaveDataToFile();

        Console.WriteLine("Нажмите Enter для остановки бота.");
        Console.ReadLine();

        cts.Cancel();
        SaveDataToFile();
    }

    // Обработка входящих обновлений
    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not null) // Проверка на наличие текстового сообщения
        {
            Console.WriteLine($"Получено сообщение: {update.Message.Text}");
            await HandleMessageAsync(botClient, update.Message, cancellationToken);
        }
        else if (update.CallbackQuery is not null) // Проверка на наличие callback_query
        {
            Console.WriteLine($"Получен callback_query: {update.CallbackQuery.Data}");
            await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
        }
        else
        {
            Console.WriteLine("Получено обновление, не являющееся сообщением или callback_query, игнорируем.");
        }
    }

    private static async Task ShowRafflesListAsync(long chatId)
    {
        // Если данные не загружены, сообщаем пользователю
        if (Raffles.Count == 0)
        {
            await BotClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Нет доступных розыгрышей.",
                cancellationToken: CancellationToken.None
            );
            return;
        }

        // Формируем сообщение о текущих розыгрышах
        var text = "Список доступных розыгрышей:\n";
        foreach (var raffle in Raffles)
        {
            text += $"🎉 {raffle.Key}: {raffle.Value.Description}\n";
        }

        await BotClient.SendTextMessageAsync(
            chatId: chatId,
            text: text,
            cancellationToken: CancellationToken.None
        );
    }




    // Обработка ошибок
    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Ошибка Telegram API: {apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine($"Произошла ошибка: {errorMessage}");
        return Task.CompletedTask;
    }

    private static void SaveRaffleHistory(string raffleName, string winner, DateTime dateTime)
    {
        var historyEntry = new RaffleHistoryEntry
        {
            RaffleName = raffleName,
            Winner = winner,
            DateTime = dateTime.ToString("yyyy-MM-dd HH:mm")
        };

        List<RaffleHistoryEntry> history;
        if (System.IO.File.Exists(HistoryFilePath))
        {
            var existingData = System.IO.File.ReadAllText(HistoryFilePath);
            history = JsonSerializer.Deserialize<List<RaffleHistoryEntry>>(existingData) ?? new List<RaffleHistoryEntry>();
        }
        else
        {
            history = new List<RaffleHistoryEntry>();
        }

        history.Add(historyEntry);

        var jsonData = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(HistoryFilePath, jsonData);
    }


    // Обработка сообщений
    private static async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var userId = message.From!.Id;
        var text = message.Text;

        Console.WriteLine($"[DEBUG] Получено сообщение: {text} от пользователя {userId}");


        // Обработка команды /start
        if (text == "/start")
        {
            await ShowMainMenu(botClient, chatId, cancellationToken);
            return;
        }

        // Обработка команды /adm для доступа к админскому меню
        if (text == "/adm")
        {
            if (AdminIds.Contains(userId))
            {
                await ShowAdminMenu(botClient, chatId, cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "У вас нет доступа к админскому меню.",
                    cancellationToken: cancellationToken);
            }
            return;
        }

        if (text == "Назад 🔙")
        {
            SaveDataToFile();
            await ShowMainMenu(botClient, chatId, cancellationToken);
            return;
        }

        // Обработка таймера для розыгрыша
        if (UserStates.ContainsKey(userId) && UserStates[userId].StartsWith("setting_timer"))
        {
            var userState = UserStates[userId]; // Извлекаем текущее состояние пользователя
            var raffleName = userState.Split(':')[1]; // Извлекаем название розыгрыша из состояния

            if (!Raffles.ContainsKey(raffleName))
            {
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Ошибка: Розыгрыш не найден.",
                    cancellationToken: cancellationToken);
                UserStates.Remove(userId);
                return;
            }

            if (text == "Назад 🚪")
            {
                if (AdminIds.Contains(userId))
                {
                    SaveDataToFile();
                    await ShowAdminMenu(botClient, chatId, cancellationToken); // Возврат в админское меню
                }
                else
                {
                    SaveDataToFile();
                    await ShowMainMenu(botClient, chatId, cancellationToken); // Возврат в пользовательское меню
                }
                UserStates.Remove(userId); // Сброс состояния пользователя
                return;
            }




            if (text == "Пропустить")
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Таймер не установлен. Розыгрыш \"{raffleName}\" создан!",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Назад 🚪") }) { ResizeKeyboard = true },
                    cancellationToken: cancellationToken);
                UserStates.Remove(userId);
                return;
            }

            if (TimeSpan.TryParse(text, out var timeSpan))
            {
                UserStates.Remove(userId);
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Таймер для розыгрыша \"{raffleName}\" установлен на {timeSpan}.",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Назад 🚪") }) { ResizeKeyboard = true },
                    cancellationToken: cancellationToken);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(timeSpan); // Ожидание по таймеру
                    await ConductRaffle(botClient, chatId, raffleName, cancellationToken); // Проведение розыгрыша по истечении времени
                });
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Неверный формат. Введите таймер в формате ЧЧ:ММ:",
                    cancellationToken: cancellationToken);
            }
            return;
        }

        if (text == "Выход 🚪")
        {
            await ShowMainMenu(botClient, chatId, cancellationToken); // Возврат в пользовательское меню
            UserStates.Remove(userId); // Сброс состояния пользователя
            return;
        }

        if (text == "Провести розыгрыш 🎉")
        {
            if (Raffles.Count == 0)
            {
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Нет доступных розыгрышей для проведения.",
                    cancellationToken: cancellationToken);
            }
            else
            {
                SaveDataToFile();
                await ShowRaffleSelectionMenu(botClient, chatId, "Выберите розыгрыш для проведения:", cancellationToken);
                UserStates[userId] = "conducting_raffle"; // Устанавливаем корректное состояние
            }
            return;
        }


        if (text == "Показать список розыгрышей 📋")
        {
            SaveDataToFile();
            await ShowRafflesList(botClient, chatId, cancellationToken);
            return;
        }

        // Обработка кнопок из админского меню
        if (AdminIds.Contains(userId))
        {
            // Создание нового розыгрыша
            if (text == "Создать розыгрыш ➕")
            {
                UserStates[userId] = StateCreatingRaffle;
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Введите название нового розыгрыша:",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                    cancellationToken: cancellationToken);
                return;
            }

            // Редактирование существующего розыгрыша
            if (text == "Редактировать розыгрыш ✏️")
            {
                if (Raffles.Count == 0)
                {
                    SaveDataToFile();
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Нет доступных розыгрышей для редактирования.",
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await ShowRaffleSelectionMenu(botClient, chatId, "Выберите розыгрыш для редактирования:", cancellationToken);
                    UserStates[userId] = StateEditingRaffle;
                }
                return;
            }

            if (text == "история розыгрышей")
            {
                Console.WriteLine("[DEBUG] Пользователь запросил историю розыгрышей");
                await ShowHistory(botClient, chatId, cancellationToken);
                return;
            }

            // Удаление существующего розыгрыша
            if (text == "Удалить розыгрыш 🗑️")
            {
                if (Raffles.Count == 0)
                {
                    SaveDataToFile();
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Нет доступных розыгрышей для удаления.",
                        cancellationToken: cancellationToken);
                }
                else
                {
                    SaveDataToFile();
                    await ShowRaffleSelectionMenu(botClient, chatId, "Выберите розыгрыш для удаления:", cancellationToken);
                    UserStates[userId] = StateDeletingRaffle;
                }
                return;
            }
        }

        if (UserStates.ContainsKey(userId) && UserStates[userId] == "conducting_raffle")
        {
            if (!Raffles.ContainsKey(text))
            {
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Ошибка: выбранный розыгрыш не найден.",
                    cancellationToken: cancellationToken);
                return;
            }
            SaveDataToFile();
            await ConductRaffle(botClient, chatId, text, cancellationToken); // Проводим розыгрыш
            UserStates.Remove(userId); // Сбрасываем состояние
            return;
        }

        // Проверка состояния пользователя при редактировании и удалении розыгрышей
        if (UserStates.ContainsKey(userId))
        {
            var userState = UserStates[userId];

            // Создание нового розыгрыша
            if (userState == StateCreatingRaffle)
            {
                if (!Raffles.ContainsKey(text))
                {
                    Raffles[text] = (Description: "", CoverFileId: null);
                    
                    UserStates[userId] = $"{StateEditingRafflePhoto}:{text}";
                    SaveDataToFile();
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Розыгрыш \"{text}\" создан! Пожалуйста, отправьте изображение для обложки:",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Пропустить"), new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Розыгрыш с таким названием уже существует. Попробуйте другое название.",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                }
                return;
            }

            // Редактирование названия розыгрыша
            if (userState.StartsWith(StateEditingRaffleName))
            {
                var oldName = userState.Split(':')[1];
                if (!Raffles.ContainsKey(oldName))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Ошибка: Розыгрыш не найден.",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                    UserStates.Remove(userId);
                    return;
                }

                if (Raffles.ContainsKey(text))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Розыгрыш с таким названием уже существует. Попробуйте другое название.",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                    return;
                }

                var raffle = Raffles[oldName];
                Raffles.Remove(oldName);
                Raffles[text] = raffle; // Обновляем название
               

                UserStates[userId] = $"{StateEditingRafflePhoto}:{text}";
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Название розыгрыша обновлено на \"{text}\". Теперь отправьте новое фото обложки (или нажмите \"Пропустить\"):",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Пропустить"), new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                    cancellationToken: cancellationToken);
                return;
            }

            // Редактирование фото розыгрыша
            if (userState.StartsWith(StateEditingRafflePhoto))
            {
                var raffleName = userState.Split(':')[1];
                if (!Raffles.ContainsKey(raffleName))
                {
                    SaveDataToFile();
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Ошибка: Розыгрыш не найден.",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                    UserStates.Remove(userId);
                    return;
                }

              




                if (text == "Выход 🚪")
                {
                    await ShowMainMenu(botClient, chatId, cancellationToken); // Возврат в пользовательское меню
                    UserStates.Remove(userId); // Сбрасываем состояние пользователя
                    return;
                }


                if (text == "Пропустить")
                {
                    UserStates[userId] = $"{StateEditingRaffleDescription}:{raffleName}";
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Фото обложки осталось прежним. Теперь отправьте новое описание розыгрыша:",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                }
                else if (message.Photo?.Any() == true)
                {
                    var fileId = message.Photo.Last().FileId;
                    var raffle = Raffles[raffleName];
                    Raffles[raffleName] = (raffle.Description, CoverFileId: fileId);

                    UserStates[userId] = $"{StateEditingRaffleDescription}:{raffleName}";
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Фото обложки обновлено. Теперь отправьте новое описание розыгрыша:",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                }
                else
                {
                    SaveDataToFile();
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Пожалуйста, отправьте изображение или нажмите \"Пропустить\".",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Пропустить"), new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                }
                return;
            }

            // Редактирование описания розыгрыша
            if (UserStates.ContainsKey(userId) && UserStates[userId].StartsWith(StateEditingRaffleDescription))
            {
                var raffleName = userState.Split(':')[1]; // Извлекаем название розыгрыша из состояния

                if (!Raffles.ContainsKey(raffleName))
                {
                    SaveDataToFile();
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Ошибка: Розыгрыш не найден.",
                        replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Назад 🚪") }) { ResizeKeyboard = true },
                        cancellationToken: cancellationToken);
                    UserStates.Remove(userId);
                    return;
                }

                var raffle = Raffles[raffleName];
                Raffles[raffleName] = (Description: text, CoverFileId: raffle.CoverFileId);

                // Установка таймера
                UserStates[userId] = $"setting_timer:{raffleName}";
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Введите таймер в формате ЧЧ:ММ для автоматического проведения розыгрыша, или нажмите \"Пропустить\":",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Пропустить"), new KeyboardButton("Назад 🚪") }) { ResizeKeyboard = true },
                    cancellationToken: cancellationToken);
                return;
            }



            // Удаление розыгрыша
            if (userState == StateDeletingRaffle && Raffles.ContainsKey(text))
            {
                Raffles.Remove(text);
                UserStates.Remove(userId);
                SaveDataToFile();
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Розыгрыш \"{text}\" удалён.",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                    cancellationToken: cancellationToken);
                return;
            }
        }

        // Если пользователь выбрал розыгрыш для редактирования
        if (text != null && Raffles.ContainsKey(text))
        {
            UserStates[userId] = $"{StateEditingRaffleName}:{text}";
            SaveDataToFile();
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Вы выбрали \"{text}\" для редактирования. Введите новое название:",
                replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("Выход 🚪") }) { ResizeKeyboard = true },
                cancellationToken: cancellationToken);
            return;
        }

        // Если команда не распознана
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Неизвестная команда.",
            cancellationToken: cancellationToken
        );
    }

    private static async Task ShowHistory(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        // Если файл отсутствует, создаем его
        if (!System.IO.File.Exists(HistoryFilePath))
        {
            System.IO.File.WriteAllText(HistoryFilePath, "[]"); // Создаем пустую историю
        }

        // Чтение содержимого файла
        var historyJson = System.IO.File.ReadAllText(HistoryFilePath);
        Console.WriteLine("[DEBUG] Содержимое истории: " + historyJson);
        var history = JsonSerializer.Deserialize<List<RaffleHistoryEntry>>(historyJson);

        // Проверяем, есть ли записи в истории
        if (history == null || !history.Any())
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "История розыгрышей пуста.",
                cancellationToken: cancellationToken
            );
            return;
        }

        // Форматируем историю для отображения
        var formattedHistory = string.Join("\n\n", history.Select(h =>
            $"🎉 *{h.RaffleName}*\nДата: {h.DateTime}\nПобедитель: {h.Winner}"));

        // Отправляем сообщение с историей
        await botClient.SendTextMessageAsync(
     chatId: chatId,
     text: $"История розыгрышей:\n\n{formattedHistory}",
     parseMode: ParseMode.Html,
     cancellationToken: cancellationToken
 );

 
    }
    // Модель для хранения записей истории
    private class RaffleHistoryEntry
    {
        public string RaffleName { get; set; }
        public string Winner { get; set; }
        public string DateTime { get; set; }
    }




    private static async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Data is null)
            return;

        if (callbackQuery.Data.StartsWith("participate_"))
        {
            var raffleName = callbackQuery.Data.Substring("participate_".Length);

            if (Raffles.ContainsKey(raffleName))
            {
                if (!Participants.ContainsKey(raffleName))
                {
                    Participants[raffleName] = new List<long>();
                }

                if (!Participants[raffleName].Contains(callbackQuery.From.Id))
                {
                    Participants[raffleName].Add(callbackQuery.From.Id);
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы успешно зарегистрированы в розыгрыше!");
                }
                else
                {
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы уже участвуете в этом розыгрыше!");
                }
            }
            else
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Розыгрыш не найден.");
            }
        }
    }


    private static async Task ConductRaffle(ITelegramBotClient botClient, long chatId, string raffleName, CancellationToken cancellationToken)
    {
        if (!Raffles.ContainsKey(raffleName))
        {
            await botClient.SendTextMessageAsync(chatId, $"Розыгрыш \"{raffleName}\" не найден.", cancellationToken: cancellationToken);
            return;
        }

        if (!Participants.ContainsKey(raffleName) || Participants[raffleName].Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId, $"В розыгрыше \"{raffleName}\" нет участников.", cancellationToken: cancellationToken);
            return;
        }

        var random = new Random();
        var winnerId = Participants[raffleName][random.Next(Participants[raffleName].Count)];
        var winner = $"Пользователь с ID {winnerId}";

        // Отправка сообщения о победителе
        await botClient.SendTextMessageAsync(chatId, $"🎉 Победитель розыгрыша \"{raffleName}\": [Пользователь](tg://user?id={winnerId})!",
            parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);

        // Сохранение истории
        SaveRaffleHistory(raffleName, winner, DateTime.Now);

        // Очистка участников и удаление розыгрыша
        Participants.Remove(raffleName);
        Raffles.Remove(raffleName);
    }



    private static async Task ShowRafflesList(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        if (Raffles.Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId: chatId, text: "Нет доступных розыгрышей.", cancellationToken: cancellationToken);
            return;
        }

        foreach (var raffle in Raffles)
        {
            var raffleName = raffle.Key;
            var raffleDescription = raffle.Value.Description;
            var coverFileId = raffle.Value.CoverFileId;

            InlineKeyboardMarkup inlineMarkup = new InlineKeyboardMarkup(new[]
            {
            InlineKeyboardButton.WithCallbackData("Участвовать", $"participate_{raffleName}")
        });

            if (!string.IsNullOrEmpty(coverFileId))
            {
                await botClient.SendPhotoAsync(chatId: chatId, photo: coverFileId, caption: $"🎉 *{raffleName}*\n\n{raffleDescription}",
                    parseMode: ParseMode.Markdown, replyMarkup: inlineMarkup, cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId: chatId, text: $"🎉 *{raffleName}*\n\n{raffleDescription}",
                    parseMode: ParseMode.Markdown, replyMarkup: inlineMarkup, cancellationToken: cancellationToken);
            }
        }
    }





    // Отображение главного меню для пользователей
    private static async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var replyMarkup = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Показать список розыгрышей 📋" }
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Добро пожаловать! Выберите действие:",
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }

    // Отображение админского меню
    private static async Task ShowAdminMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var replyMarkup = new ReplyKeyboardMarkup(new[]
        {
        new KeyboardButton[] { "Создать розыгрыш ➕", "Редактировать розыгрыш ✏️" },
        new KeyboardButton[] { "Удалить розыгрыш 🗑️", "Провести розыгрыш 🎉" },
        new KeyboardButton[] { "История розыгрышей", "Выход 🚪" }

    })
        {
            ResizeKeyboard = true
        };

        Console.WriteLine("[DEBUG] Отправка админского меню пользователю");
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Админское меню:",
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }






    private static async Task ShowRaffleSelectionMenu(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken)
    {
        var buttons = Raffles.Keys.Select(name => new KeyboardButton(name)).ToArray();
        var replyMarkup = new ReplyKeyboardMarkup(buttons.Chunk(2).ToArray())
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }
    private class RaffleDataModel
    {
        public Dictionary<string, RaffleDetails> Raffles { get; set; } = new();
        public Dictionary<string, List<long>> Participants { get; set; } = new();
    }

    private class RaffleDetails
    {
        public string Description { get; set; }
        public string? CoverFileId { get; set; }
    }

}