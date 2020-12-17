using System.Collections.Generic;
using System.Linq;
using System;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core;
using WebSocketSharp;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Guess The Word", "Bazz3l", "1.1.0")]
    [Description("Guess a random scrambled word and receive a reward.")]
    public class GuessTheWord : CovalencePlugin
    {
        [PluginReference] Plugin ServerRewards, Economics;

        #region Fields

        private const string PermUse = "guesstheword.use";
        
        private List<string> _wordList = new List<string>();
        private string _currentScramble;
        private string _currentWord;
        private Timer _currentTimer;
        private Timer _repeater;

        private ConfigData _config;
        private StoredData _stored;
        
        #endregion

        #region Config
        
        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<ConfigData>();

                if (_config == null)
                {
                    throw new JsonException();
                }
            }
            catch
            {
                LoadDefaultConfig();
                
                PrintWarning("Loaded default config");
            }
        }
        
        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private ConfigData GetDefaultConfig()
        {
            return new ConfigData
            {
                APIEndpoint = "https://raw.githubusercontent.com/instafluff/ComfyDictionary/master/wordlist.txt?raw=true",
                UseServerRewards = false,
                UseAwardItems = true,
                UseEconomics = false,
                ServerRewardPoints = 100,
                EconomicsPoints = 100.0,
                MinWordLength = 4,
                MaxWordLength = 6,
                MaxWords = 50,
                EventTime = 60f,
                EventLength = 120f,
                AwardItemsMax = 2,
                AwardItems = new List<AwardItem> {
                    new AwardItem {
                        Name = "stones",
                        Amount = 10000
                    },
                    new AwardItem {
                        Name = "wood",
                        Amount = 10000
                    },
                    new AwardItem {
                        Name = "sulfur",
                        Amount = 5000
                    },
                    new AwardItem {
                        Name = "metal.fragments",
                        Amount = 10000
                    },
                    new AwardItem {
                        Name = "metal.refined",
                        Amount = 100
                    },
                }
            };
        }

        private class ConfigData
        {
            public string APIEndpoint;
            public bool UseServerRewards;
            public bool UseAwardItems;
            public bool UseEconomics;
            public int ServerRewardPoints;
            public double EconomicsPoints;
            public int MinWordLength;
            public int MaxWordLength;
            public int MaxWords;
            public float EventTime;
            public float EventLength;
            public int AwardItemsMax = 2;
            public List<AwardItem> AwardItems;
        }

        private class AwardItem
        {
            public string Name;
            public int Amount;

            public override string ToString()
            {
                return $"{Name}: {Amount}";
            }
        }
        
        #endregion

        #region Storage

        private void LoadDefaultData() => _stored = new StoredData();

        private void LoadData()
        {
            try
            {
                _stored = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

                if (_stored == null)
                {
                    throw new JsonException();
                }
            }
            catch
            {
                LoadDefaultData();
                
                PrintWarning("Loaded default data.");
            }
        }
        
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _stored);

        private void ClearData()
        {
            _stored.Players.Clear();
            
            SaveData();
        }

        private class StoredData
        {
            public Dictionary<string, RewardData> Players = new Dictionary<string, RewardData>();
        }

        private class RewardData
        {
            public int Rewards;

            public bool HasRewards() => Rewards > 0;
        }

        private RewardData FindRewardData(string userID)
        {
            EnsureKey(_stored.Players, userID, new RewardData());

            return _stored.Players[userID];
        }
        
        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                {"Prefix", "[#dc143c]Guess The Word[/#]: {0}"},
                {"InvalidSyntax", "Invalid syntax, [#ffc55c]/word[/#] <answer>"},
                {"EventStart", "Can you guess the word [#ffc55c]{0}[/#]"},
                {"EventEnded", "No one guessed [#ffc55c]{0}[/#]"},
                {"EventClaim", "You won type [#ffc55c]/claim[/#] to receive your reward."},
                {"EventAward", "You received [#ffc55c]{0}[/#]"},
                {"EventPoints", "{0} RP"},
                {"EventWinner", "[#ffc55c]{0}[/#] guessed the word [#ffc55c]{1}[/#]"},
                {"NotActive", "Sorry, No event currently running."},
                {"NoSlots", "Sorry, Not enough inventory space."},
                {"Invalid", "Sorry, Incorrect answer."}
            }, this);
        }

        #endregion

        #region Oxide

        private void OnServerInitialized()
        {
            AddCovalenceCommand("word", nameof(WordCommand), PermUse);

        #if RUST
            AddCovalenceCommand("claim", nameof(ClaimCommand), PermUse);            
        #endif

            webrequest.Enqueue(_config.APIEndpoint, null, SetupCallback, this);
        }

        private void Init() => LoadData();

        #endregion

        #region Core
        
        private void StartEvent()
        {
            if (!string.IsNullOrEmpty(_currentScramble) || _wordList.Count == 0)
            {
                return;
            }

            _currentWord = _wordList.GetRandom();

            _currentScramble = ScrambleCurrentWord();

            _currentTimer = timer.Once(_config.EventLength, EventEnded);

            Puts("{0}", _currentWord);

            BroadcastAll("EventStart", _currentScramble);
        }

        private void EventEnded()
        {
            ResetEvent();

            BroadcastAll("EventEnded", _currentWord);
        }

        private void ResetEvent()
        {
            _currentScramble = null;
            
            _repeater?.Destroy();
            _repeater = timer.Every(_config.EventTime, StartEvent);
            
            _currentTimer?.Destroy();
        }

        private void SetupCallback(int code, string response)
        {
            if (code != 200 || response.IsNullOrEmpty())
            {
                PrintWarning("Failed to fetch word list.");
                return;
            }
            
            _wordList = response.Split(',').ToList()
                .Where(x => x.Length >= _config.MinWordLength && x.Length <= _config.MaxWordLength)
                .Take(_config.MaxWords)
                .ToList();
            
            _repeater = timer.Every(_config.EventTime, StartEvent);
        }

        private string ScrambleCurrentWord()
        {
            List<char> wordChars = new List<char>(_currentWord.ToCharArray());

            string scrambledWord = string.Empty;

            while (wordChars.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, wordChars.Count - 1);

                scrambledWord += wordChars[index];

                wordChars.RemoveAt(index);
            }

            return _currentWord == scrambledWord ? ScrambleCurrentWord() : scrambledWord;
        }
        
        private bool CheckGuess(string currentGuess) => string.Equals(currentGuess, _currentWord, StringComparison.OrdinalIgnoreCase);

        private void RewardPlayer(IPlayer player)
        {
            if (_config.UseServerRewards && ServerRewards)
            {
                ServerRewards?.Call("AddPoints", player.Id, _config.ServerRewardPoints);

                player.Message(Lang("EventAward", player.Id, Lang("EventPoints", player.Id, _config.ServerRewardPoints)));
            }

            if (_config.UseEconomics && Economics)
            {
                Economics?.Call("Deposit", player.Id, _config.EconomicsPoints);

                player.Message(Lang("EventAward", player.Id, Lang("EventPoints", player.Id, _config.EconomicsPoints)));
            }

            if (_config.UseAwardItems)
            {
                FindRewardData(player.Id).Rewards++;
                
                SaveData();

                player.Message(Lang("EventClaim", player.Id));
            }

            BroadcastAll("EventWinner", player.Name, _currentWord);
            
            ResetEvent();
        }

        private List<AwardItem> AwardItems()
        {
            List<AwardItem> awardItems = new List<AwardItem>();

            int maxTries = 50;

            do
            {
                AwardItem awardItem = _config.AwardItems.GetRandom();
                
                if (!awardItems.Contains(awardItem))
                {
                    awardItems.Add(awardItem);
                }
                
            } while (awardItems.Count < _config.AwardItemsMax && maxTries-- > 0);

            return awardItems;
        }

        #endregion

        #region Commands
        
        private void WordCommand(IPlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(_currentScramble))
            {
                player.Message(Lang("NotActive", player.Id));
                return;
            }

            if (args.Length < 1)
            {
                player.Message(Lang("EventStart", player.Id, _currentScramble));
                return;
            }

            if (!CheckGuess(args[0]))
            {
                player.Message(Lang("Invalid", player.Id));
                return;
            }

            RewardPlayer(player);
        }
        
        private void ClaimCommand(IPlayer player, string command, string[] args)
        {
            BasePlayer bPlayer = player.Object as BasePlayer;
            
            if (bPlayer == null)
            {
                return;
            }

            RewardData rewardData = FindRewardData(player.Id);
            
            if (!rewardData.HasRewards())
            {
                player.Message(Lang("NoReward", player.Id));
                return;
            }
            
            if (!FreeSlots(bPlayer, _config.AwardItemsMax))
            {
                player.Message(Lang("NoSlots", player.Id));
                return;
            }

            List<AwardItem> awardItems = AwardItems();
            
            foreach (AwardItem aItem in awardItems)
            {
                Item item = ItemManager.CreateByName(aItem.Name, aItem.Amount);
                
                bPlayer.GiveItem(item);
            }
    
            rewardData.Rewards--;
    
            SaveData();

            player.Message(Lang("EventAward", player.Id, string.Join("\n", awardItems.Select(x => x.ToString()).ToArray())));
        }

        #endregion

        #region Helpers
        
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private bool FreeSlots(BasePlayer player, int slots)
        {
            if (player == null || player.inventory == null)
            {
                return false;
            }
            
            return player.inventory.containerMain.capacity - player.inventory.containerMain.itemList.Count >= slots;
        }
        
        private void EnsureKey<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key, TValue value = default(TValue))
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
            }
        }
        
        private void BroadcastAll(string key, params object[] args) => server.Broadcast(Lang("Prefix", null, Lang(key, null, args)));

        #endregion
    }
}