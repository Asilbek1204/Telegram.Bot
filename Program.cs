using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramExpenseBot.Data;
using TelegramExpenseBot.Models;

namespace ExpenseBot
{
    class Program
    {
        private static TelegramBotClient botClient;
        private static readonly CancellationTokenSource cts = new CancellationTokenSource();

        static async Task Main(string[] args)
        {
            try
            {
                // Ma'lumotlar bazasini yaratish
                using (var context = new ExpenseContext())
                {
                    context.Database.EnsureCreated();
                }

                // Bot tokenini o'rnating
                botClient = new TelegramBotClient("8298453169:AAFUUbs6zT7xOIu1ecIu-4vxcDwgO5X5jow");

                // Botni ishga tushirish
                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                botClient.StartReceiving(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions,
                    cts.Token);

                var me = await botClient.GetMeAsync();
                Console.WriteLine($"Bot {me.FirstName} ishga tushdi!");

                // Dasturni to'xtatmaslik uchun
                await Task.Delay(-1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Xato: {ex.Message}");
            }
            finally
            {
                cts.Cancel();
            }
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text)
                {
                    var message = update.Message;
                    var chatId = message.Chat.Id;
                    var messageText = message.Text;

                    Console.WriteLine($"Qabul qilindi: '{messageText}' chat {chatId}.");

                    // Komandalarni qayta ishlash
                    if (messageText.StartsWith("/start"))
                    {
                        await SendWelcomeMessage(chatId, cancellationToken);
                    }
                    else if (messageText.StartsWith("/add"))
                    {
                        await AddExpense(chatId, messageText, cancellationToken);
                    }
                    else if (messageText.StartsWith("/list"))
                    {
                        await ListExpenses(chatId, cancellationToken);
                    }
                    else if (messageText.StartsWith("/total"))
                    {
                        await ShowTotal(chatId, cancellationToken);
                    }
                    else if (messageText.StartsWith("/delete"))
                    {
                        await DeleteExpense(chatId, messageText, cancellationToken);
                    }
                    else if (messageText.StartsWith("/daily"))
                    {
                        await DailyReport(chatId, cancellationToken);
                    }
                    else if (messageText.StartsWith("/monthly"))
                    {
                        await MonthlyReport(chatId, messageText, cancellationToken);
                    }
                    else if (messageText.StartsWith("/help"))
                    {
                        await SendHelpMessage(chatId, cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Noto'g'ri buyruq. /help ni bosing yordam olish uchun.",
                            cancellationToken: cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Xatolik: {ex.Message}");
            }
        }

        public static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Xatosi:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }

        private static async Task SendWelcomeMessage(long chatId, CancellationToken cancellationToken)
        {
            string welcomeMessage =
                "Xush kelibsiz! Bu shaxsiy xarajatlarni hisoblovchi bot.\n\n" +
                "Mavjud buyruqlar:\n" +
                "/add [summa] [kategoriya] - xarajat qo'shish\n" +
                "/list - barcha xarajatlarni ko'rsatish\n" +
                "/total - umumiy xarajatlar summasini ko'rsatish\n" +
                "/delete [ID] - xarajatni o'chirish\n" +
                "/daily - bugungi kun uchun hisobot\n" +
                "/monthly [oy] - oylik hisobot (masalan: /monthly avgust)\n" +
                "/help - barcha buyruqlar haqida ma'lumot\n\n" +
                "Masalan: /add 15000 ovqat\n" +
                "Xarajat ID sini /list buyrug'i orqali ko'rishingiz mumkin.";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: welcomeMessage,
                cancellationToken: cancellationToken);
        }

        private static async Task SendHelpMessage(long chatId, CancellationToken cancellationToken)
        {
            string helpMessage =
                "📋 Barcha Buyruqlar:\n\n" +
                "➕ Xarajat qo'shish:\n" +
                "/add [summa] [kategoriya]\n" +
                "Masalan: /add 25000 transport\n\n" +
                "📄 Xarajatlarni ko'rish:\n" +
                "/list - oxirgi 10 ta xarajat\n\n" +
                "💰 Umumiy hisob:\n" +
                "/total - barcha xarajatlar yig'indisi\n\n" +
                "🗑️ Xarajat o'chirish:\n" +
                "/delete [ID] - ID ni /list orqali ko'ring\n\n" +
                "📊 Hisobotlar:\n" +
                "/daily - bugungi xarajatlar\n" +
                "/monthly [oy] - oylik hisobot\n" +
                "Masalan: /monthly avgust\n\n" +
                "❓ Yordam:\n" +
                "/help - bu xabarni ko'rsatish";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: helpMessage,
                cancellationToken: cancellationToken);
        }

        private static async Task AddExpense(long chatId, string messageText, CancellationToken cancellationToken)
        {
            try
            {
                var parts = messageText.Split(' ', 3);
                if (parts.Length < 3)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Noto'g'ri format. Iltimos: /add 1000 ovqat",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (!decimal.TryParse(parts[1], out decimal amount))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Summa noto'g'ri kiritilgan. Iltimos, raqam kiriting.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var category = parts[2];

                using (var context = new ExpenseContext())
                {
                    var expense = new Expense
                    {
                        UserId = chatId,
                        Amount = amount,
                        Category = category,
                        CreatedDate = DateTime.UtcNow,
                        Description = category
                    };

                    context.Expenses.Add(expense);
                    await context.SaveChangesAsync();
                }

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"✅ {amount} so'm '{category}' kategoriyasiga qo'shildi.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Xarajat qo'shishda xato: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Xarajat qo'shishda xato yuz berdi.",
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task ListExpenses(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                using (var context = new ExpenseContext())
                {
                    var expenses = await context.Expenses
                        .Where(e => e.UserId == chatId)
                        .OrderByDescending(e => e.CreatedDate)
                        .Take(10)
                        .ToListAsync();

                    if (!expenses.Any())
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Hozircha xarajatlar mavjud emas.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    var message = "📋 So'ngi 10 ta xarajat:\n\n";
                    foreach (var expense in expenses)
                    {
                        message += $"🆔 {expense.Id} | {expense.CreatedDate:dd.MM.yyyy} | {expense.Amount:N0} so'm | {expense.Category}\n";
                    }

                    message += "\n🗑️ Xarajatni o'chirish uchun: /delete [ID]";

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Xarajatlarni ko'rsatishda xato: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Xarajatlarni ko'rsatishda xato yuz berdi.",
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task ShowTotal(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                using var context = new ExpenseContext();

                // Avval xarajatlar mavjudligini tekshiramiz
                var hasExpenses = await context.Expenses
                    .Where(e => e.UserId == chatId)
                    .AnyAsync();

                if (!hasExpenses)
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Hozircha xarajatlar mavjud emas.",
                        cancellationToken: cancellationToken);
                    return;
                }

                // Barcha xarajatlarning yig'indisini hisoblash (xavsiz usul)
                var totalAmount = await context.Expenses
                    .Where(e => e.UserId == chatId)
                    .SumAsync(e => (decimal?)e.Amount) ?? 0;

                // Xarajatlar sonini sanash
                var expenseCount = await context.Expenses
                    .Where(e => e.UserId == chatId)
                    .CountAsync();

                // Javob tayyorlash
                var response = $"💰 Umumiy xarajat: {totalAmount:N0} so'm\n📊 Jami xarajatlar soni: {expenseCount}";

                await botClient.SendTextMessageAsync(chatId, response, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Jami summani hisoblashda xato: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                await botClient.SendTextMessageAsync(
                    chatId,
                    "❌ Hisobot olishda xatolik! Iltimos, keyinroq urunib ko'ring.",
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task DeleteExpense(long chatId, string messageText, CancellationToken cancellationToken)
        {
            try
            {
                var parts = messageText.Split(' ');
                if (parts.Length < 2 || !int.TryParse(parts[1], out int expenseId))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Noto'g'ri format. Iltimos: /delete 5\nXarajat ID sini /list buyrug'i orqali ko'rishingiz mumkin.",
                        cancellationToken: cancellationToken);
                    return;
                }

                using (var context = new ExpenseContext())
                {
                    var expense = await context.Expenses
                        .FirstOrDefaultAsync(e => e.Id == expenseId && e.UserId == chatId);

                    if (expense == null)
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Xarajat topilmadi yoki sizga tegishli emas.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    context.Expenses.Remove(expense);
                    await context.SaveChangesAsync();

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"✅ Xarajat o'chirildi: {expense.Amount} so'm - {expense.Category}",
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Xarajatni o'chirishda xato: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Xarajatni o'chirishda xato yuz berdi.",
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task DailyReport(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                using (var context = new ExpenseContext())
                {
                    var today = DateTime.Today;
                    var expenses = await context.Expenses
                        .Where(e => e.UserId == chatId && e.CreatedDate.Date == today)
                        .OrderByDescending(e => e.CreatedDate)
                        .ToListAsync();

                    if (!expenses.Any())
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Bugun hech qanday xarajat qilmagansiz.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    var total = expenses.Sum(e => e.Amount);
                    var message = $"📅 Bugungi hisobot ({today:dd.MM.yyyy}):\n\n";
                    message += $"💰 Jami xarajat: {total:N0} so'm\n";
                    message += $"📊 Xarajatlar soni: {expenses.Count} ta\n\n";

                    message += "📋 Xarajatlar ro'yxati:\n";
                    foreach (var expense in expenses)
                    {
                        message += $"• {expense.Amount:N0} so'm - {expense.Category} ({expense.CreatedDate:HH:mm})\n";
                    }

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kunlik hisobot olishda xato: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Kunlik hisobot olishda xato yuz berdi.",
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task MonthlyReport(long chatId, string messageText, CancellationToken cancellationToken)
        {
            try
            {
                var parts = messageText.Split(' ');
                int month = DateTime.Now.Month;
                int year = DateTime.Now.Year;

                if (parts.Length > 1)
                {
                    var monthNames = new Dictionary<string, int>
                    {
                        {"yanvar", 1}, {"fevral", 2}, {"mart", 3}, {"aprel", 4},
                        {"may", 5}, {"iyun", 6}, {"iyul", 7}, {"avgust", 8},
                        {"sentabr", 9}, {"oktabr", 10}, {"noyabr", 11}, {"dekabr", 12}
                    };

                    if (monthNames.ContainsKey(parts[1].ToLower()))
                    {
                        month = monthNames[parts[1].ToLower()];
                    }
                    else if (int.TryParse(parts[1], out int parsedMonth) && parsedMonth >= 1 && parsedMonth <= 12)
                    {
                        month = parsedMonth;
                    }
                }

                using (var context = new ExpenseContext())
                {
                    var startDate = new DateTime(year, month, 1);
                    var endDate = startDate.AddMonths(1).AddDays(-1);

                    var expenses = await context.Expenses
                        .Where(e => e.UserId == chatId && e.CreatedDate >= startDate && e.CreatedDate <= endDate)
                        .OrderByDescending(e => e.CreatedDate)
                        .ToListAsync();

                    if (!expenses.Any())
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"{GetMonthName(month)} oyida xarajatlar mavjud emas.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    var total = expenses.Sum(e => e.Amount);
                    var byCategory = expenses.GroupBy(e => e.Category)
                                            .Select(g => new { Category = g.Key, Total = g.Sum(e => e.Amount) })
                                            .OrderByDescending(x => x.Total);

                    var message = $"📊 {GetMonthName(month)} oyi hisoboti:\n\n";
                    message += $"💰 Jami xarajat: {total:N0} so'm\n";
                    message += $"📊 Xarajatlar soni: {expenses.Count} ta\n\n";

                    message += "🏷️ Kategoriyalar bo'yicha:\n";
                    foreach (var category in byCategory)
                    {
                        var percentage = (category.Total / total) * 100;
                        message += $"• {category.Category}: {category.Total:N0} so'm ({percentage:F1}%)\n";
                    }

                    message += $"\n📅 Davr: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Oylik hisobot olishda xato: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Oylik hisobot olishda xato yuz berdi.",
                    cancellationToken: cancellationToken);
            }
        }

        private static string GetMonthName(int month)
        {
            string[] monthNames = {
                "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
                "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr"
            };
            return monthNames[month - 1];
        }
    }
}