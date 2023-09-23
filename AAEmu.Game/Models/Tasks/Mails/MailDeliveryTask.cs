﻿using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Mails;

class MailDeliveryTask : Task
{
    public override void Execute()
    {
        MailManager.Instance.CheckAllMailTimings();
    }
}
