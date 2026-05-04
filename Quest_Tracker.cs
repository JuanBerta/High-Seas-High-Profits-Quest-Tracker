using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.city.mayor;
using zip.lexy.tgame.ui.widget.trade;
using zip.lexy.tgame.ui.widget.trader.townhall;
using static MelonLoader.MelonLogger;
using lui.general.button;
using zip.lexy.tgame.ui.widget;

namespace Goods_Tracker
{
    public class GoodsTrackerClass : MelonMod
    {
        [HarmonyPatch(typeof(TownHallQuestListItem), "Initialize")]
        public static class QuestListItem_Patch
        {
            public static void Postfix(TownHallQuestListItem __instance, MayorQuest quest)
            {
                var btnField = AccessTools.Field(typeof(TownHallQuestListItem), "tradeBtn");
                LButton tradeBtn = (LButton)btnField?.GetValue(__instance);
                Transform existingTrackBtn = __instance.transform.Find("TrackQuestButton");

                if (tradeBtn != null && existingTrackBtn == null)
                {
                    LButton trackBtn = GameObject.Instantiate(tradeBtn, tradeBtn.transform.parent);
                    trackBtn.name = "TrackQuestButton";

                    RectTransform trackBtnRect = trackBtn.GetComponent<RectTransform>();
                    trackBtnRect.anchoredPosition = new Vector2(-82, 0);

                    TMP_Text trackBtnText = trackBtn.GetComponentInChildren<TMP_Text>();

                    // Set initial text based on whether it's already tracked
                    if (trackBtnText != null)
                        trackBtnText.text = QuestTrackerState.TrackedQuests.Contains(quest) ? "Untrack" : "Track";

                    trackBtn.OnClick.RemoveAllListeners();

                    for (int i = 0; i < trackBtn.OnClick.GetPersistentEventCount(); i++)
                    {
                        trackBtn.OnClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                    }

                    trackBtn.OnClick.AddListener(new UnityEngine.Events.UnityAction(() =>
                    {
                        var prop = typeof(zip.lexy.tgame.state.EntityBase).GetProperty("gameState",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var gameState = (zip.lexy.tgame.state.GameState)prop?.GetValue(__instance);

                        if (gameState != null && gameState.viewCity != null)
                        {
                            string currentCity = gameState.viewCity.name; // Capture current city name

                            QuestTrackerState.ToggleQuest(quest, currentCity);

                            var orchestration = GameObject.FindObjectOfType<OrchestrationUI>();
                            if (orchestration != null)
                            {
                                QuestTrackerState.UpdateLabel(gameState, orchestration);
                            }

                            if (trackBtnText != null)
                                trackBtnText.text = QuestTrackerState.IsTracked(quest) ? "Untrack" : "Track";
                        }
                    }));
                }
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

                if (completedQuest != null && completedQuest.achieved)
                {
                    // Remove from our tracker
                    QuestTrackerState.RemoveQuest(completedQuest);

                    // Update the HUD label
                    var orchestration = GameObject.FindObjectOfType<OrchestrationUI>();
                    if (orchestration != null)
                    {
                        // We use a null check for gameState here as UpdateLabel needs it
                        // Or you can modify UpdateLabel to find gameState if it's null
                        var prop = typeof(zip.lexy.tgame.state.EntityBase).GetProperty("gameState",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var gameState = (zip.lexy.tgame.state.GameState)prop?.GetValue(__instance);

                        QuestTrackerState.UpdateLabel(gameState, orchestration);
                    }
                }
            }
        }

        public static string GetResourceIdFromArgs(string args)
        {
            // Example args: "good:stone|amount:10"
            if (string.IsNullOrEmpty(args) || !args.Contains("good:")) return null;

            // Split by '|' then find the part starting with 'good:'
            string goodPart = args.Split('|').FirstOrDefault(p => p.StartsWith("good:"));
            if (goodPart != null)
            {
                return goodPart.Replace("good:", "");
            }
            return null;
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
            public static HashSet<MayorQuest> TrackedQuests = new HashSet<MayorQuest>();

            public static TextMeshProUGUI DisplayLabel;
            public static RectTransform DisplayLabelRect;
            public static string QuestName;

            public static void ToggleQuest(MayorQuest quest, string cityName)
            {
                if (!GroupedQuests.ContainsKey(cityName))
                {
                    GroupedQuests[cityName] = new List<MayorQuest>();
                }

                // Check if this specific quest is already tracked in this city
                if (GroupedQuests[cityName].Any(q => q.args == quest.args && q.type == quest.type))
                {
                    GroupedQuests[cityName].RemoveAll(q => q.args == quest.args && q.type == quest.type);
                    if (GroupedQuests[cityName].Count == 0) GroupedQuests.Remove(cityName);
                    MelonLogger.Msg($"[Goods Tracker] Untracked quest in {cityName}");
                }
                else
                {
                    GroupedQuests[cityName].Add(quest);
                    MelonLogger.Msg($"[Goods Tracker] Tracked quest in {cityName}");
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
                    var prop = typeof(zip.lexy.tgame.state.EntityBase).GetProperty("gameState",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    gameState = (GameState)prop?.GetValue(orchestration);
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

                string fullText = "<color=yellow>ACTIVE QUESTS:</color>\n";
                foreach (var entry in GroupedQuests)
                {
                    // Display City Name as a Header
                    fullText += $"<color=orange><b>[{entry.Key.ToUpper()}]</b></color>\n";
                    foreach (var q in entry.Value)
                    {
                        fullText += FormatQuestText(q.args) + "\n";
                    }
                }
                DisplayLabel.text = fullText;
            }

            public static string FormatQuestText(string args)
            {
                if (string.IsNullOrEmpty(args)) return "Unknown Quest";
                try
                {
                    var parts = args.Split('|').Select(p => p.Split(':')).ToDictionary(s => s[0], s => s[1]);
                    string good = parts.ContainsKey("good") ? parts["good"] : "items";
                    string amount = parts.ContainsKey("amount") ? parts["amount"] : "0";
                    good = char.ToUpper(good[0]) + good.Substring(1);
                    return $"• Gather {amount} {good}";
                }
                catch
                {
                    return $"• {args}";
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
            }
        }
    }
}