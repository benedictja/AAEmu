﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Despawn : ICommand
{
    // Unused protected static Logger Logger = LogManager.GetCurrentClassLogger();
    public void OnLoad()
    {
        CommandManager.Instance.Register("despawn", this);
    }

    public string GetCommandLineHelp()
    {
        return "<npc||doodad> <<objId>||<templateId> <radius>>";
    }

    public string GetCommandHelpText()
    {
        return "Despawns a npc or doodad using by <objId> or <templateId> inside a <radius> range.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2)
        {
            character.SendMessage("[Despawn] " + CommandManager.CommandPrefix + "despawn " + GetCommandLineHelp());
            return;
        }

        string action = args[0];
        float radius = 30f;
        bool useRadius = (args.Length >= 3);
        if (!uint.TryParse(args[1], out var unitId))
        {
            character.SendMessage("|cFFFF0000[Despawn] Parse error unitId|r");
            return;
        }
        if ((args.Length >= 3) && (!float.TryParse(args[2], out radius)))
        {
            character.SendMessage("|cFFFF0000[Despawn] Parse error radius|r");
            return;
        }

        if (!useRadius)
        {
            switch (action)
            {
                case "doodad":
                    var myDoodad = WorldManager.Instance.GetDoodad(unitId);

                    if ((myDoodad != null) && (myDoodad is Doodad))
                    {
                        character.SendMessage("[Despawn] Removing Doodad with ID {0} - @DOODAD_NAME({1})", myDoodad.ObjId, myDoodad.TemplateId);
                        ObjectIdManager.Instance.ReleaseId(myDoodad.ObjId);
                        myDoodad.Delete();
                    }
                    else
                    {
                        character.SendMessage("|cFFFF0000[Despawn] Doodad with Id {0} Does not exist |r", unitId);
                    }
                    break;
                case "npc":
                    var myNPC = WorldManager.Instance.GetNpc(unitId);

                    if ((myNPC != null) && (myNPC is Npc))
                    {
                        character.SendMessage("[Despawn] Removing NPC with ID {0} - @NPC_NAME({1})", myNPC.ObjId, myNPC.TemplateId);
                        ObjectIdManager.Instance.ReleaseId(myNPC.ObjId);
                        myNPC.Delete();
                    }
                    else
                    {
                        character.SendMessage("|cFFFF0000[Despawn] NPC with objectId {0} don't exist|r", unitId);
                    }
                    break;
                case "unit":
                    var myUnit = WorldManager.Instance.GetBaseUnit(unitId);

                    if ((myUnit != null) && (myUnit is BaseUnit))
                    {
                        character.SendMessage("[Despawn] Removing Transfer with ID {0}", myUnit.ObjId);
                        ObjectIdManager.Instance.ReleaseId(myUnit.ObjId);
                        myUnit.Delete();
                    }
                    else
                    {
                        character.SendMessage("|cFFFF0000[Despawn] NPC with objectId {0} don't exist|r", unitId);
                    }
                    break;
                default:
                    character.SendMessage("|cFFFF0000[Despawn] Unknown object type|r");
                    break;
            }
        }
        else
        {
            var removedCount = 0;
            // Use radius
            switch (action)
            {
                case "doodad":
                    var myDoodads = WorldManager.GetAround<Doodad>(character, radius);

                    foreach (var doodad in myDoodads)
                    {
                        if (doodad.TemplateId == unitId)
                        {
                            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
                            doodad.Delete();
                            removedCount++;
                        }
                    }
                    character.SendMessage("[Despawn] Removed {0} Doodad(s) with TemplateID {1} - @DOODAD_NAME({1})", removedCount, unitId);
                    break;
                case "npc":
                    var myNPCs = WorldManager.GetAround<Npc>(character, radius);

                    foreach (var npc in myNPCs)
                    {
                        if (npc.TemplateId == unitId)
                        {
                            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
                            npc.Delete();
                            removedCount++;
                        }
                    }
                    character.SendMessage("[Despawn] Removed {0} NPC(s) with TemplateID {1} - @NPC_NAME({1})", removedCount, unitId);
                    break;
                default:
                    character.SendMessage("|cFFFF0000[Despawn] Unknown object type|r");
                    break;
            }
        }
    }
}
