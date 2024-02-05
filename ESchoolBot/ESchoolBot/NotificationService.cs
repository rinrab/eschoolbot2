﻿using Telegram.Bot;

namespace ESchoolBot
{
    public class NotificationService : BackgroundService
    {
        private readonly IDatabaseClient databaseClient;
        private readonly ILogger logger;
        private readonly ITelegramBotClient botClient;
        private readonly IESchoolClient eschoolClient;
        private readonly int fetchDelay = 10;

        public NotificationService(IDatabaseClient databaseClient,
                                   ILogger<NotificationService> logger,
                                   ITelegramBotClient botClient,
                                   IESchoolClient eschoolClient)
        {
            this.databaseClient = databaseClient;
            this.logger = logger;
            this.botClient = botClient;
            this.eschoolClient = eschoolClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Notification service started on every {fetchDelay} seconds.", fetchDelay);

            while (!stoppingToken.IsCancellationRequested)
            {
                List<DatabaseClient.User> users = databaseClient.ListUsers();

                foreach (DatabaseClient.User user in users)
                {
                    try
                    {
                        await FetchUser(user, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error while processing fetch for user {userId}", user.ChatId);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(fetchDelay), stoppingToken);
            }
        }

        private async Task FetchUser(DatabaseClient.User user, CancellationToken stoppingToken)
        {
            DiaryPeriodResponse diariesResponse;

            try
            {
                diariesResponse = await eschoolClient.GetDiaryPeriodAsync(user.SessionId, user.UserId, user.PeriodId);
            }
            catch (LoginException)
            {
                try
                {
                    string sessionId = await eschoolClient.LoginAsync(user.Username, user.Password, stoppingToken);
                    databaseClient.UpdateSessionId(user.ChatId, sessionId);
                    diariesResponse = await eschoolClient.GetDiaryPeriodAsync(user.SessionId, user.UserId, user.PeriodId);
                }
                catch (LoginException)
                {
                    await botClient.SendTextMessageAsync(user.ChatId, Formatter.LoginRequired,
                                                         cancellationToken: stoppingToken);
                    // TODO: disable notifications
                    throw;
                }
            }

            DiaryPeriodResponse.DiaryPeriod[] diaries = diariesResponse.Result;

            if (user.ProcessedDiaries == -1)
            {
                databaseClient.UpdateProcessedDiaries(user.ChatId, diaries.Length);
            }
            else
            {
                for (int i = diaries.Length - 1, sentMessages = 0; i >= user.ProcessedDiaries; i--)
                {
                    logger.LogInformation("{test}", diaries[i]);
                    if (diaries[i].Subject != null)
                    {
                        await botClient.SendTextMessageAsync(user.ChatId, Formatter.FormatNewDiaryMessage(diaries[i]), cancellationToken: stoppingToken);
                        sentMessages++;
                    }

                    if (sentMessages == 3)
                    {
                        break;
                    }
                }
                databaseClient.UpdateProcessedDiaries(user.ChatId, diaries.Length);
            }
        }
    }
}
