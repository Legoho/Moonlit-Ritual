using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Objective 1 - Find the Lost Relic (book, wand, crystal, holy symbol...).
/// The relic is placed behind enemy lines, past the spawner farthest from the Anchor.
/// A heroine picks it up by walking onto or next to it (diagonals count). With "Must Return To Anchor"
/// she then has to bring it back next to the Anchor. If she is Downed, she drops it where she fell.
/// </summary>
public class LostRelicObjective : ObjectiveBase
{
    [Header("Relic")]
    [Tooltip("e.g. Lost Grimoire, Moonsilver Wand, Star Crystal, Holy Symbol")]
    public string relicName = "Lost Grimoire";
    [Tooltip("The relic's model. Optionally add a glow / light so it's easy to spot.")]
    [SerializeField] private GameObject relicPrefab;
    [Tooltip("Where the relic floats while carried, relative to the heroine.")]
    [SerializeField] private Vector3 carriedOffset = new Vector3(0f, 2.2f, 0f);
    [Tooltip("Height above the tile while it lies on the ground.")]
    [SerializeField] private float groundHeight = 0.3f;

    [Header("Rules")]
    [Tooltip("On: picking it up isn't enough, it must be carried back next to the Anchor.")]
    [SerializeField] private bool mustReturnToAnchor = true;
    [Tooltip("How far from the far spawner the relic may be placed.")]
    [SerializeField] private int searchRadius = 4;

    private GameObject relic;
    private Vector2Int relicTile;
    private Heroine carrier;

    private void Reset()
    {
        title = "Find the Lost Grimoire";
        description = "A grimoire was lost behind the enemy lines. Retrieve it and bring it back to the Anchor.";
        moonlightReward = 2;
        bonus = new ObjectiveBonus { insightAllEnemyTypes = 2 };
    }

    public override string StatusLine
    {
        get
        {
            if (State == ObjectiveState.Completed) return $"{relicName} recovered";
            if (carrier != null) return $"{carrier.DisplayName} carries the {relicName}" + (mustReturnToAnchor ? " - bring it to the Anchor" : "");
            return $"The {relicName} lies at {relicTile}";
        }
    }

    protected override void Setup()
    {
        carrier = null;
        if (!Manager.TryFindBehindEnemyLines(searchRadius, out relicTile))
        {
            Fail("There is nowhere to place it on this map.");
            return;
        }
        Manager.Reserve(relicTile);
        if (relicPrefab != null)
        {
            relic = Instantiate(relicPrefab);
            relic.name = relicName;
            PutOnGround(relicTile);
        }
    }

    protected override void Cleanup()
    {
        if (relic != null) Destroy(relic);
        relic = null;
        carrier = null;
    }

    public override void OnHeroineMoved(Heroine heroine, IReadOnlyList<Vector2Int> path)
    {
        if (carrier == null)
        {
            foreach (var t in path)
            {
                if (Chebyshev(t, relicTile) > 1) continue;
                PickUp(heroine);
                break;
            }
        }
        TryDeliver();
    }

    public override void OnRoundStart(int round) { DropIfDowned(); TryDeliver(); }
    public override void OnEnemyPhaseEnd() => DropIfDowned();

    private void PickUp(Heroine h)
    {
        carrier = h;
        if (relic != null)
        {
            relic.transform.SetParent(h.transform, false);
            relic.transform.localPosition = carriedOffset;
        }
        Announce($"{h.DisplayName} picks up the {relicName}!");
        if (!mustReturnToAnchor) Complete();
    }

    private void TryDeliver()
    {
        if (!mustReturnToAnchor || carrier == null || Manager.Anchor == null || !IsActive) return;
        if (carrier.IsDowned || Chebyshev(carrier.GridPosition, Manager.Anchor.GridPosition) > 1) return;

        Announce($"{carrier.DisplayName} returns the {relicName} to the circle.");
        if (relic != null) Destroy(relic, 0.5f);
        Complete();
    }

    private void DropIfDowned()
    {
        if (carrier == null || !carrier.IsDowned || !IsActive) return;
        relicTile = carrier.GridPosition;
        Announce($"{carrier.DisplayName} drops the {relicName}! Someone else can pick it up.");
        carrier = null;
        PutOnGround(relicTile);
    }

    private void PutOnGround(Vector2Int tile)
    {
        if (relic == null) return;
        relic.transform.SetParent(null);
        relic.transform.position = Grid.GridToWorld(tile) + Vector3.up * groundHeight;
    }
}
