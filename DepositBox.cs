// DepositBox.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("DepositBox", "Orangemart", "1.0.0")]
    [Description("Drop box that registers drops for admin while removing items from the game.")]
    public class DepositBox : RustPlugin
    {
        private static DepositBox instance;
        private int DepositItemID;
        private ulong DepositBoxSkinID;
        private int MaxDepositLimit;

        private const string permPlace = "depositbox.place";
        private const string permAdminCheck = "depositbox.admincheck";

        private DepositLog depositLog;
        private Dictionary<Item, BasePlayer> depositTrack = new Dictionary<Item, BasePlayer>();

        void Init()
        {
            instance = this;
            LoadConfiguration();
            LoadDepositLog();
            permission.RegisterPermission(permPlace, this);
            permission.RegisterPermission(permAdminCheck, this);
        }

        [ConsoleCommand("depositsummary")]
        private void ConsoleDepositSummary(ConsoleSystem.Arg arg)
        {
            GenerateDepositSummary(null, 100000);
            Puts("✅ Deposit summary generated via console command.");
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file.");
            Config["DepositItemID"] = -1779183908;
            Config["DepositBoxSkinID"] = 1641384897;
            Config["MaxDepositLimit"] = 1000;
            SaveConfig();
        }

        private void LoadConfiguration()
        {
            DepositItemID = Convert.ToInt32(Config["DepositItemID"], CultureInfo.InvariantCulture);
            DepositBoxSkinID = Convert.ToUInt64(Config["DepositBoxSkinID"], CultureInfo.InvariantCulture);
            MaxDepositLimit = Convert.ToInt32(Config["MaxDepositLimit"], CultureInfo.InvariantCulture);
        }

        private void SaveConfiguration()
        {
            Config["DepositItemID"] = DepositItemID;
            Config["DepositBoxSkinID"] = DepositBoxSkinID;
            Config["MaxDepositLimit"] = MaxDepositLimit;
            SaveConfig();
        }

        void OnServerInitialized(bool initial)
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is StorageContainer container)
                    OnEntitySpawned(container);
            }
        }

        void Unload()
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is StorageContainer container && container.TryGetComponent(out DepositBoxRestriction restriction))
                    restriction.Destroy();
            }
            instance = null;
        }

        void OnEntitySpawned(StorageContainer container)
        {
            if (container == null || container.skinID != DepositBoxSkinID) return;
            if (!container.TryGetComponent(out DepositBoxRestriction mono))
            {
                mono = container.gameObject.AddComponent<DepositBoxRestriction>();
                mono.container = container.inventory;
                mono.InitDepositBox();
            }
        }

        [ChatCommand("depositbox")]
        private void GiveDepositBox(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permPlace))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            player.inventory.containerMain.GiveItem(ItemManager.CreateByItemID(833533164, 1, DepositBoxSkinID));
            player.ChatMessage(lang.GetMessage("BoxGiven", this, player.UserIDString));
        }

        [ChatCommand("depositsummary")]
        private void DepositSummaryCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permAdminCheck))
            {
                player.ChatMessage("You do not have permission to run this command.");
                return;
            }

            int prizePool = 100000;
            if (args.Length > 0 && int.TryParse(args[0], out int parsedAmount))
                prizePool = parsedAmount;

            GenerateDepositSummary(player, prizePool);
        }

        public object GenerateDepositSummary(BasePlayer player = null, int prizePool = 100000)
        {
            var playerTotals = new Dictionary<string, int>();
            int totalDeposits = 0;

            // Calculate totals on-demand from the log file
            foreach (var entry in depositLog.Deposits)
            {
                if (!playerTotals.ContainsKey(entry.SteamId))
                    playerTotals[entry.SteamId] = 0;

                playerTotals[entry.SteamId] += entry.AmountDeposited;
                totalDeposits += entry.AmountDeposited;
            }

            var summary = new Dictionary<string, object>();
            var claims = new Dictionary<string, int>();
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("steamid,total_deposited,percentage,sats_reward");

            foreach (var entry in playerTotals)
            {
                double percentage = totalDeposits > 0 ? (double)entry.Value / totalDeposits : 0;
                int reward = (int)Math.Round(percentage * prizePool);

                summary[entry.Key] = new
                {
                    total_deposited = entry.Value,
                    percentage = percentage * 100,
                    sats_reward = reward
                };

                claims[entry.Key] = reward;
                csvBuilder.AppendLine($"{entry.Key},{entry.Value},{(percentage * 100).ToString("F2", CultureInfo.InvariantCulture)},{reward}");
            }

            string dirPath = Interface.Oxide.DataDirectory + "/DepositBox";
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

            File.WriteAllText(Path.Combine(dirPath, "DepositBoxSummary.json"), JsonConvert.SerializeObject(summary, Formatting.Indented));
            File.WriteAllText(Path.Combine(dirPath, "DepositBoxClaims.json"), JsonConvert.SerializeObject(claims, Formatting.Indented));
            File.WriteAllText(Path.Combine(dirPath, "DepositBoxSummary.csv"), csvBuilder.ToString());

            player?.ChatMessage($"Deposit summary saved to data/DepositBox/ with prize pool: {prizePool} sats");
            return true;
        }

        public class DepositBoxRestriction : FacepunchBehaviour
        {
            public ItemContainer container;
            public void InitDepositBox()
            {
                container.canAcceptItem += CanAcceptItem;
                container.onItemAddedRemoved += OnItemAddedRemoved;
            }

            private bool CanAcceptItem(Item item, int targetPos)
            {
                if (item == null || item.info == null || item.info.itemid != DepositBox.instance.DepositItemID)
                    return false;

                var player = item.GetOwnerPlayer();
                if (player == null)
                    return false;

                int currentTotal = 0;
                string summaryPath = Path.Combine(Interface.Oxide.DataDirectory, "DepositBox/DepositBoxSummary.json");
                if (File.Exists(summaryPath))
                {
                    try
                    {
                        var summaryData = JsonConvert.DeserializeObject<Dictionary<string, Newtonsoft.Json.Linq.JObject>>(File.ReadAllText(summaryPath));
                        if (summaryData.TryGetValue(player.UserIDString, out var playerData))
                        {
                            currentTotal = playerData.Value<int>("total_deposited");
                        }
                    }
                    catch (Exception ex)
                    {
                        DepositBox.instance.Puts($"Warning: Could not read or parse DepositBoxSummary.json for limit check. Allowing deposit. Error: {ex.Message}");
                    }
                }

                // Check if the player's total from the last summary already exceeds the limit.
                // This is a simple check and does not account for deposits made after the last summary.
                if (currentTotal >= DepositBox.instance.MaxDepositLimit)
                {
                    player.ChatMessage($"Your total on the last leaderboard ({currentTotal} scrap) meets or exceeds the deposit limit of {DepositBox.instance.MaxDepositLimit}. You cannot deposit more until the next leaderboard update.");
                    player.GiveItem(item);
                    return false;
                }

                // The old check for `item.amount > maxAllowed` is removed as per the new, simplified logic.
                // We now only check if the player was already over the limit at the time of the last summary.
                // If the player is under the limit, we accept the deposit.
                DepositBox.instance.TrackDeposit(item, player);
                return true;
            }

            private void OnItemAddedRemoved(Item item, bool added)
            {
                if (!added || item.info.itemid != DepositBox.instance.DepositItemID) return;

                if (DepositBox.instance.depositTrack.TryGetValue(item, out BasePlayer player))
                {
                    DepositBox.instance.LogDeposit(player, item.amount);
                    DepositBox.instance.depositTrack.Remove(item);
                    item.Remove();
                }
            }

            public void Destroy()
            {
                container.canAcceptItem -= CanAcceptItem;
                container.onItemAddedRemoved -= OnItemAddedRemoved;
                UnityEngine.Object.Destroy(this);
            }
        }

        private class DepositLog
        {
            [JsonProperty("deposits")]
            public List<DepositEntry> Deposits { get; set; } = new List<DepositEntry>();
        }

        private class DepositEntry
        {
            [JsonProperty("steamid")]
            public string SteamId { get; set; }
            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
            [JsonProperty("amount_deposited")]
            public int AmountDeposited { get; set; }
        }

        public void LogDeposit(BasePlayer player, int amount)
        {
            depositLog.Deposits.Add(new DepositEntry
            {
                SteamId = player.UserIDString,
                Timestamp = DateTime.UtcNow.ToString("o"),
                AmountDeposited = amount
            });

            SaveDepositLog();

            player.ChatMessage(lang.GetMessage("DepositRecorded", this, player.UserIDString)
                .Replace("{amount}", amount.ToString(CultureInfo.InvariantCulture)));
        }

        public void TrackDeposit(Item item, BasePlayer player)
        {
            if (item != null && player != null)
                depositTrack[item] = player;
        }

        private void LoadDepositLog()
        {
            depositLog = Interface.Oxide.DataFileSystem.ReadObject<DepositLog>("DepositBoxLog") ?? new DepositLog();
        }

        private void SaveDepositLog()
        {
            Interface.Oxide.DataFileSystem.WriteObject("DepositBoxLog", depositLog);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"NoPermission", "You do not have permission to place this box."},
                {"BoxGiven", "You have received a Deposit Box."},
                {"DepositRecorded", "Your deposit of {amount} scrap has been recorded successfully."},
                {"PlacedNoPerm", "You have placed a deposit box but lack permission to place it."}
            }, this);
        }
    }
}
