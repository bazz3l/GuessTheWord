using System.Collections.Generic;
using System;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Guess The Word", "Bazz3l", "1.0.6")]
    [Description("Guess the scrambled word and receive a reward.")]
    class GuessTheWord : CovalencePlugin
    {
        [PluginReference] Plugin ServerRewards, Economics;

        #region Fields
        List<string> wordList = new List<string>();
        bool eventActive      = false;
        Timer eventRepeatTimer;
        Timer eventTimer;
        string currentScramble;
        string currentWord;
        #endregion

        #region Config
        PluginConfig config;

        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                APIEndpoint        = "https://raw.githubusercontent.com/instafluff/ComfyDictionary/master/wordlist.txt?raw=true",
                UseServerRewards   = true,
                UseEconomics       = false,
                ServerRewardPoints = 100,
                EconomicsPoints    = 100.0,
                MinWordLength      = 4,
                MaxWordLength      = 6,
                MaxWords           = 50,
                eventTime          = 3600f,
                eventLength        = 120f
            };
        }

        class PluginConfig
        {
            public string APIEndpoint;
            public bool UseServerRewards;
            public bool UseEconomics;
            public int ServerRewardPoints;
            public double EconomicsPoints;
            public int MinWordLength;
            public int MaxWordLength;
            public int MaxWords;
            public float eventTime;
            public float eventLength;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"Prefix", "[#DC143C]Guess The Word[/#]: "},
                {"InvalidSyntax", "invalid syntax, /word <answer>"},
                {"NotActive", "not active."},
                {"Invalid", "incorrect answer."},
                {"StartEvent", "guess the word, [#DC143C]{0}[/#]"},
                {"EventEnded", "no one guessed, [#DC143C]{0}[/#]"},
                {"EventAward", "you received [#DC143C]{0}[/#]"},
                {"EventWinner", "[#DC143C]{0}[/#] guessed the word, [#DC143C]{1}[/#]"}
            }, this);
        }

        void OnServerInitialized()
        {
            FetchWordList();

            eventRepeatTimer = timer.Repeat(config.eventTime, 0, () => StartEvent());
        }

        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }
        #endregion

        #region Core
        void StartEvent()
        {
            if (eventActive || wordList.Count == 0) return;

            eventActive     = true;
            currentWord     = wordList[Oxide.Core.Random.Range(0, wordList.Count)];
            currentScramble = ScrambleWord();

            MessageAll("StartEvent", currentScramble);

            eventTimer = timer.Once(config.eventLength, () => EventEnded());
        }

        void EventEnded()
        {
            ResetEvent();

            MessageAll("EventEnded", currentWord);
        }

        void ResetEvent()
        {
            eventActive = false;

            if(!eventRepeatTimer.Destroyed)
            {
                eventRepeatTimer.Destroy();
                eventRepeatTimer = timer.Repeat(config.eventTime, 0, () => StartEvent());
            }

            if(!eventTimer.Destroyed)
                eventTimer.Destroy();
        }

        void FetchWordList()
        {
            webrequest.EnqueueGet(config.APIEndpoint, (code, response) => {
                if (code != 200 || response == null) return;

                wordList = response.Split(',').ToList<string>()
                .Where(x => x.Length >= config.MinWordLength && x.Length <= config.MaxWordLength)
                .Take(config.MaxWords)
                .ToList();
            }, this);
        }

        string ScrambleWord()
        {
            List<char> wordChars = new List<char>(currentWord.ToCharArray());
            string scrambledWord = string.Empty;

            while(wordChars.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, wordChars.Count - 1);
                scrambledWord += wordChars[index];
                wordChars.RemoveAt(index);
            }

            if (currentWord == scrambledWord)
                return ScrambleWord();

            return scrambledWord;
        }

        bool CheckGuess(string currentGuess)
        {
            return string.Equals(currentGuess, currentWord, StringComparison.OrdinalIgnoreCase);
        }

        void RewardPlayer(IPlayer player)
        {
            string message = string.Empty;

            if (ServerRewards && config.UseServerRewards)
            {
                ServerRewards?.Call("AddPoints", player.Id, config.ServerRewardPoints);

                message = Lang("EventAward", player.Id, string.Format("{0}RP", config.ServerRewardPoints));
            }

            if (Economics && config.UseEconomics)
            {
                Economics?.Call("Deposit", player.Id, config.EconomicsPoints);

                message = Lang("EventAward", player.Id, string.Format("${0}", (int)config.EconomicsPoints + "RP"));
            }

            if (ServerRewards || Economics)
            {
                MessageAll("EventWinner", player.Name, currentWord);

                player.Message(message);             
            }
        }

        void MessageAll(string key, params object[] args)
        {
            foreach (IPlayer player in covalence.Players.Connected)
            {
                if (player == null) continue;

                player.Message(Lang("Prefix", player.Id) + Lang(key, player.Id, args));
            }
        }
        #endregion

        #region Commands
        [Command("word")]
        void WordCommand(IPlayer player, string command, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                player.Message(Lang("Prefix", player.Id) + Lang("InvalidSyntax", player.Id));
                return;
            }

            if (!eventActive)
            {
                player.Message(Lang("Prefix", player.Id) + Lang("NotActive", player.Id));
                return;
            }

            if (!CheckGuess(args[0]))
            {
                player.Message(Lang("Prefix", player.Id) + Lang("Invalid", player.Id));
                return;
            }

            RewardPlayer(player);
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        #endregion
    }
}