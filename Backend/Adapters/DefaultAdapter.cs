﻿using Bot.Builder.Community.Cards;
using Bot.Builder.Community.Cards.Management;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;

namespace Backend.Adapters
{
    public class DefaultAdapter : CloudAdapter
    {
        public DefaultAdapter(
            BotFrameworkAuthentication auth,
            ILogger<IBotFrameworkHttpAdapter> logger,
            TelemetryInitializerMiddleware telemetryInitializerMiddleware,
            CardManager cardManager,
            ConversationState? conversationState = default)
            : base(auth, logger)
        {
            Use(telemetryInitializerMiddleware);
            Use(new ShowTypingMiddleware());

            var cardManagerMiddleware = new CardManagerMiddleware(cardManager);
            cardManagerMiddleware.UpdatingOptions.IdTrackingStyle=TrackingStyle.TrackEnabled;
           
            Use(cardManagerMiddleware
                .SetAutoApplyIds(false)
                .SetAutoConvertAdaptiveCards(false)
                .SetIdOptions(new DataIdOptions(new[]
                {
                    DataIdScopes.Card
                })));

            OnTurnError = async (turnContext, exception) =>
            {
                // Log any leaked exception from the application.
                logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

                // Send a message to the user
                var errorMessageText = "The bot encountered an error or bug.";
                var errorMessage = MessageFactory.Text(errorMessageText, errorMessageText, InputHints.ExpectingInput);
                await turnContext.SendActivityAsync(errorMessage);

                errorMessage = MessageFactory.Text(exception.Message, exception.Message, InputHints.ExpectingInput);
                await turnContext.SendActivityAsync(errorMessage);

                errorMessage = MessageFactory.Text(exception.StackTrace, exception.StackTrace, InputHints.ExpectingInput);
                await turnContext.SendActivityAsync(errorMessage);

                errorMessageText = "To continue to run this bot, please fix the bot source code.";
                errorMessage = MessageFactory.Text(errorMessageText, errorMessageText, InputHints.ExpectingInput);
                await turnContext.SendActivityAsync(errorMessage);

                if (conversationState != null)
                {
                    try
                    {
                        // Delete the conversationState for the current conversation to prevent the
                        // bot from getting stuck in a error-loop caused by being in a bad state.
                        // _conversationState should be thought of as similar to "cookie-state" in a Web pages.
                        await conversationState.DeleteAsync(turnContext);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"Exception caught on attempting to Delete ConversationState : {e.Message}");
                    }
                }

                // Send a trace activity, which will be displayed in the Bot Framework Emulator
                await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError");
            };
        }
    }
}