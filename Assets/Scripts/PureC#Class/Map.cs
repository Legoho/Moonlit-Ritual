using System;
using UnityEngine;


public enum MapCellType
{
    Floor = 0,
    Empty,
    Wall,
    Spawn,
    Hazard,
    Ritual //3x3
}
[Serializable]
public class Map 
{
    [SerializeField, Min(7)]
    public int rows = 7;
    [SerializeField, Min(7)]
    public int columns = 7;

    [SerializeField]
    private Map_So[] mapTiles = new Map_So[49];

    public Map_So this[int row, int column]
    {
        get
        {
            int index = GetIndex(row, column);
            return mapTiles[index];
        }
        set
        {
            int index = GetIndex(row, column);
            mapTiles[index] = value;
        }
    }

    // NEW: lets callers check neighbours without catching exceptions.
    public bool InBounds(int row, int column)
    {
        return row >= 0 && row < rows && column >= 0 && column < columns;
    }

    // NEW: wipes the map to the given size (used before random generation).
    public void Clear(int newRows, int newColumns)
    {
        rows = Mathf.Max(1, newRows);
        columns = Mathf.Max(1, newColumns);
        mapTiles = new Map_So[rows * columns];
    }

    private int GetIndex(int row, int column)
    {
        if (row < 0 || row >= rows)
            throw new IndexOutOfRangeException(nameof(row));

        if (column < 0 || column >= columns)
            throw new IndexOutOfRangeException(nameof(column));

        return row * columns + column;
    }

    public void Resize(int newRows, int newColumns)
    {
        newRows = Mathf.Max(1, newRows);
        newColumns = Mathf.Max(1, newColumns);

        Map_So[] resizedTiles = new Map_So[newRows * newColumns];

        int copiedRows = Mathf.Min(rows, newRows);
        int copiedColumns = Mathf.Min(columns, newColumns);

        for (int row = 0; row < copiedRows; row++)
        {
            for (int column = 0; column < copiedColumns; column++)
            {
                int oldIndex = row * columns + column;
                int newIndex = row * newColumns + column;

                // FIX: if rows/columns were edited in the Inspector, the array
                // can be smaller than rows * columns says. Don't read past it.
                if (mapTiles == null || oldIndex >= mapTiles.Length)
                    continue;

                resizedTiles[newIndex] = mapTiles[oldIndex];
            }
        }

        rows = newRows;
        columns = newColumns;
        mapTiles = resizedTiles;
    }
}
