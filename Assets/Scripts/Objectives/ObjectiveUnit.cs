/// <summary>
/// A unit an objective puts on the board (civilian, restless spirit...). It occupies a tile,
/// can be clicked, and can be targeted by actions that support it.
/// </summary>
public abstract class ObjectiveUnit : GridUnit
{
    public ObjectiveBase Owner { get; set; }

    /// <summary>Shown in the log when the player clicks it.</summary>
    public virtual string Describe() => DisplayName;
}
