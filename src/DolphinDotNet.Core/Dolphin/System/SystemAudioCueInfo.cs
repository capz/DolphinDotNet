using System;

namespace Dolphin.System;

/// <summary>Verified actions of the original persistent IPL score.</summary>
public enum SystemAudioCue
{
    TurnSide=0, TurnCenter=1, Select=2, Back=3, OptionCancel=4,
    OptionConfirm=5, OptionMove1=6, OptionMove2=7, OptionMove3=8,
    OptionSelect=9, PressStart=10, Error=11,
    /// <summary>Completion notification used after erasing or moving a save.</summary>
    CardOperationComplete=12,
    /// <summary>Completion notification used after copying or formatting.</summary>
    CardCopyComplete=13,
    /// <summary>Repeating transfer progress from slot A; Stop ends the effect.</summary>
    CardTransferFromA=14,
    /// <summary>Repeating transfer progress from slot B; Stop ends the effect.</summary>
    CardTransferFromB=15,
    CardCopyTransferComplete=16, CardMoveToBComplete=17, CardMoveToAComplete=18
}

/// <summary>Immutable metadata for a qualified complete score cue.</summary>
public sealed class SystemAudioCueInfo
{
    public SystemAudioCue Cue {get;}
    public string ProfileId {get;}
    /// <summary>True for authored progress loops which play until stopped.</summary>
    public bool RepeatsUntilStopped=>Cue==SystemAudioCue.CardTransferFromA||Cue==SystemAudioCue.CardTransferFromB;
    internal SystemAudioCueInfo(SystemAudioCue cue,string profile){Cue=cue;ProfileId=profile;}
    internal static void Validate(SystemAudioCue cue)
    {if((int)cue<0||(int)cue>18)throw new ArgumentOutOfRangeException(nameof(cue));}
}
