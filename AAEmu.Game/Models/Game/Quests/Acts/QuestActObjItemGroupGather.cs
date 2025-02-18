﻿using System.Security.AccessControl;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGroupGather : QuestActTemplate
{
    public uint ItemGroupId { get; set; }
    public int Count { get; set; }
    public bool Cleanup { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public static int GroupGatherStatus { get; private set; } = 0;
    private int Objective { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActObjItemGroupGather");
        if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
        {
            GroupGatherStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = GroupGatherStatus + QuestActObjMonsterHunt.HuntStatus + QuestActObjMonsterGroupHunt.GroupHuntStatus + QuestActObjInteraction.InteractionStatus;

            if (quest.Template.LetItDone)
            {
                if (quest.OverCompletionPercent >= quest.Template.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > quest.Template.Score)
                    quest.ExtraCompletion = true;
            }

            Update();

            return quest.OverCompletionPercent >= quest.Template.Score;
        }

        if (quest.Template.LetItDone)
        {
            quest.OverCompletionPercent = objective * 100 / Count;

            if (quest.OverCompletionPercent >= 50)
                quest.EarlyCompletion = true;

            if (quest.OverCompletionPercent > 100)
                quest.ExtraCompletion = true;
        }

        Update();

        return objective >= Count;
    }
    public override void Update()
    {
        Objective++;
    }
    public override bool IsCompleted()
    {
        return Objective >= Count;
    }
    public override int GetCount()
    {
        Logger.Info("Получим, информацию на сколько выполнено задание.");

        return Objective;
    }
    public override void ClearStatus()
    {
        GroupGatherStatus = 0;
        Objective = 0;
        Logger.Info("Сбросили статус в ноль.");
    }
}
