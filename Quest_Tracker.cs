using HarmonyLib;
using lui.general.button;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using zip.lexy.tgame.constants;
using zip.lexy.tgame.localization;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.city.mayor;
using zip.lexy.tgame.state.ship;
using zip.lexy.tgame.ui.mainmenu;
using zip.lexy.tgame.ui.widget;
using zip.lexy.tgame.ui.widget.ship;
using zip.lexy.tgame.ui.widget.trader.townhall;
using zip.lexy.tgame.util;
using static MelonLoader.MelonLogger;

namespace Goods_Tracker
{
    public class GoodsTrackerClass : MelonMod
    {
        [HarmonyPatch(typeof(TownHallQuestListItem), "Initialize")]
        public static class QuestListItem_Patch
        {
            public static void Postfix(TownHallQuestListItem __instance, MayorQuest quest)
            {
                TownHallQuestListItem townHallQuestListItem = UnityEngine.GameObject.FindAnyObjectByType<TownHallQuestListItem>();
                var btnField = GetBtnField();
                LButton tradeBtn = (LButton)btnField.GetValue(__instance);
                Transform existingTrackBtn = GetExistingTrackBtn(__instance);

                TradeAndTrackingButtonsExists(tradeBtn, existingTrackBtn, __instance, quest);
            }
        }

        // We patch UpdateContentOfInventoryItems with a Postfix instead of Prefix to ensure we are modifying the text AFTER the game has set it, not before.
        // This way we can avoid the issue of our green text being overwritten by the game's white text.
        [HarmonyPatch(typeof(ShipInventory), "UpdateContentOfInventoryItems")]
        public static class ShipInventory_Update_Patch
        {
            public static void Postfix(ShipInventory __instance, CargoHolder holder, Transform ___itemContainer)
            {
                GameState gameState = GetGameState();
                OrchestrationUI orchestration = GetOrchestration();

                ChangeShipInventoryTracking(holder, ___itemContainer);

                // FIX: Find ALL quest items in the UI and update their buttons
                var allQuestItems = GameObject.FindObjectsOfType<TownHallQuestListItem>();
                foreach (var item in allQuestItems)
                {
                    var trackBtn = GetTrackButton(item);
                    if (trackBtn != null)
                    {
                        var trackBtnText = GetTrackBtnText(trackBtn);
                        var questField = AccessTools.Field(typeof(TownHallQuestListItem), "quest");
                        MayorQuest quest = (MayorQuest)questField?.GetValue(item);

                        if (quest != null) SetTrackUntrackButtonText(trackBtnText, quest);
                    }
                }
                QuestTrackerState.UpdateLabel(gameState, orchestration);
            }
        }

        [HarmonyPatch(typeof(InventoryItem), "UpdateItem")]
        public static class InventoryItem_Update_Patch {
            public static void Postfix(InventoryItem __instance, string itemType, float quantity, string customLabel, string customValue)
            {
                var gameState = GetGameState();
                var orchestration = GetOrchestration();
                QuestTrackerState.UpdateLabel(gameState, orchestration);
            }
        }

        [HarmonyPatch(typeof(GameState), "LoadCities")]
        public static class OnLoadCities_Patch
        {
            public static void Postfix(GameState __instance, Dictionary<string, object> data)
            {
                QuestTrackerState.LoadQuests();

                QuestTrackerState.UpdateLabel(__instance, GetOrchestration());
            }
        }

        [HarmonyPatch(typeof(MayorQuestTradeConfirmationWindow), "OnTradeAccepted")]
        public static class QuestCompleted_Patch
        {
            public static void Postfix(MayorQuestTradeConfirmationWindow __instance)
            {
                // Use reflection to get the private 'quest' field from the window
                var questField = AccessTools.Field(typeof(MayorQuestTradeConfirmationWindow), "quest");
                MayorQuest completedQuest = (MayorQuest)questField?.GetValue(__instance);
                var orchestration = GetOrchestration();
                var gameState = GetGameState();

                if (completedQuest != null && completedQuest.achieved)
                {
                    // Remove from our tracker
                    QuestTrackerState.RemoveQuest(completedQuest); 
                }

                // Update the HUD label
                if (orchestration != null)
                {
                    QuestTrackerState.UpdateLabel(gameState, orchestration);
                }
            }
        }

        static OrchestrationUI GetOrchestration()
        {
            var orchestration = GameObject.FindObjectOfType<OrchestrationUI>();
            return orchestration;
        }

        static GameState GetGameState()
        {
            var mayorQuestTradeConfirmationWindow = UnityEngine.Object.FindObjectOfType<MayorQuestTradeConfirmationWindow>();
            var prop = typeof(zip.lexy.tgame.state.EntityBase).GetProperty("gameState",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var gameState = (zip.lexy.tgame.state.GameState)prop?.GetValue(mayorQuestTradeConfirmationWindow);
            return gameState;
        }

        /// --- HELPER METHODS ---
        /// Get the private 'tradeBtn' field from TownHallQuestListItem using Harmony's AccessTools
        static FieldInfo GetBtnField()
        {
            var tradeBtn = AccessTools.Field(typeof(TownHallQuestListItem), "tradeBtn");
            return tradeBtn;
        }

        // Get the actual LButton instance from the TownHallQuestListItem using the field info
        static LButton GetTradeBtn(TownHallQuestListItem __instance)
        {
            // Search only within this specific list item's children
            Transform btnTransform = __instance.transform.Find("btn-trade");
            if (btnTransform != null) return btnTransform.GetComponent<LButton>();

            // Fallback to reflection if the name is different
            var btnField = GetBtnField();
            return (LButton)btnField?.GetValue(__instance);
        }

        // Get the existing "TrackQuestButton" if it already exists to avoid duplicates when refreshing the quest list
        static Transform GetExistingTrackBtn(TownHallQuestListItem __instance)
        {
            Transform existingTrackBtn = __instance.transform.Find("quest-tracker");
            return existingTrackBtn;
        }

        // Check if the trade button exists and the track button doesn't exist before creating a new track button
        static bool TradeAndTrackingButtonsExists(LButton tradeBtn, Transform existingTrackBtn, TownHallQuestListItem __instance, MayorQuest quest)
        {
            if (tradeBtn != null && existingTrackBtn == null)
            {
                LButton trackBtn = GameObject.Instantiate(tradeBtn, tradeBtn.transform.parent);
                trackBtn.name = "quest-tracker";

                RectTransform trackBtnRect = trackBtn.GetComponent<RectTransform>();
                trackBtnRect.anchoredPosition = new Vector2(-82, 0);

                TMP_Text trackBtnText = trackBtn.GetComponentInChildren<TMP_Text>();

                if(QuestTrackerState.IsTracked(quest))
                {
                    SetTrackUntrackButtonText(trackBtnText, quest);
                }

                trackBtn.OnClick.RemoveAllListeners();

                for (int i = 0; i < trackBtn.OnClick.GetPersistentEventCount(); i++)
                {
                    trackBtn.OnClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                }

                TrackButtonAddListener(trackBtn, trackBtnText, __instance, quest);
                return true;
            }
            return false;
        }

        static void SetTrackUntrackButtonText(TMP_Text trackBtnText, MayorQuest quest)
        {
            if (trackBtnText == null) return;

            // Use the logic that checks GroupedQuests
            bool tracked = QuestTrackerState.IsTracked(quest);

            trackBtnText.text = tracked ? "Untrack" : "Track";
            trackBtnText.color = tracked ? Color.green : Color.white;
        }

        static LButton GetTrackButton(TownHallQuestListItem __instance)
        {
            // Look for our custom button inside this specific item
            Transform trackBtnObj = __instance.transform.Find("quest-tracker");
            return trackBtnObj?.GetComponent<LButton>();
        }

        // Add a click listener to the track button that toggles tracking for the quest and updates the button text accordingly
        static void TrackButtonAddListener(LButton trackBtn, TMP_Text trackBtnText, TownHallQuestListItem questListItem, MayorQuest quest)
        {
            trackBtn.OnClick.AddListener(new UnityEngine.Events.UnityAction(() =>
            {
                var gameState = GetGameState();

                if (gameState != null && gameState.viewCity != null)
                {
                    string currentCity = gameState.viewCity.name;
                    QuestTrackerState.ToggleQuest(quest, currentCity);

                    // REFRESH UI
                    var orchestration = GetOrchestration();
                    if (orchestration != null)
                    {
                        QuestTrackerState.UpdateLabel(gameState, orchestration);
                    }

                    // FIX: Explicitly update THIS specific button's text
                    SetTrackUntrackButtonText(trackBtnText, quest);
                }
            }));
        }

        static TMP_Text GetTrackBtnText(LButton trackBtn)
        {
            TMP_Text trackBtnText = trackBtn.GetComponentInChildren<TMP_Text>();
            return trackBtnText;
        }

        static MayorQuest GetMayorQuest()
        {
            var townHallQuestListItem = UnityEngine.Object.FindAnyObjectByType<TownHallQuestListItem>();
            if (townHallQuestListItem == null) return null;
            var questField = AccessTools.Field(typeof(TownHallQuestListItem), "quest");
            MayorQuest quest = (MayorQuest)questField?.GetValue(townHallQuestListItem);
            return quest;
        }

        static void ChangeShipInventoryTracking(CargoHolder holder, Transform itemContainer)
        {
            LoopInventoryItems(holder, itemContainer, GetList(holder));
        }

        static List<string> GetList(CargoHolder holder)
        {
            // 1. Get the sorted list of goods exactly as the game does it
            List<string> list = (from good in holder.GetGoods()
                                 select good.type into name
                                 orderby zip.lexy.tgame.localization.Localization.ForKey(name)
                                 select name).ToList();
            return list;
        }

        static void LoopInventoryItems(CargoHolder holder, Transform itemContainer, List<string> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (i >= itemContainer.childCount) break;

                string itemType = list[i];
                float amount = holder.GetGood(itemType).amount;

                var inventoryItem = itemContainer.GetChild(i).GetComponent<InventoryItem>();
                if (inventoryItem == null) continue;

                var itemTypeText = (TMP_Text)AccessTools.Field(typeof(InventoryItem), "itemTypeText").GetValue(inventoryItem);
                var quantityText = (TMP_Text)AccessTools.Field(typeof(InventoryItem), "quantityText").GetValue(inventoryItem);

                // Check if THIS specific resource is needed by any tracked quest
                bool isResourceNeeded = QuestTrackerState.GroupedQuests.Values
                    .Any(questList => questList.Any(q => q.args.Contains($"good:{itemType}")));

                ChangeTextColor(inventoryItem, itemType, amount, itemTypeText, quantityText, isResourceNeeded);
            }
        }

        static void ChangeTextColor(InventoryItem inventoryItem, string itemType, float amount, TMP_Text itemTypeText, TMP_Text quantityText, bool isTracked)
        {
            if (itemTypeText == null || quantityText == null) return;

            if (isTracked) // FIX: Only turn green IF tracked
            {
                itemTypeText.text = $"<color=green>{zip.lexy.tgame.localization.Localization.ForKey(itemType)}</color>";
                quantityText.text = $"<color=green>{zip.lexy.tgame.util.FU.OneDecimalBelowTen(amount)}</color>";
            }
            else
            {
                // If not tracked, we do nothing. 
                // Since we moved to Postfix, the game has already set the correct white text.
            }
        }

        static void SendMelonLoggerErrorMessage(string message)
        {
            MelonLogger.Error(message);
        }

        static void SendMelonLoggerInfoMessage(string message)
        {
            MelonLogger.Msg(message);
        }


        public static class QuestTrackerState
        {
            public static Dictionary<string, List<MayorQuest>> GroupedQuests = new Dictionary<string, List<MayorQuest>>();
            public static TextMeshProUGUI DisplayLabel;
            public static RectTransform DisplayLabelRect;
            public static string QuestName;
            private const string SAVE_KEY = "GoodsTracker_SavedQuests";

            public static void ToggleQuest(MayorQuest quest, string cityName)
            {
                GameState gameState = GetGameState();
                var townHallQuestListItem = UnityEngine.GameObject.FindAnyObjectByType<TownHallQuestListItem>();

                var tradeBtn = GetTradeBtn(townHallQuestListItem);
                var trackBtn = GetTrackButton(townHallQuestListItem);
                var trackBtnText = GetTrackBtnText(trackBtn);

                // Safety check: if no ship is selected, we can't update inventory colors anyway
                if (gameState == null || gameState.selectedShip == null) return;

                if (!GroupedQuests.ContainsKey(cityName))
                {
                    GroupedQuests[cityName] = new List<MayorQuest>();
                }

                if (GroupedQuests[cityName].Any(q => q.args == quest.args && q.type == quest.type))
                {
                    GroupedQuests[cityName].RemoveAll(q => q.args == quest.args && q.type == quest.type);
                    if (GroupedQuests[cityName].Count == 0) GroupedQuests.Remove(cityName);
                    SendMelonLoggerInfoMessage($"[Goods Tracker] Untracked quest in {cityName}: {quest.args}");
                }
                else
                {
                    // FIX: Create a NEW instance so we aren't tied to the UI's memory reference
                    MayorQuest questCopy = new MayorQuest
                    {
                        type = quest.type,
                        args = quest.args,
                        achieved = quest.achieved
                    };

                    GroupedQuests[cityName].Add(questCopy);
                    SendMelonLoggerInfoMessage($"[Goods Tracker] Tracked new quest in {cityName}: {questCopy.args}");
                }

                SaveQuests();

                // Refresh UI
                var shipInventoryUI = UnityEngine.Object.FindAnyObjectByType<zip.lexy.tgame.ui.widget.ship.ShipInventory>();
                if (shipInventoryUI != null)
                {
                    AccessTools.Method(typeof(zip.lexy.tgame.ui.widget.ship.ShipInventory), "RefreshUI").Invoke(shipInventoryUI, null);
                }
            }

            public static void SaveQuests()
            {
                HashSet<string> uniqueLines = new HashSet<string>();

                foreach (var entry in GroupedQuests)
                {
                    foreach (var quest in entry.Value)
                    {
                        // Format: City|Type|Args|Achieved
                        // Note: quest.args already contains pipes (good:x|amount:y), 
                        // so the final string will look like City|Type|good:x|amount:y|True
                        string line = $"{entry.Key}|{quest.type}|{quest.args}|{quest.achieved}";
                        uniqueLines.Add(line);
                    }
                }

                string data = string.Join(";", uniqueLines);
                PlayerPrefs.SetString(SAVE_KEY, data);
                PlayerPrefs.Save();
            }

            public static void LoadQuests()
            {
                string data = PlayerPrefs.GetString(SAVE_KEY, "");
                if (string.IsNullOrEmpty(data)) return;

                GroupedQuests.Clear();
                // Split the different quests
                string[] rows = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string row in rows)
                {
                    string[] parts = row.Split('|');
                    // We expect at least: City | Type | Args(at least one) | Achieved
                    if (parts.Length < 4) continue;

                    string cityName = parts[0];
                    string questType = parts[1];

                    // The 'achieved' bool is always the last element
                    string achievedStr = parts[parts.Length - 1];
                    bool isAchieved = achievedStr.ToLower() == "true";

                    // Everything between index 2 and the last index is part of 'args'
                    // This handles cases like "good:grain|amount:30" correctly
                    string originalArgs = string.Join("|", parts.Skip(2).Take(parts.Length - 3));

                    if (!GroupedQuests.ContainsKey(cityName))
                    {
                        GroupedQuests[cityName] = new List<MayorQuest>();
                    }

                    // Add the reconstructed quest
                    GroupedQuests[cityName].Add(new MayorQuest
                    {
                        type = questType,
                        args = originalArgs,
                        achieved = isAchieved
                    });

                    SendMelonLoggerInfoMessage($"[Goods Tracker] Loaded: {cityName} - {originalArgs} (Achieved: {isAchieved})");
                }
            }

            public static bool IsTracked(MayorQuest quest)
            {
                return GroupedQuests.Values.Any(list => list.Any(q => q.args == quest.args && q.type == quest.type));
            }

            public static void UpdateLabel(GameState gameState, OrchestrationUI orchestration)
            {
                // If gameState wasn't passed, try to find it via orchestration
                if (gameState == null)
                {
                    gameState = GetGameState();
                }
                GameObject labelObj = GameObject.Find("QuestTrackerLabel");
                if (labelObj == null)
                {
                    labelObj = new GameObject("QuestTrackerLabel");
                    labelObj.transform.SetParent(orchestration.transform, false);
                    DisplayLabel = labelObj.AddComponent<TextMeshProUGUI>();
                    DisplayLabel.fontSize = 24;
                    DisplayLabel.color = Color.white;
                    DisplayLabel.alignment = TextAlignmentOptions.TopLeft;

                    var uiField = AccessTools.Field(typeof(OrchestrationUI), "ui");
                    Transform hudContainer = (Transform)uiField?.GetValue(orchestration);

                    DisplayLabelRect = DisplayLabel.GetComponent<RectTransform>();
                    DisplayLabelRect.transform.SetParent(hudContainer != null ? hudContainer : orchestration.transform, false);

                    DisplayLabelRect.anchorMin = new Vector2(0, 1);
                    DisplayLabelRect.anchorMax = new Vector2(0, 1);
                    DisplayLabelRect.pivot = new Vector2(0, 1);
                    DisplayLabelRect.anchoredPosition = new Vector2(20, -140);
                    DisplayLabelRect.sizeDelta = new Vector2(400, 300);
                }

                if (GroupedQuests.Count == 0)
                {
                    DisplayLabel.text = "";
                    return;
                }

                string fullText = GroupedQuests.Count > 0 ? "<color=yellow>ACTIVE QUESTS:</color>\n" : "";
                // We get the city name from the key of GroupedQuests, and the quest args from the MayorQuest objects in the list
                foreach(var entry in GroupedQuests.OrderBy(e => e.Key))
                {
                    // Display City Name as a Header
                    fullText += $"<color=orange><b>[{entry.Key.ToUpper()}]</b></color>\n";
                    foreach (var q in entry.Value.OrderBy(q => q.args))
                    {
                        fullText += FormatQuestText(q.args) + "\n";
                        SendMelonLoggerInfoMessage($"Args: {q.args}");
                    }
                }
                DisplayLabel.text = fullText;
            }

            public static string FormatQuestText(string args)
            {
                if (string.IsNullOrEmpty(args)) return "Unknown Quest";
                try
                {
                    // Splits "good:grain|amount:30" into a dictionary
                    var parts = args.Split('|')
                                    .Select(p => p.Split(':'))
                                    .ToDictionary(s => s[0], s => s[1]);

                    string good = parts.ContainsKey("good") ? parts["good"] : "items";
                    string amount = parts.ContainsKey("amount") ? parts["amount"] : "0";

                    // Capitalize the first letter (e.g., grain -> Grain)
                    good = char.ToUpper(good[0]) + good.Substring(1);

                    return $"• Gather {amount} {good}";
                }
                catch
                {
                    // Fallback if the string format is unexpected
                    return $"• {args.Replace("good:", "").Replace("|", " ")}";
                }
            }

            public static void RemoveQuest(MayorQuest quest)
            {
                string cityToRemove = null;

                foreach (var entry in GroupedQuests)
                {
                    // Remove the quest if the args and type match
                    int removedCount = entry.Value.RemoveAll(q => q.args == quest.args && q.type == quest.type);
                    if (removedCount > 0)
                    {
                        MelonLogger.Msg($"[Goods Tracker] Auto-removed completed quest from {entry.Key}");
                        if (entry.Value.Count == 0) cityToRemove = entry.Key;
                        break;
                    }
                }

                if (cityToRemove != null) GroupedQuests.Remove(cityToRemove);

                SaveQuests();
            }
        }
    }
}