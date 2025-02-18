﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.World;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterQuests
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<uint> _removed;

    private Character Owner { get; set; }
    public Dictionary<uint, Quest> ActiveQuests { get; }
    private Dictionary<ushort, CompletedQuest> CompletedQuests { get; }

    public CharacterQuests(Character owner)
    {
        Owner = owner;
        ActiveQuests = new Dictionary<uint, Quest>();
        CompletedQuests = new Dictionary<ushort, CompletedQuest>();
        _removed = new List<uint>();
    }

    public bool HasQuest(uint questId)
    {
        return ActiveQuests.ContainsKey(questId);
    }

    public bool HasQuestCompleted(uint questId)
    {
        var questBlockId = (ushort)(questId / 64);
        var questBlockIndex = (int)(questId % 64);
        return CompletedQuests.TryGetValue(questBlockId, out var questBlock) && questBlock.Body.Get(questBlockIndex);
    }

    public bool Add(uint questId, bool forcibly = false, uint npcObjId = 0, uint doodadObjId = 0, uint sphereId = 0)
    {
        if (ActiveQuests.ContainsKey(questId))
        {
            if (forcibly)
            {
                Logger.Info("[GM] quest {0}, added!", questId);
                Drop(questId, true);
            }
            else
            {
                Logger.Info("Duplicate quest {0}, not added!", questId);
                return false;
            }
        }

        var template = QuestManager.Instance.GetTemplate(questId);
        if (template == null) { return false; }

        if (HasQuestCompleted(questId))
        {
            if (forcibly)
            {
                Logger.Info("[GM] quest {0}, added!", questId);
                Drop(questId, true);
            }
            else if (template.Repeatable == false)
            {
                Logger.Warn($"Quest {questId} already completed for {Owner.Name}, not added!");
                Owner.SendErrorMessage(ErrorMessageType.QuestDailyLimit);
                return false;
            }
        }

        var quest = new Quest(template);
        quest.Id = QuestIdManager.Instance.GetNextId();
        quest.Status = QuestStatus.Invalid;
        quest.Condition = QuestConditionObj.Progress;
        quest.Owner = Owner;

        if (npcObjId > 0)
        {
            quest.Owner.CurrentTarget = WorldManager.Instance.GetUnit(npcObjId);
        }
        else if (doodadObjId > 0)
        {
            // TODO
        }
        else if (sphereId > 0)
        {
            // TODO
        }

        if (QuestManager.Instance.QuestTimeoutTask.Count != 0)
        {
            if (QuestManager.Instance.QuestTimeoutTask.ContainsKey(quest.Owner.Id) && QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].ContainsKey(questId))
            {
                QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].Remove(questId);
            }
        }

        // TODO new quests
        quest.CreateContextInstance();     // установим начальный контекст
        var res = quest.StartQuest(forcibly); // начало квеста
        //var res = quest.Start();
        if (!res)
        {
            Drop(questId, true);
            return false;
        }

        ActiveQuests.Add(quest.TemplateId, quest);
        quest.Owner.SendMessage("[Quest] {0}, quest {1} added.", Owner.Name, questId);
        //quest.ContextProcessing();
        quest.GoToNextStep();

        return true;
    }

    /// <summary>
    /// Complete - завершаем квест, получаем награду
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="selected"></param>
    /// <param name="supply"></param>
    public void Complete(uint questId, int selected, bool supply = true)
    {
        if (!ActiveQuests.ContainsKey(questId))
        {
            Logger.Warn($"Complete, quest does not exist {questId}");
            return;
        }

        var quest = ActiveQuests[questId];
        quest.QuestRewardItemsPool.Clear();
        quest.QuestRewardCoinsPool = 0;
        quest.QuestRewardExpPool = 0;
        var res = quest.Complete(selected);
        if (res != 0)
        {
            if (supply)
            {
                var levelBasedRewards = QuestManager.Instance.GetSupplies(quest.Template.Level);
                if (levelBasedRewards != null)
                {
                    if (quest.Template.LetItDone)
                    {
                        // Добавим|убавим за перевыполнение|недовыполнение плана, если позволено квестом
                        // Add [reduce] for overfulfilling [underperformance] of the plan, if allowed by the quest
                        // TODO: Verify if the bonus only applies to the level-based XP/Gold, or if it also applies to the rewards parts in quest_act_supply_xxx
                        quest.QuestRewardExpPool += levelBasedRewards.Exp * quest.OverCompletionPercent / 100;
                        quest.QuestRewardCoinsPool += levelBasedRewards.Copper * quest.OverCompletionPercent / 100;

                        if (!quest.ExtraCompletion)
                        {
                            // посылаем пакет, так как он был пропущен в методе Update()
                            // send a packet because it was skipped in the Update() method
                            quest.Status = QuestStatus.Progress;
                            // пакет не нужен
                            //Owner.SendPacket(new SCQuestContextUpdatedPacket(quest, quest.ComponentId));
                            quest.Status = QuestStatus.Completed;
                        }
                    }
                    else
                    {
                        quest.QuestRewardExpPool += levelBasedRewards.Exp;
                        quest.QuestRewardCoinsPool += levelBasedRewards.Copper;
                    }
                }
            }
            quest.DistributeRewards();

            var completeId = (ushort)(quest.TemplateId / 64);
            if (!CompletedQuests.ContainsKey(completeId))
                CompletedQuests.Add(completeId, new CompletedQuest(completeId));
            var complete = CompletedQuests[completeId];
            complete.Body.Set((int)(quest.TemplateId % 64), true);
            var body = new byte[8];
            complete.Body.CopyTo(body, 0);
            Drop(questId, false);
            //OnQuestComplete(questId);
            Owner.SendPacket(new SCQuestContextCompletedPacket(quest.TemplateId, body, res));
        }
    }

    public void Drop(uint questId, bool update, bool forcibly = false)
    {
        if (!ActiveQuests.ContainsKey(questId)) { return; }

        var quest = ActiveQuests[questId];
        quest.Drop(update);
        ActiveQuests.Remove(questId);
        _removed.Add(questId);

        if (forcibly) { ResetCompletedQuest(questId); }

        quest.Owner.SendMessage("[Quest] for player: {0}, quest: {1} removed.", Owner.Name, questId);
        Logger.Warn("[Quest] for player: {0}, quest: {1} removed.", Owner.Name, questId);

        if (QuestManager.Instance.QuestTimeoutTask.ContainsKey(quest.Owner.Id))
        {
            if (QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].ContainsKey(questId))
            {
                _ = QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id][questId].CancelAsync();
                _ = QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].Remove(questId);
            }
        }

        QuestIdManager.Instance.ReleaseId((uint)quest.Id);
    }

    public bool SetStep(uint questContextId, uint step)
    {
        if (step > 8)
            return false;

        if (!ActiveQuests.ContainsKey(questContextId))
            return false;

        var quest = ActiveQuests[questContextId];
        quest.Step = (QuestComponentKind)step;
        return true;
    }

    public void OnReportToNpc(uint objId, uint questId, int selected)
    {
        if (!ActiveQuests.ContainsKey(questId))
            return;

        var quest = ActiveQuests[questId];

        var npc = WorldManager.Instance.GetNpc(objId);
        if (npc == null)
            return;

        //if (npc.GetDistanceTo(Owner) > 8.0f)
        //    return;

        quest.OnReportToNpc(npc, selected);
    }

    public void OnReportToDoodad(uint objId, uint questId, int selected)
    {
        if (!ActiveQuests.ContainsKey(questId))
            return;

        var quest = ActiveQuests[questId];

        var doodad = WorldManager.Instance.GetDoodad(objId);
        if (doodad == null)
            return;

        // if (npc.GetDistanceTo(Owner) > 8.0f)
        //     return;

        quest.OnReportToDoodad(doodad);
    }

    public void OnTalkMade(uint npcObjId, uint questContextId, uint questComponentId, uint questActId)
    {
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return;

        if (npc.GetDistanceTo(Owner) > 8.0f)
            return;

        if (!ActiveQuests.ContainsKey(questContextId))
            return;

        var quest = ActiveQuests[questContextId];

        quest.OnTalkMade(npc);
    }

    public void OnKill(Npc npc)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnKill(npc);
    }

    public void OnAggro(Npc npc)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnAggro(npc);
    }

    public void OnItemGather(Item item, int count)
    {
        //if (!Quests.ContainsKey(item.Template.LootQuestId))
        //    return;
        //var quest = Quests[item.Template.LootQuestId];
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnItemGather(item, count);
    }

    /// <summary>
    /// Использование предмета в инвентаре (Use of an item from your inventory)
    /// </summary>
    /// <param name="item"></param>
    public void OnItemUse(Item item)
    {
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnItemUse(item);
    }

    /// <summary>
    /// Взаимодействие с doodad, например ломаем шахту по квесту (Interaction with doodad, for example, breaking a mine on a quest)
    /// </summary>
    /// <param name="type"></param>
    /// <param name="target"></param>
    public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnInteraction(type, target);
    }

    public void OnExpressFire(uint emotionId, uint objId, uint obj2Id)
    {
        var npc = WorldManager.Instance.GetNpc(obj2Id);
        if (npc == null)
            return;

        //if (npc.GetDistanceTo(Owner) > 8.0f)
        //    return;

        foreach (var quest in ActiveQuests.Values)
            quest.OnExpressFire(npc, emotionId);
    }

    public void OnLevelUp()
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnLevelUp();
    }

    public void OnQuestComplete(uint questId)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnQuestComplete(questId);
    }

    public void OnEnterSphere(SphereQuest sphereQuest)
    {
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnEnterSphere(sphereQuest);
    }

    public void OnCraft(Craft craft)
    {
        // TODO added for quest Id=6024
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnCraft(craft);
    }

    public void AddCompletedQuest(CompletedQuest quest)
    {
        CompletedQuests.Add(quest.Id, quest);
    }

    public void ResetCompletedQuest(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        var quest = GetCompletedQuest(completeId);

        if (quest == null) { return; }

        quest.Body.Set((int)questId - completeId * 64, false);
        CompletedQuests[completeId] = quest;
    }

    public CompletedQuest GetCompletedQuest(ushort id)
    {
        return CompletedQuests.TryGetValue(id, out var quest) ? quest : null;
    }

    public bool IsQuestComplete(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        if (!CompletedQuests.ContainsKey(completeId))
            return false;
        return CompletedQuests[completeId].Body[(int)(questId - completeId * 64)];
    }

    public void Send()
    {
        var quests = ActiveQuests.Values.ToArray();
        if (quests.Length <= 20)
        {
            Owner.SendPacket(new SCQuestsPacket(quests));
            return;
        }

        for (var i = 0; i < quests.Length; i += 20)
        {
            var size = quests.Length - i >= 20 ? 20 : quests.Length - i;
            var res = new Quest[size];
            Array.Copy(quests, i, res, 0, size);
            Owner.SendPacket(new SCQuestsPacket(res));
        }
    }

    public void SendCompleted()
    {
        var completedQuests = CompletedQuests.Values.ToArray();
        if (completedQuests.Length <= 200)
        {
            Owner.SendPacket(new SCCompletedQuestsPacket(completedQuests));
            return;
        }

        for (var i = 0; i < completedQuests.Length; i += 20)
        {
            var size = completedQuests.Length - i >= 200 ? 200 : completedQuests.Length - i;
            var result = new CompletedQuest[size];
            Array.Copy(completedQuests, i, result, 0, size);
            Owner.SendPacket(new SCCompletedQuestsPacket(result));
        }
    }

    public void ResetQuests(QuestDetail questDetail, bool sendIfChanged = true) => ResetQuests(new QuestDetail[] { questDetail }, sendIfChanged);

    private void ResetQuests(QuestDetail[] questDetail, bool sendIfChanged = true)
    {
        foreach (var (completeBlockId, completeBlock) in CompletedQuests)
        {
            for (var blockIndex = 0; blockIndex < 64; blockIndex++)
            {
                var questId = (uint)(completeBlockId * 64) + (uint)blockIndex;
                var q = QuestManager.Instance.GetTemplate(questId);
                // Skip unused Ids
                if (q == null)
                    continue;
                // Skip if quest still active
                if (HasQuest(questId))
                    continue;

                foreach (var qd in questDetail)
                {
                    if ((q.DetailId == qd) && (completeBlock.Body[blockIndex]))
                    {
                        completeBlock.Body.Set(blockIndex, false);
                        Logger.Info($"QuestReset by {Owner.Name}, reset {questId}");
                        if (sendIfChanged)
                        {
                            var body = new byte[8];
                            completeBlock.Body.CopyTo(body, 0);
                            Owner.SendPacket(new SCQuestContextResetPacket(questId, body, completeBlockId));
                        }
                    }
                }
            }
        }
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM completed_quests WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var quest = new CompletedQuest();
                    quest.Id = reader.GetUInt16("id");
                    quest.Body = new BitArray((byte[])reader.GetValue("data"));
                    CompletedQuests.Add(quest.Id, quest);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quests WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var quest = new Quest();
                    quest.Id = reader.GetUInt32("id");
                    quest.TemplateId = reader.GetUInt32("template_id");
                    quest.Status = (QuestStatus)reader.GetByte("status");
                    quest.ReadData((byte[])reader.GetValue("data"));
                    quest.Owner = Owner;
                    quest.Template = QuestManager.Instance.GetTemplate(quest.TemplateId);
                    quest.RecalcObjectives(false);
                    quest.CreateContextInstance();
                    //quest.RecallEvents();
                    ActiveQuests.Add(quest.TemplateId, quest);
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removed.Count > 0)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                var ids = string.Join(",", _removed);
                command.CommandText = $"DELETE FROM quests WHERE owner = @owner AND template_id IN({ids})";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            _removed.Clear();
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO completed_quests(`id`,`data`,`owner`) VALUES(@id,@data,@owner)";
            foreach (var quest in CompletedQuests.Values)
            {
                command.Parameters.AddWithValue("@id", quest.Id);
                var body = new byte[8];
                quest.Body.CopyTo(body, 0);
                command.Parameters.AddWithValue("@data", body);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();

                command.Parameters.Clear();
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO quests(`id`,`template_id`,`data`,`status`,`owner`) VALUES(@id,@template_id,@data,@status,@owner)";

            foreach (var quest in ActiveQuests.Values)
            {
                command.Parameters.AddWithValue("@id", quest.Id);
                command.Parameters.AddWithValue("@template_id", quest.TemplateId);
                command.Parameters.AddWithValue("@data", quest.WriteData());
                command.Parameters.AddWithValue("@status", (byte)quest.Status);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();

                command.Parameters.Clear();
            }
        }
    }

    public void CheckDailyResetAtLogin()
    {
        // TODO: Put Server timezone offset in configuration file, currently using local machine midnight
        // var utcDelta = DateTime.Now - DateTime.UtcNow;
        // var isOld = (DateTime.Today + utcDelta - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        var isOld = (DateTime.Today - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        if (isOld)
            ResetDailyQuests(false);
    }

    public void ResetDailyQuests(bool sendPacketsIfChanged)
    {
        Owner.Quests.ResetQuests(
            new QuestDetail[]
            {
                QuestDetail.Daily, QuestDetail.DailyGroup, QuestDetail.DailyHunt,
                QuestDetail.DailyLivelihood
            }, true
        );
    }

    public void RecallEvents()
    {
        foreach (var quest in Owner.Quests.ActiveQuests.Values)
        {
            quest.RecallEvents();
        }
    }
}
