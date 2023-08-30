// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.18.1

using Azure.Search.Documents.Models;
using Backend.Model;
using Backend.Service;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Graph;

namespace Backend.Dialogs
{
    

    public class MainDialog : ComponentDialog
    {
        private readonly AIService _aiService;
        private readonly UserState _userState;
        private readonly BotState _conversationState;
        private readonly ILogger _logger;
        private readonly Templates _lgEngine;

        public MainDialog(
            AIService aiService,
            UserState userState,
            ConversationState conversationState,
            IConfiguration configuration,
            ILogger<MainDialog> logger)
        {
            _aiService=aiService;
            _userState = userState;
            _conversationState = conversationState;
            _logger = logger;

            AddDialog(new TextPrompt(nameof(TextPrompt)));

            var waterfallSteps = new WaterfallStep[]
            {
                    ProcessStepAsync,
            };
            var loopDialog =
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);

            _lgEngine = Templates.ParseFile("./Cards/Cards.lg");
        }


        private async Task<DialogTurnResult> ProcessStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userStateAccessors = _userState.CreateProperty<UserData>(nameof(UserData));
            var userData = await userStateAccessors.GetAsync(stepContext.Context, () => new UserData());

            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData());

            var question = string.Empty;

            //get current question
           question =stepContext.Context.Activity.Text;

            //generate answer using azure search and gpt
           (bool isnormal, var answer) = await GenerateAnswerAsync(question, conversationData.ConversationHistory);
            if (!string.IsNullOrEmpty(answer))
            {
                //send response to the user
                await SendTextResponseAsync(stepContext, answer, cancellationToken);
                if (isnormal) { await SaveToHistoryAsync(stepContext, question, answer); }
              //  await SaveToHistoryAsync(stepContext, question, answer);

            }
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private async Task SaveToHistoryAsync(WaterfallStepContext stepContext, string question, string answer)
        {
            var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData());

            //save user question and bot response to the conversation data.
            var pair = new ChatTurn()
            {
                User=question,
                Assistant=answer
            };
            conversationData.ConversationHistory.Add(pair);

            var count = _aiService.GetTokenCount(string.Join("\n", conversationData.ConversationHistory.Select(x => x.User+"\n"+x.Assistant)));
            while (count>2500)
            {
                conversationData.ConversationHistory.RemoveAt(0);
                count = _aiService.GetTokenCount(string.Join("\n", conversationData.ConversationHistory.Select(x => x.User+"\n"+x.Assistant)));
            }
        }



        private async Task<(bool,string)> GenerateAnswerAsync(string question, SynchronizedCollection<ChatTurn> history)
        {
            string answer = string.Empty;
            string internalEnglishQuestion = question;
            string internalChineseQuestion = question;

            //step1. get full context question usge gpt summary
            (bool isnormal, internalEnglishQuestion,internalChineseQuestion) = await _aiService.GetFullContextQuestionAsync(question, history);
            if (isnormal == false)
            {
                return (false,"Sorry, I cannot provide inforamtion on illegal and unethical questions!");
            }
            //step2. get refrence content from azure search
            //使用语义搜寻查询 + chatCompletion
            //string refContent = await _aiService.GetReferenceContentAsync(internalEnglishQuestion,QueryLanguage.EnUs);
            //if(string.IsNullOrEmpty(refContent))
            //{
            //    refContent = await _aiService.GetReferenceContentAsync(internalChineseQuestion, QueryLanguage.ZhCn);
            //}

            //使用embedding查询向量 + chatCompletion
            var results = _aiService.SearchVectorAsync(internalEnglishQuestion);
            var refContent = string.Join("\n", results.Result.Where(x => x.Score > 0.8).Select(x => x.Payload?["SourcePage"] + ":" + x.Payload?["Content"].Replace("\n", "").Replace("\r", "")).ToList());
            if (string.IsNullOrEmpty(refContent))
            {
                results = _aiService.SearchVectorAsync(internalChineseQuestion);
                refContent = string.Join("\n", results.Result.Where(x => x.Score > 0.8).Select(x => x.Payload?["SourcePage"] + ":" + x.Payload?["Content"].Replace("\n", "").Replace("\r", "")).ToList());
            }

            //step3. gerenater answer from chat gpt
           (bool isnormal_2, answer) = await _aiService.GetChatGPTAnswerAsync(question, history, refContent);
            
            _logger.LogInformation($"Bot:{answer}");

            //to escapt back slash and quota. otherwise generating adaptive card will fail.
            return (isnormal_2,answer.Replace("\\", "\\\\").Replace("\"", "\\\""));
        }



        private async Task<string> SendTextResponseAsync(WaterfallStepContext stepContext, string answer, CancellationToken cancellationToken)
        {
            var cardText = _lgEngine.Evaluate("TextResponseCard", new { text = answer });
            var answerActivity = ActivityFactory.FromObject(cardText);
            var response = await stepContext.Context.SendActivityAsync(answerActivity, cancellationToken);

            return response.Id;

        }
    }
}