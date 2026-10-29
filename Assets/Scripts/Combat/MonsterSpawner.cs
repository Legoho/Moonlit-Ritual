using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Place on the board. At the start of every round it spawns one monster (by default)
/// from its own list, on its tile or the nearest free tile. It does not occupy a tile itself.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    public enum SpawnOrder { Random, Sequential }

    [Tooltip("Monster prefabs (with MonsterHolder) this spawner can produce.")]
    public List<MonsterHolder> monsterPrefabs = new();
    public SpawnOrder order = SpawnOrder.Random;
    public int monstersPerRound = 1;
    [Tooltip("First round this spawner is active (0 = from the start).")]
    public int firstSpawnRound = 0;
    [Tooltip("How far from its tile a monster may appear if the tile is taken.")]
    public int searchRadius = 3;

    private int nextIndex;
    private int spawnCount;

    public Vector2Int Tile => GridManager.Instance.WorldToGrid(transform.position);

    public void ResetSpawner()
    {
        nextIndex = 0;
        spawnCount = 0;
    }

    public List<MonsterHolder> SpawnForRound(int round)
    {
        var spawned = new List<MonsterHolder>();
        var grid = GridManager.Instance;
        if (grid == null || round < firstSpawnRound || monsterPrefabs.Count == 0) return spawned;

        int count = monstersPerRound + RunManager.Effects.extraSpawnsPerRound;
        for (int i = 0; i < count; i++)
        {
            var prefab = PickPrefab();
            if (prefab == null) continue;

            if (!grid.TryFindNearestFree(Tile, searchRadius, out var tile))
            {
                Debug.Log($"{name}: no free tile to spawn on this round.");
                break;
            }

            var monster = Instantiate(prefab, grid.GridToWorld(tile), Quaternion.identity);
            monster.name = $"{prefab.name} #{++spawnCount} ({name})";
            if (!monster.PlaceAt(tile))
            {
                Destroy(monster.gameObject);
                continue;
            }
            spawned.Add(monster);
        }
        return spawned;
    }

    private MonsterHolder PickPrefab()
    {
        if (order == SpawnOrder.Random)
            return monsterPrefabs[Random.Range(0, monsterPrefabs.Count)];

        var prefab = monsterPrefabs[nextIndex % monsterPrefabs.Count];
        nextIndex++;
        return prefab;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.7f, 0.2f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}
