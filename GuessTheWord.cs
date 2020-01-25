using System.Collections.Generic;
using System.Linq;
using System;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("Guess The Word", "Bazz3l", "1.0.3")]
    [Description("Guess the scrambled word and receive a reward.")]
    class GuessTheWord : RustPlugin
    {
        [PluginReference]
        private Plugin ServerRewards, Economics;

        #region Fields
        private List<string> wordList = new List<string>();
        private bool eventActive      = false;
        private Timer eventRepeatTimer;
        private Timer eventTimer;
        private string currentScramble;
        private string currentWord;
        #endregion

        #region Config
        private PluginConfig config;

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                APIEndpoint        = "https://raw.githubusercontent.com/instafluff/ComfyDictionary/master/wordlist.txt?raw=true",
                UseServerRewards   = true,
                UseEconomics       = false,
                ServerRewardPoints = 100,
                EconomicsPoints    = 100.0,
                MinWordLength      = 3,
                MaxWordLength      = 6,
                MaxWords           = 50,
                eventTime          = 3600f,
                eventLength        = 120f
            };
        }

        private class PluginConfig
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
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"]      = "<color=#DC143C>Guess The Word</color>: ",
                ["Syntax"]      = "invalid syntax, /word <answer>",
                ["Active"]      = "not active.",
                ["Invalid"]     = "incorrect answer.",
                ["StartEvent"]  = "guess the word, <color=#DC143C>{0}</color>",
                ["EventEnded"]  = "no one guessed, <color=#DC143C>{0}</color>",
                ["EventWinner"] = "<color=#DC143C>{0}</color> guessed the word, <color=#DC143C>{1}</color>",
            }, this);
        }

        private void OnServerInitialized()
        {
            FetchWordList();

            eventRepeatTimer = timer.Repeat(config.eventTime, 0, () => StartEvent());
        }

        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }
        #endregion

        #region Core
        private void StartEvent()
        {
            if (eventActive || wordList.Count == 0) return;

            eventActive     = true;
            currentWord     = wordList[Oxide.Core.Random.Range(0, wordList.Count)];
            currentScramble = ScrambleWord();

            MessageAll("StartEvent", currentScramble);

            eventTimer = timer.Once(config.eventLength, () => EventEnded());
        }

        private void EventEnded()
        {
            ResetEvent();

            MessageAll("EventEnded", currentWord);
        }

        private void ResetEvent()
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

        private void FetchWordList()
        {
            webrequest.Enqueue(config.APIEndpoint, null, (code, response) => {
                if (code != 200 || response == null) return;

                wordList = response.Split(',').ToList<string>()
                .Where(x => x.Length >= config.MinWordLength && x.Length <= config.MaxWordLength)
                .Take(config.MaxWords)
                .ToList();

            }, this, RequestMethod.GET);
        }

        private string ScrambleWord()
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

        private bool CheckGuess(string currentGuess)
        {
            return string.Equals(currentGuess, currentWord, StringComparison.OrdinalIgnoreCase);
        }

        private void RewardPlayer(BasePlayer player)
        {
            if (config.UseServerRewards)
                ServerRewards?.Call("AddPoints", player.userID, config.ServerRewardPoints);

            if (config.UseEconomics)
                Economics?.Call("Deposit", player.userID, config.EconomicsPoints);

            ResetEvent();

            MessageAll("EventWinner", player.displayName, currentWord);
        }

        private void MessageAll(string key, params object[] args)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;

                player.ChatMessage(Lang("Prefix", player.UserIDString) + Lang(key, player.UserIDString, args));
            }
        }
        #endregion

        #region Commands
        [ChatCommand("word")]
        void WordCommand(BasePlayer player, string command, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                player.ChatMessage(Lang("Prefix", player.UserIDString) + Lang("Syntax", player.UserIDString));
                return;
            }

            if (!eventActive)
            {
                player.ChatMessage(Lang("Prefix", player.UserIDString) + Lang("Active", player.UserIDString));
                return;
            }

            if (!CheckGuess(args[0]))
            {
                player.ChatMessage(Lang("Prefix", player.UserIDString) + Lang("Invalid", player.UserIDString));
                return;
            }

            RewardPlayer(player);
        }
        #endregion

        #region Helpers
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        #endregion
    }
}