﻿
using Telegram.Bot;

namespace ESchoolBot
{
    public class ESchoolAccessor : IESchoolAccessor
    {
        private readonly IDatabaseClient databaseClient;
        private readonly IESchoolClient eschoolClient;
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<ESchoolAccessor> logger;

        public ESchoolAccessor(IDatabaseClient databaseClient,
                               IESchoolClient eschoolClient,
                               ITelegramBotClient botClient,
                               ILogger<ESchoolAccessor> logger)
        {
            this.databaseClient = databaseClient;
            this.eschoolClient = eschoolClient;
            this.botClient = botClient;
            this.logger = logger;
        }

        public async Task<DiaryPeriodResponse.DiaryPeriod[]> GetDiariesAsync(DatabaseClient.User user, CancellationToken cancellationToken)
        {
            StateResponse state = await InvokeESchoolClientAsync(
                user,
                async (sessionId, cancellationToken) =>
                {
                    return await eschoolClient.GetStateAsync(user.SessionId);
                },
                cancellationToken);

            GroupsResponse groups = await InvokeESchoolClientAsync(
                user,
                async (sessionId, cancellationToken) =>
                {
                    return await eschoolClient.GetGroupsAsync(user.SessionId, state.UserId);
                },
                cancellationToken);

            PeriodsResponse periods = await InvokeESchoolClientAsync(
                user,
                async (sessionId, cancellationtoken) =>
                {
                    return await eschoolClient.GetPeriodsAsync(user.SessionId, groups.Last().GroupId);
                },
                cancellationToken);

            int periodId = periods.Items.First().Id;

            DiaryPeriodResponse diariesResponse = await InvokeESchoolClientAsync(
                user,
                async (sessionId, cancellationToken) =>
                {
                    return await eschoolClient.GetDiaryPeriodAsync(user.SessionId, state.UserId, periodId);
                },
                cancellationToken);

            return diariesResponse.Result;
        }

        private delegate Task<T> InvokeESchoolClientAction<T>(string sessionId, CancellationToken cancellationToken);

        private async Task<T> InvokeESchoolClientAsync<T>(DatabaseClient.User user,
                                                          InvokeESchoolClientAction<T> action,
                                                          CancellationToken cancellationToken)
        {
            try
            {
                return await action(user.SessionId, cancellationToken);
            }
            catch (LoginException)
            {
                logger.LogInformation("Updating SessionId for user {chatId}", user.ChatId);

                string newToken = await eschoolClient.LoginAsync(user.Username, user.Password, cancellationToken);

                databaseClient.UpdateSessionId(user.ChatId, newToken);

                try
                {
                    return await action(newToken, cancellationToken);
                }
                catch (LoginException)
                {
                    await botClient.SendTextMessageAsync(user.ChatId,
                                                         Formatter.LoginRequired,
                                                         cancellationToken: cancellationToken);
                    throw;
                }
            }
        }
    }
}
